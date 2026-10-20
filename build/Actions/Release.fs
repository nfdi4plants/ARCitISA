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

/// What one release_electron_binaries matrix entry builds. Makers is None when electron-forge
/// runs every maker for the platform. Extensions are the files the release must contain.
type private ElectronTarget = {
    Makers: string list option
    Extensions: string list
}

let private electronTarget (os: string) (arch: string) : ElectronTarget =
    match os, arch with
    | "Windows", "x64" -> {
        Makers = None
        Extensions = [ ".exe"; ".nupkg" ]
      }
    | "macOS", ("x64" | "arm64") -> {
        Makers = None
        Extensions = [ ".zip"; ".dmg" ]
      }
    | "Linux", "x64" -> {
        Makers = None
        Extensions = [ ".deb"; ".rpm"; ".zip"; ".AppImage"; ".pkg.tar.zst" ]
      }
    // The pinned AppImage runtime and the pacman package are x64 only.
    | "Linux", "arm64" -> {
        Makers =
            Some [
                "@electron-forge/maker-zip"
                "@electron-forge/maker-deb"
                "@electron-forge/maker-rpm"
            ]
        Extensions = [ ".deb"; ".rpm"; ".zip" ]
      }
    | _ -> failwithf "No electron release target for %s %s" os arch

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
    file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)

let private verifyElectronMakeOutput (arch: string) (builtAfterUtc: DateTime) =

    let makeDir = Path.Combine(ProjectPaths.electronPath, "out", "make")

    if not (Directory.Exists makeDir) then
        failwithf "electron-forge make produced no output in %s" makeDir

    let os = currentRunnerOs ()
    let expected = (electronTarget os arch).Extensions

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
            "electron-forge make on %s %s is missing fresh files with extension %s in %s. Fresh files found: %s"
            os
            arch
            (String.Join(", ", missing))
            makeDir
            found

    for file in freshFiles do
        if expected |> List.exists (fun ext -> hasExtension ext file) then
            printGreenfn "Found electron binary: %s" file

/// Builds the Arch Linux package from the unpacked app that electron-forge leaves in out/.
let private pacmanPackage (version: Changelog.Version) (arch: string) =

    // pacman forbids '-' in pkgver. Dropping it turns 3.1.0-alpha.1 into 3.1.0alpha.1, which vercmp
    // sorts below 3.1.0. A '_' or '.' in its place would sort the prerelease above the release.
    let pkgver = version.Version.ToString().Replace("-", "")

    let outPath = Path.Combine(ProjectPaths.electronPath, "out")
    let appDir = Path.Combine(outPath, $"ARCitISA-linux-{arch}")

    if not (Directory.Exists appDir) then
        failwithf "Cannot build the pacman package, the packaged app is missing: %s" appDir

    let packagingDir = Path.Combine(ProjectPaths.electronPath, "packaging", "pacman")
    let iconsDir = Path.Combine(ProjectPaths.electronPath, "assets", "icons", "png")
    let stagingDir = Path.Combine(outPath, "pacman-build")
    let outDir = Path.Combine(outPath, "make", "pacman", arch)

    if Directory.Exists stagingDir then
        Directory.Delete(stagingDir, true)

    Directory.CreateDirectory stagingDir |> ignore
    Directory.CreateDirectory(Path.Combine(stagingDir, "icons")) |> ignore
    Directory.CreateDirectory outDir |> ignore

    for file in [ "PKGBUILD"; "arcitisa.desktop" ] do
        File.Copy(Path.Combine(packagingDir, file), Path.Combine(stagingDir, file))

    File.Copy(Path.GetFullPath "LICENSE", Path.Combine(stagingDir, "LICENSE"))

    for icon in Directory.GetFiles(iconsDir, "*.png") do
        File.Copy(icon, Path.Combine(stagingDir, "icons", Path.GetFileName icon))

    // makepkg needs Arch and refuses to run as root. docker cp instead of bind mounts keeps this
    // working when the build itself runs inside a container that talks to the host docker daemon.
    let container = $"arcitisa-pacman-{Guid.NewGuid():N}"

    let buildCommand =
        """useradd -m builder && mkdir -p /out && chown -R builder: /build /out && cd /build && runuser -u builder -- env ARCITISA_PKGVER="$ARCITISA_PKGVER" PKGDEST=/out makepkg --nodeps --noconfirm"""

    try
        run
            "docker"
            [
                "create"
                "--name"
                container
                "-e"
                $"ARCITISA_PKGVER={pkgver}"
                "archlinux:base-devel"
                "bash"
                "-c"
                buildCommand
            ]
            ""

        try
            run "docker" [ "cp"; $"{stagingDir}/."; $"{container}:/build" ] ""
            run "docker" [ "cp"; appDir; $"{container}:/build/ARCitISA-linux-x64" ] ""
            run "docker" [ "start"; "-a"; container ] ""
            run "docker" [ "cp"; $"{container}:/out/."; outDir ] ""
        finally
            run "docker" [ "rm"; "-f"; container ] ""
    finally
        Directory.Delete(stagingDir, true)

let electronBinaries (version: Changelog.Version) (arch: string) =

    let builtAfterUtc = DateTime.UtcNow.AddSeconds(-2.0)
    let target = electronTarget (currentRunnerOs ()) arch

    run "npm" [ "run"; "fable" ] ProjectPaths.electronPath

    let makeArgs = [
        "scripts/forge-make.cjs"
        sprintf "--arch=%s" arch
        match target.Makers with
        | Some makers -> sprintf "--targets=%s" (String.Join(",", makers))
        | None -> ()
    ]

    run "node" makeArgs ProjectPaths.electronPath

    if target.Extensions |> List.contains ".pkg.tar.zst" then
        pacmanPackage version arch

    // forge-make.cjs turns a killed make into a failure. A make run can also exit 0 without writing
    // every installer, so verifyElectronMakeOutput looks in out/make for each expected extension (#1421).
    verifyElectronMakeOutput arch builtAfterUtc

    ()

let verifyReleaseAssets (dir: string) =

    // Must match the artifact names uploaded by the electron-web target of the release job and by
    // the release_electron_binaries matrix in .github/workflows/release-pipeline.yml.
    let electronMatrix = [
        "Windows", "x64"
        "macOS", "x64"
        "macOS", "arm64"
        "Linux", "x64"
        "Linux", "arm64"
    ]

    let expectedArtifacts = [
        "release-assets-client", [ ".zip" ]
        yield!
            electronMatrix
            |> List.map (fun (os, arch) -> $"release-assets-electron-{os}-{arch}", (electronTarget os arch).Extensions)
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
