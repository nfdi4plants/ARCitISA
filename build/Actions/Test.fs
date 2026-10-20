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

    let components =
        runAsync "components" "npm" [ "run"; "test:ci" ] ProjectPaths.componentTestsPath

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

            match serverResult, clientResult, electronCoreResult, electronRendererResult, componentsResult with
            | Ok(), Ok(), Ok(), Ok(), Ok() ->
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

                exit 1

        }
        |> Async.RunSynchronously
