[<RequireQualifiedAccessAttribute>]
module Release

open SimpleExec
open ProjectInfo
open System
open System.IO

let npm (version: Changelog.Version) (isDryRun: bool) =

    let isPrerelease = version.Version.IsPrerelease

    try
        run
            "npm"
            [
                "view"
                $"{ProjectInfo.componentsPackageName}@{version.Version.ToString()}"
            ]
            ProjectPaths.componentsPath

        printGreenfn
            $"Package {ProjectInfo.componentsPackageName} version {version.Version.ToString()} already published to npmjs.org, skipping publish step."
    with _ ->
        printGreenfn
            $"Publishing package {ProjectInfo.componentsPackageName} version {version.Version.ToString()} to npmjs.org."

        let version = VersionIO.updateComponentsPackageJSONVersion version

        printGreenfn "Start building npm package"

        let build = run "npm" [ "run"; "build" ] ProjectPaths.componentsPath

        printGreenfn "Start updating .css file to use @layer swt-base instead of @layer base"

        let cssFilePath =
            Path.Combine(ProjectPaths.componentsPath, "dist", "assets", "swate-components.css")

        if not (System.IO.File.Exists(cssFilePath)) then
            failwithf "CSS file not found at %s" cssFilePath

        let content = System.IO.File.ReadAllText(cssFilePath)
        let replacedContent = content.Replace("@layer base", "@layer swt-base")
        System.IO.File.WriteAllText(cssFilePath, replacedContent)

        printGreenfn "Start publishing npm package"

        let publish =
            run
                "npm"
                [
                    "publish"
                    "--access"
                    "public"
                    "--provenance"
                    if isPrerelease then
                        "--tag"
                        "next"
                    if isDryRun then
                        "--dry-run"
                ]
                ProjectPaths.componentsPath

        ()

    ()

let nuget (key: string) (isDryRun: bool) =

    let mkCssFile = run "npm" [ "run"; "prebuild:net" ] ProjectPaths.componentsPath

    let pack =
        run
            "dotnet"
            [
                "pack"
                ProjectPaths.nugetSln
                "--configuration"
                "Release"
                "--output"
                ProjectPaths.nugetDeployPath
            ]
            ""

    let publish =
        if not isDryRun then
            run
                "dotnet"
                [
                    "nuget"
                    "push"
                    $"{ProjectPaths.nugetDeployPath}/*.nupkg"
                    "--api-key"
                    key
                    "--skip-duplicate"
                    "--source"
                    "https://api.nuget.org/v3/index.json"
                ]
                ""

    ()

let docker (username: string) (key: string) (version: Changelog.Version) (isDryRun: bool) =

    VersionIO.updateVersionFiles version
    VersionIO.updateFSharpProjectVersions version

    let dockerRegistryTarget = "ghcr.io"
    let imageName = "ghcr.io/nfdi4plants/swate"

    let login =
        run
            "docker"
            [
                "login"
                "--username"
                username
                // --pasword-stdin requires a completly different setup. this needs to be checked.
                // https://docs.docker.com/reference/cli/docker/login/#password-stdin
                "--password" // ⚠️
                key
                dockerRegistryTarget
            ]
            ""

    let isPrerelease = version.Version.IsPrerelease

    let imageVersioned = $"{imageName}:{version.Version.ToString()}"
    let imageLatest = $"{imageName}:latest"
    let imageNext = $"{imageName}:next"

    // docker build -t ghcr.io/nfdi4plants/swate:next -t ghcr.io/nfdi4plants/swate:2.0.0 -t ghcr.io/nfdi4plants/swate:latest -f ./build/Dockerfile.publish .

    let build =
        run
            "docker"
            [
                "build"
                "-t"
                imageNext
                if not isPrerelease then
                    "-t"
                    imageVersioned
                    "-t"
                    imageLatest
                "-f"
                ProjectPaths.dockerFilePath
                "."
            ]
            ""

    let push =
        if not isDryRun then
            run "docker" [ "push"; imageNext ] ""

            if not isPrerelease then
                run "docker" [ "push"; imageVersioned ] ""
                run "docker" [ "push"; imageLatest ] ""

    ()

open System.IO
open System.IO.Compression

/// Builds the frontend and zips it for the GitHub release
let electron () =

    let sourceDir = Path.Combine(ProjectPaths.deployPath, "public")
    let targetZip = "./SwateClient.zip"

    Bundle.Client(false)

    if File.Exists(targetZip) then
        File.Delete(targetZip)

    ZipFile.CreateFromDirectory(sourceDir, targetZip, CompressionLevel.Optimal, includeBaseDirectory = false)

    ()

/// File extensions electron-forge make produces per GitHub runner.os name.
let private electronMakeExtensions (os: string) : string list =
    match os with
    | "Windows" -> [ ".exe"; ".nupkg" ]
    | "macOS" -> [ ".zip"; ".dmg" ]
    | "Linux" -> [ ".deb"; ".rpm"; ".zip" ]
    | other -> failwithf "Unknown runner OS: %s" other

let private currentRunnerOs () : string =
    if OperatingSystem.IsWindows() then
        "Windows"
    elif OperatingSystem.IsMacOS() then
        "macOS"
    elif OperatingSystem.IsLinux() then
        "Linux"
    else
        failwith "Unsupported operating system for electron binaries."

let private hasExtension (ext: string) (file: string) =
    String.Equals(Path.GetExtension file, ext, StringComparison.OrdinalIgnoreCase)

let private verifyElectronMakeOutput (builtAfterUtc: DateTime) =

    let makeDir = Path.Combine(ProjectPaths.electronPath, "out", "make")

    if not (Directory.Exists makeDir) then
        failwithf "electron-forge make produced no output in %s" makeDir

    let os = currentRunnerOs ()
    let expected = electronMakeExtensions os

    let freshFiles =
        Directory.GetFiles(makeDir, "*", SearchOption.AllDirectories)
        |> Array.filter (fun file -> File.GetLastWriteTimeUtc file >= builtAfterUtc)

    let missing =
        expected
        |> List.filter (fun ext -> not (freshFiles |> Array.exists (hasExtension ext)))

    if not missing.IsEmpty then
        let found =
            if freshFiles.Length = 0 then
                "none"
            else
                String.Join(", ", freshFiles)

        failwithf
            "electron-forge make on %s is missing fresh files with extension %s in %s. Fresh files found: %s"
            os
            (String.Join(", ", missing))
            makeDir
            found

    for file in freshFiles do
        if expected |> List.exists (fun ext -> hasExtension ext file) then
            printGreenfn "Found electron binary: %s" file

let electronBinaries (arch: string) =

    let builtAfterUtc = DateTime.UtcNow.AddSeconds(-2.0)

    run "npm" [ "run"; "fable" ] ProjectPaths.electronPath

    run "node" [ "scripts/forge-make.cjs"; sprintf "--arch=%s" arch ] ProjectPaths.electronPath

    // forge-make.cjs turns a killed make into a failure. A make run can also exit 0 without writing
    // every installer, so verifyElectronMakeOutput looks in out/make for each expected extension (#1421).
    verifyElectronMakeOutput builtAfterUtc

    ()

let verifyReleaseAssets (dir: string) =

    // Must match the artifact names uploaded by the electron-web target of the release job and by
    // the release_electron_binaries matrix in .github/workflows/release-pipeline.yml.
    let expectedArtifacts = [
        "release-assets-client", [ ".zip" ]
        "release-assets-electron-Windows-x64", electronMakeExtensions "Windows"
        "release-assets-electron-macOS-x64", electronMakeExtensions "macOS"
        "release-assets-electron-macOS-arm64", electronMakeExtensions "macOS"
        "release-assets-electron-Linux-x64", electronMakeExtensions "Linux"
    ]

    let problems =
        expectedArtifacts
        |> List.choose (fun (name, extensions) ->
            let artifactDir = Path.Combine(dir, name)

            if not (Directory.Exists artifactDir) then
                Some(sprintf "%s: directory not found in %s" name dir)
            else
                let files = Directory.GetFiles(artifactDir, "*", SearchOption.AllDirectories)

                let missing =
                    extensions
                    |> List.filter (fun ext -> not (files |> Array.exists (hasExtension ext)))

                if missing.IsEmpty then
                    None
                else
                    Some(sprintf "%s: no file with extension %s" name (String.Join(", ", missing)))
        )

    if not problems.IsEmpty then
        failwithf "Release assets in %s are incomplete:\n%s" dir (String.Join("\n", problems))

    printGreenfn "Verified %i release artifacts in %s" expectedArtifacts.Length dir

let storybook () =
    run "npm" [ "run"; "build:storybook" ] ProjectPaths.componentsPath
