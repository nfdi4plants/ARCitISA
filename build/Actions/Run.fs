[<RequireQualifiedAccessAttribute>]
module Run

open ProjectInfo

let ClientArgs = [
    "fable"
    "watch"
    "-o"
    "output"
    "-s"
    "-e"
    "fs.js"
    yield! DEFINE_ARCITISA_ENVIRONMENT_FABLE
    "--run"
    "npx"
    "vite"
    "--debug"
    "transform"
]

let createDevCertsForExcelAddIn () =
    run "npx" [| "office-addin-dev-certs"; "install"; "--days"; "365" |] ""

    let userProfile =
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile)

    let certPath = System.IO.Path.Combine(userProfile, ".office-addin-dev-certs/ca.crt")

    let psi =
        new System.Diagnostics.ProcessStartInfo(FileName = certPath, UseShellExecute = true)

    System.Diagnostics.Process.Start(psi) |> ignore

let All (db: bool) (isExcel: bool) =

    [
        runAsync "server" "dotnet" [ "watch"; "run" ] ProjectPaths.serverPath
        runAsync "client" "dotnet" ClientArgs ProjectPaths.clientPath
        if db then
            runAsync
                "database"
                "docker-compose"
                [ "-f"; ProjectPaths.dockerComposePath; "up"; "-d" ]
                __SOURCE_DIRECTORY__
        if isExcel then
            runAsync
                "excel"
                "npx"
                [
                    "office-addin-debugging"
                    "start"
                    ProjectPaths.devManifestPath
                    "desktop"
                    "--debug-method"
                    "web"
                ]
                ""
    ]
    |> runParallel
