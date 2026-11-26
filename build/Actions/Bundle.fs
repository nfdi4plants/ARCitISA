[<RequireQualifiedAccessAttribute>]
module Bundle

open ProjectInfo

let Client (forArcitisa: bool) =

    run
        "dotnet"
        [
            "fable"
            "-o"
            "output"
            "-s"
            "-e"
            "fs.js"
            if forArcitisa then
                yield! DEFINE_ARCITISA_ENVIRONMENT_FABLE
            "--run"
            "npx"
            "vite"
            "build"
        ]
        ProjectPaths.clientPath

let All () =
    [
        runAsync
            "server"
            "dotnet"
            [
                "publish"
                "-c"
                "Release"
                "-o"
                ProjectPaths.deployPath
            ]
            ProjectPaths.serverPath
        runAsync
            "client"
            "dotnet"
            [
                "fable"
                "-o"
                "output"
                "-s"
                "-e"
                "fs.js"
                yield! DEFINE_ARCITISA_ENVIRONMENT_FABLE
                "--run"
                "npx"
                "vite"
                "build"
            ]
            ProjectPaths.clientPath
    ]
    |> runParallel
