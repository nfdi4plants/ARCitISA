[<RequireQualifiedAccessAttribute>]
module Test

open ProjectInfo
open System.IO

let private cleanGeneratedOutput (testProjectPath: string) =
    let outputPath = Path.Combine(testProjectPath, "output")

    if Directory.Exists outputPath then
        Directory.Delete(outputPath, true)

let buildSharedTests () =
    run "dotnet" [ "build" ] ProjectPaths.sharedTestsPath

/// This disables microsoft data collection for using `office-addin-mock`
let disableUserData () =
    run "npx" [ "office-addin-usage-data"; "off" ] __SOURCE_DIRECTORY__

let Watch () =
    cleanGeneratedOutput ProjectPaths.clientTestsPath
    cleanGeneratedOutput ProjectPaths.electronCoreTestsPath
    cleanGeneratedOutput ProjectPaths.electronRendererTestsPath

    [
        runAsync "server" "dotnet" [ "watch"; "run" ] ProjectPaths.serverTestsPath
        // This below will start web ui for tests, but cannot execute due to office-addin-mock
        runAsync
            "client"
            "dotnet"
            [
                "fable"
                "watch"
                "-o"
                "output"
                "-s"
                yield! DEFINE_SWATE_ENVIRONMENT_FABLE
                "--run"
                "npx"
                "mocha"
                $"{ProjectPaths.clientTestsPath}/output/Client.Tests.js"
                "--watch"
            ]
            ProjectPaths.clientTestsPath
        runAsync
            "electron-core"
            "dotnet"
            [
                "fable"
                "watch"
                "-o"
                "output"
                "-s"
                yield! DEFINE_SWATE_ENVIRONMENT_FABLE
                "--run"
                "npx"
                "vitest"
            ]
            ProjectPaths.electronCoreTestsPath
        runAsync
            "electron-renderer"
            "dotnet"
            [
                "fable"
                "watch"
                "-o"
                "output"
                "-s"
                yield! DEFINE_SWATE_ENVIRONMENT_FABLE
                "--run"
                "npx"
                "vitest"
            ]
            ProjectPaths.electronRendererTestsPath
        runAsync "components" "npm" [ "run"; "test" ] ProjectPaths.componentTestsPath
    ]
    |> runParallel

let WatchJs () =
    cleanGeneratedOutput ProjectPaths.clientTestsPath
    cleanGeneratedOutput ProjectPaths.electronCoreTestsPath
    cleanGeneratedOutput ProjectPaths.electronRendererTestsPath

    [
        runAsync
            "client"
            "dotnet"
            [
                "fable"
                "watch"
                "-o"
                "output"
                "-s"
                yield! DEFINE_SWATE_ENVIRONMENT_FABLE
                "--run"
                "npx"
                "mocha"
                $"{ProjectPaths.clientTestsPath}/output/Client.Tests.js"
                "--watch"
                "--parallel"
            ]
            ProjectPaths.clientTestsPath
        runAsync
            "electron-core"
            "dotnet"
            [
                "fable"
                "watch"
                "-o"
                "output"
                "-s"
                yield! DEFINE_SWATE_ENVIRONMENT_FABLE
                "--run"
                "npx"
                "vitest"
            ]
            ProjectPaths.electronCoreTestsPath
        runAsync
            "electron-renderer"
            "dotnet"
            [
                "fable"
                "watch"
                "-o"
                "output"
                "-s"
                yield! DEFINE_SWATE_ENVIRONMENT_FABLE
                "--run"
                "npx"
                "vitest"
            ]
            ProjectPaths.electronRendererTestsPath
    ]
    |> runParallel

module Run =
    let server = runAsync "server" "dotnet" [ "run" ] ProjectPaths.serverTestsPath

    let client =
        cleanGeneratedOutput ProjectPaths.clientTestsPath

        runAsync
            "client"
            "dotnet"
            [
                "fable"
                "-o"
                "output"
                "-s"
                yield! DEFINE_SWATE_ENVIRONMENT_FABLE
                "--run"
                "npx"
                "mocha"
                $"{ProjectPaths.clientTestsPath}/output/Client.Tests.js"
            ]
            ProjectPaths.clientTestsPath

    let electronCore =
        cleanGeneratedOutput ProjectPaths.electronCoreTestsPath

        runAsync
            "electron-core"
            "dotnet"
            [
                "fable"
                "-o"
                "output"
                "-s"
                yield! DEFINE_SWATE_ENVIRONMENT_FABLE
                "--run"
                "npx"
                "vitest"
                "run"
            ]
            ProjectPaths.electronCoreTestsPath

    let electronRenderer =
        cleanGeneratedOutput ProjectPaths.electronRendererTestsPath

        runAsync
            "electron-renderer"
            "dotnet"
            [
                "fable"
                "-o"
                "output"
                "-s"
                yield! DEFINE_SWATE_ENVIRONMENT_FABLE
                "--run"
                "npx"
                "vitest"
                "run"
            ]
            ProjectPaths.electronRendererTestsPath

    // Storybook runs as its own command because a single combined run with parallel Storybook was flaky.
    // Both vitest runs always execute so CI reports both results.
    let components = async {
        let dir = ProjectPaths.componentTestsPath
        let! cleanResult = runAsync "components" "npm" [ "run"; "clean" ] dir

        match cleanResult with
        | Error _ -> return cleanResult
        | Ok() ->
            let! fableResult = runAsync "components" "npm" [ "run"; "fable"; "--"; "--noCache" ] dir

            match fableResult with
            | Error _ -> return fableResult
            | Ok() ->
                let! unitRtlResult =
                    runAsync "components" "npx" [ "vitest"; "run"; "--project=unit"; "--project=rtl" ] dir

                let! storybookResult =
                    runAsync
                        "components"
                        "npx"
                        [
                            "vitest"
                            "run"
                            "--project=storybook"
                            "--fileParallelism"
                            "--maxWorkers=3"
                        ]
                        dir

                match unitRtlResult, storybookResult with
                | Ok(), Ok() -> return Ok()
                | Error e, _
                | _, Error e -> return Error e
    }

    // Isolated suites for the patched Fable.SimpleJson and Fable.Remoting.Client sources.
    // Remove them with the compatibility projects, see src/Fable.SimpleJson.Compatibility/README.md.
    // Both suites always execute so CI reports both results.
    let compatibility = async {
        let runSuite prefix path =
            cleanGeneratedOutput path

            runAsync
                prefix
                "dotnet"
                [
                    "fable"
                    "-o"
                    "output"
                    "-s"
                    "--run"
                    "npx"
                    "vitest"
                    "run"
                ]
                path

        let! simpleJsonResult = runSuite "simplejson" ProjectPaths.simpleJsonCompatibilityTestsPath
        let! remotingResult = runSuite "remoting" ProjectPaths.remotingCompatibilityTestsPath

        match simpleJsonResult, remotingResult with
        | Ok(), Ok() -> return Ok()
        | Error e, _
        | _, Error e -> return Error e
    }

    let All () =
        async {
            printGreenfn "Running all tests..."
            printGreenfn "Running server tests..."
            let! serverResult = server
            printGreenfn "Running client tests..."
            let! clientResult = client
            printGreenfn "Running electron core tests..."
            let! electronCoreResult = electronCore
            printGreenfn "Running electron renderer tests..."
            let! electronRendererResult = electronRenderer
            printGreenfn "Running component tests..."
            let! componentsResult = components
            printGreenfn "Running compatibility tests..."
            let! compatibilityResult = compatibility

            match
                serverResult,
                clientResult,
                electronCoreResult,
                electronRendererResult,
                componentsResult,
                compatibilityResult
            with
            | Ok(), Ok(), Ok(), Ok(), Ok(), Ok() ->
                printGreenfn "All tests passed!"
                exit 0
            | _ ->
                if serverResult.IsError then
                    printRedfn "Server tests failed."

                if clientResult.IsError then
                    printRedfn "Client tests failed."

                if electronCoreResult.IsError then
                    printRedfn "Electron core tests failed."

                if electronRendererResult.IsError then
                    printRedfn "Electron renderer tests failed."

                if componentsResult.IsError then
                    printRedfn "Component tests failed."

                if compatibilityResult.IsError then
                    printRedfn "Compatibility tests failed."

                exit 1

        }
        |> Async.RunSynchronously
