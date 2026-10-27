open System
open System.IO
open ProjectInfo

[<EntryPoint>]
let main args =
    let argv = args |> Array.map (fun x -> x.ToLower()) |> Array.toList

    match argv with
    | "create-certs" :: _ ->
        Run.createDevCertsForExcelAddIn ()
        0
    | "bundle" :: a ->
        run "npm" [ "install" ] "."

        match a with
        | "client" :: a ->
            match a with
            | "standalone" :: _ ->
                Bundle.Client(false)
                0
            | _ ->
                Bundle.Client(true)
                0
        | _ ->
            run "dotnet" [ "restore"; "./Swate.sln" ] "."
            Bundle.All()
            0
    | "run" :: a ->
        let runDb: bool = a |> List.contains "db"
        let runExcel: bool = a |> List.contains "excel"
        Run.All runDb runExcel
        0
    | "tests" :: a
    | "test" :: a ->
        Test.disableUserData ()
        Test.buildSharedTests ()

        match a with
        | "run" :: "server" :: _ ->
            match Test.Run.server |> Async.RunSynchronously with
            | Ok() -> 0
            | Error _ -> 1
        | "run" :: "client" :: _ ->
            match Test.Run.client |> Async.RunSynchronously with
            | Ok() -> 0
            | Error _ -> 1
        | "run" :: "electron-core" :: _ ->
            match Test.Run.electronCore |> Async.RunSynchronously with
            | Ok() -> 0
            | Error _ -> 1
        | "run" :: "electron-renderer" :: _ ->
            match Test.Run.electronRenderer |> Async.RunSynchronously with
            | Ok() -> 0
            | Error _ -> 1
        | "run" :: "components" :: _ ->
            match Test.Run.components |> Async.RunSynchronously with
            | Ok() -> 0
            | Error _ -> 1
        | "run" :: "compatibility" :: _ ->
            match Test.Run.compatibility |> Async.RunSynchronously with
            | Ok() -> 0
            | Error _ -> 1
        | "watch" :: _ ->
            Test.Watch()
            0
        | "js" :: _ ->
            Test.WatchJs()
            0
        | _ ->
            Test.Run.All()
            0
    | "release" :: target :: otherArgs ->
        let latestVersion = Changelog.getLatestVersion ()
        let isDryRun = otherArgs |> List.contains "--dry-run"
        let isCi = otherArgs |> List.contains "--ci"

        if not isCi && not isDryRun then
            printRedfn "Currently the worklow only supports CI releases!"
            exit 1

        VersionIO.updateAllVersionInformationInFiles latestVersion

        match target with
        | "nuget" ->

            printGreenfn ("Starting Nuget release!")

            let key = getEnvironementVariableOrFail "NUGET_KEY"

            Release.nuget key isDryRun

            printGreenfn ("Release nuget!")
            0
        | "npm" ->

            printGreenfn ("Starting NPM release!")

            Release.npm latestVersion isDryRun

            printGreenfn "Released npm package version %O" latestVersion.Version
            0
        | "docker" ->
            printGreenfn ("Starting Docker release!")

            let key = getEnvironementVariableOrFail "DOCKER_KEY"

            let user =
                otherArgs
                |> List.tryFind (fun x -> x.StartsWith "--user=")
                |> Option.map (fun x -> x.Trim().Replace("--user=", ""))

            if user.IsNone then
                printRedfn "No docker user set! Please pass user in the format --user=yourusername"
                exit 1

            Release.docker user.Value key latestVersion isDryRun

            printGreenfn ("Release docker!")
            0
        // electron-web builds the prebundled web client frontend,
        // historically used as an iframe in an external Electron app.
        | "electron-web" ->
            Release.electron ()
            printGreenfn ("Release electron-web!")
            0
        | "electron-bin" ->

            let arch =
                argv
                |> List.tryFind (fun x -> x.StartsWith "--arch=")
                |> Option.map (fun x -> x.Trim().Replace("--arch=", ""))

            if arch.IsNone then
                printRedfn "No --arch provided for electron-bin target!"
                exit 1

            Release.electronBinaries latestVersion arch.Value
            printGreenfn "Release electron-bin for arch %s!" arch.Value
            0
        | "storybook" ->
            printGreenfn "Starting Storybook release!"
            Release.storybook ()
            0
        | _ ->
            printRedfn ("No valid release target provided!")
            1
    | "prerelease" :: _ ->

        let GitHubToken = getEnvironementVariableOrFail "GITHUB_TOKEN"

        let latestVersion = Changelog.getLatestVersion ()
        let tags = Git.getTags () |> Array.toList |> List.distinct

        let nextTag = latestVersion.Version.ToString()

        GHActions.setVersion nextTag

        let releaseAlreadyExists = tags |> List.contains (nextTag)

        if releaseAlreadyExists then
            printGreenfn
                "The latest version %O from CHANGELOG.md is already tagged in git. No release needed."
                latestVersion.Version

            GHActions.setShouldSkip ()
            0
        else
            printGreenfn "The latest version %O from CHANGELOG.md is not yet tagged in git." latestVersion.Version

            match GitHub.tryGetLatestRelease GitHubToken latestVersion with
            | Some _ -> printGreenfn "Release for version %O already exists on GitHub." latestVersion.Version
            | None -> GitHub.mkRelease GitHubToken latestVersion |> ignore

            0
    | "postrelease" :: dir :: _ ->

        // Check the assets first, so an incomplete set fails before the release is published and tagged.
        Release.verifyReleaseAssets dir

        let GitHubToken = getEnvironementVariableOrFail "GITHUB_TOKEN"
        let latestVersion = Changelog.getLatestVersion ()

        GitHub.uploadReleaseAssets GitHubToken latestVersion dir

        let isPrerelease = latestVersion.Version.IsPrerelease

        GitHub.updateRelease
            GitHubToken
            latestVersion
            (fun _ -> {
                GitHub.UpdateReleaseRequest.Empty with
                    draft = Some false
                    prerelease = Some isPrerelease
                    make_latest = Some "true"
            })
        |> ignore

        let nextTag = latestVersion.Version.ToString()

        Git.createTagAndPush (nextTag)

        0
    | "upload-release-assets" :: dir :: _ ->

        let GitHubToken = getEnvironementVariableOrFail "GITHUB_TOKEN"
        let latestVersion = Changelog.getLatestVersion ()

        GitHub.uploadReleaseAssets GitHubToken latestVersion dir

        0
    | "dev" :: a ->
        let latestVersion = Changelog.getLatestVersion ()
        0
    | _ ->
        Console.WriteLine("No valid argument provided. Please provide a valid target.")
        1
