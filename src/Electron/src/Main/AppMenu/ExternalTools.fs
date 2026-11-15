module Main.ArcExternalTools

open Fable.Core
open Fable.Electron.Main
open Main.Bindings

type VSCodeInstallationProbe = {
    GetApplicationPath: unit -> JS.Promise<string>
    GetHandlerName: unit -> string
    VerifyCommand: unit -> JS.Promise<unit>
}

let isVSCodeAvailableWith (probe: VSCodeInstallationProbe) platform = promise {
    try
        if platform = "linux" then
            // Electron exposes the desktop handler name on Linux, but not its application path.
            if System.String.IsNullOrWhiteSpace(probe.GetHandlerName()) then
                return false
            else
                do! probe.VerifyCommand()
                return true
        else
            let! applicationPath = probe.GetApplicationPath()

            return
                not (System.String.IsNullOrWhiteSpace applicationPath)
                && Filesystem.existsSync applicationPath
    with _ ->
        return false
}

/// Check both the URL registration used to open the ARC and the installed application.
let isVSCodeAvailable () =
    isVSCodeAvailableWith
        {
            GetApplicationPath =
                fun () -> promise {
                    let! application = app.getApplicationInfoForProtocol "vscode://"
                    return application.path
                }
            GetHandlerName = fun () -> app.getApplicationNameForProtocol "vscode://"
            VerifyCommand = fun () -> ChildProcess.run "code" [| "--version" |] (Node.homeDirectory ())
        }
        (Node.processPlatform ())

let vsCodeFolderUrl arcPath =
    let url = Node.pathToFileUrl arcPath

    let path =
        if url.host = "" then
            url.pathname
        else
            $"//{url.host}{url.pathname}"

    "vscode://file" + path.TrimEnd('/') + "/"

let openInVSCode arcPath =
    shell.openExternal (vsCodeFolderUrl arcPath)

let openInFileManager arcPath = promise {
    let! error = shell.openPath arcPath

    if not (System.String.IsNullOrWhiteSpace error) then
        failwith error
}

type TerminalLaunchers = {
    StartDetached: string -> string[] -> string -> JS.Promise<unit>
    Run: string -> string[] -> string -> JS.Promise<unit>
}

let openTerminalWith (launchers: TerminalLaunchers) platform arcPath =
    match platform with
    | "win32" -> promise {
        try
            // Match Explorer's "Open in Terminal": let Windows Terminal choose the user's default profile.
            // Use cwd so Terminal does not parse semicolons in folder names as command separators.
            do! launchers.StartDetached "wt.exe" [| "-d"; "." |] arcPath
        with error ->
            if Filesystem.tryGetNodeErrorCode error = Some "ENOENT" then
                failwith
                    "Windows Terminal could not be found. Install Windows Terminal or enable its wt.exe app execution alias."
            else
                return raise error
      }
    | "darwin" -> launchers.Run "/usr/bin/open" [| "-a"; "Terminal"; arcPath |] arcPath
    | "linux" ->
        let rec tryLaunch candidates = promise {
            match candidates with
            | [] -> return failwith "No terminal application was found. Install a terminal emulator and try again."
            | (command, args) :: remaining ->
                try
                    do! launchers.StartDetached command args arcPath
                with error ->
                    if Filesystem.tryGetNodeErrorCode error = Some "ENOENT" then
                        return! tryLaunch remaining
                    else
                        return raise error
        }

        tryLaunch [
            "x-terminal-emulator", [||]
            "gnome-terminal", [| "--working-directory"; arcPath |]
            "konsole", [| "--workdir"; arcPath |]
            "xfce4-terminal", [| "--working-directory"; arcPath |]
            "xterm", [||]
        ]
    | _ -> promise { return failwith $"Opening a terminal is not supported on {platform}." }

let openTerminal arcPath =
    openTerminalWith
        {
            StartDetached = ChildProcess.startDetached
            Run = ChildProcess.run
        }
        (Node.processPlatform ())
        arcPath
