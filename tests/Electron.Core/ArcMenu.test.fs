module ElectronCore.ArcMenuTests

open Fable.Core
open Fable.Electron.Main
open Main
open Vitest

type private ElectronMock =
    abstract reset: unit -> unit
    abstract protocolLookupCount: unit -> int
    abstract setOpenExternal: (string -> JS.Promise<unit>) -> unit
    abstract setOpenPath: (string -> JS.Promise<string>) -> unit
    abstract focusWindow: BrowserWindow -> unit
    abstract focusListenerCount: unit -> int

[<Import("__electronMock", "electron")>]
let private mock: ElectronMock = jsNative

[<Emit("$0.setShowMessageBox((window, options) => $1([window, options]))")>]
let private setMessageBox
    (_mock: ElectronMock)
    (_callback: BaseWindow option * Dialog.ShowMessageBox.Options -> obj)
    : unit =
    jsNative

[<Emit("$0.click({}, $1, {})")>]
let private click (_item: MenuItem) (_window: BaseWindow option) : unit = jsNative

[<Emit("Object.assign(new Error($0), { code: $1 })")>]
let private nodeError (message: string) (code: string) : exn = jsNative

[<Emit("process.execPath")>]
let private processExecutable () : string = jsNative

let private noAction _ = promise { return () }

let private actions: ArcMenu.Actions = {
    IsVSCodeAvailable = fun () -> promise { return true }
    OpenInVSCode = noAction
    OpenTerminal = noAction
    OpenInFileManager = noAction
}

let private buildMenu actions =
    Menu.buildFromTemplate [| U2.Case1(ArcMenu.createWith actions) |]

let private getItem (menu: Menu) id = menu.getMenuItemById(id).Value

Vitest.afterEach (fun () ->
    mock.reset ()
    ARC_VAULTS.Vaults.Clear()
)

Vitest.test (
    "ARC actions are installed once alongside Help and the existing menus",
    fun () ->
        AppMenu.install ()
        AppMenu.install ()
        let menu = Menu.getApplicationMenu().Value
        let arc = getItem menu "swate-arc"
        Vitest.expect(menu.items |> Array.filter (fun item -> item.id = "swate-arc") |> Array.length).toBe 1

        Vitest.expect(arc.submenu.items |> Array.map _.label).toEqual [|
            "Open ARC in Visual Studio Code"
            "Open Terminal in ARC"
            "Open ARC in File Manager"
        |]

        Vitest.expect(menu.items.[menu.items.Length - 1].id).toBe "swate-help"
        Vitest.expect(mock.focusListenerCount ()).toBe 1
)

Vitest.test (
    "VS Code is disabled until the OS lookup finds an existing application",
    fun () -> promise {
        let menu = buildMenu actions
        let item = getItem menu "swate-open-arc-in-vscode"
        let mutable applicationPath = ""
        let mutable lookupFails = false

        let probe: ArcExternalTools.VSCodeInstallationProbe = {
            GetApplicationPath =
                fun () -> promise {
                    if lookupFails then
                        failwith "Protocol lookup failed"

                    return applicationPath
                }
            GetHandlerName = fun () -> failwith "Desktop handler names are only needed on Linux"
            VerifyCommand = fun () -> promise { return failwith "CLI checks are only needed on Linux" }
        }

        let check () =
            ArcExternalTools.isVSCodeAvailableWith probe "win32"

        do! ArcMenu.refreshAvailabilityWith check menu
        Vitest.expect(item.enabled).toBe false
        applicationPath <- processExecutable ()
        do! ArcMenu.refreshAvailabilityWith check menu
        Vitest.expect(item.enabled).toBe true
        let! macInstalled = ArcExternalTools.isVSCodeAvailableWith probe "darwin"
        Vitest.expect(macInstalled).toBe true
        applicationPath <- Main.Bindings.Path.join [| processExecutable (); "missing-code" |]
        do! ArcMenu.refreshAvailabilityWith check menu
        Vitest.expect(item.enabled).toBe false
        lookupFails <- true
        do! ArcMenu.refreshAvailabilityWith check menu
        Vitest.expect(item.enabled).toBe false
    }
)

Vitest.test (
    "VS Code availability is checked during installation and again when a window receives focus",
    fun () -> promise {
        AppMenu.install ()
        do! Promise.sleep 1
        Vitest.expect(mock.protocolLookupCount ()).toBe 1
        mock.focusWindow (TestHelpers.testWindow ())
        do! Promise.sleep 1
        Vitest.expect(mock.protocolLookupCount ()).toBe 2
        let item = getItem (Menu.getApplicationMenu().Value) "swate-open-arc-in-vscode"
        Vitest.expect(item.enabled).toBe false
    }
)

Vitest.test (
    "Linux checks the registered VS Code handler and installed CLI without requesting application info",
    fun () -> promise {
        let mutable commandChecks = 0

        let probe: ArcExternalTools.VSCodeInstallationProbe = {
            GetApplicationPath = fun () -> promise { return failwith "Application info is unavailable on Linux" }
            GetHandlerName = fun () -> "code-url-handler.desktop"
            VerifyCommand = fun () -> promise { commandChecks <- commandChecks + 1 }
        }

        let! installed = ArcExternalTools.isVSCodeAvailableWith probe "linux"
        Vitest.expect(installed).toBe true
        Vitest.expect(commandChecks).toBe 1

        let! unregistered =
            ArcExternalTools.isVSCodeAvailableWith
                {
                    probe with
                        GetHandlerName = fun () -> ""
                }
                "linux"

        Vitest.expect(unregistered).toBe false
        Vitest.expect(commandChecks).toBe 1

        let! missing =
            ArcExternalTools.isVSCodeAvailableWith
                {
                    probe with
                        VerifyCommand = fun () -> promise { return raise (nodeError "Not installed" "ENOENT") }
                }
                "linux"

        Vitest.expect(missing).toBe false
    }
)

Vitest.test (
    "each action uses the selected window's current ARC path, including after a rename",
    fun () -> promise {
        let first = TestHelpers.registerVault 101 "C:/first ARC"
        let second = TestHelpers.registerVault 102 "C:/second ARC"
        let calls = ResizeArray<string * string>()
        let record name path = promise { calls.Add(name, path) }

        let menu =
            buildMenu {
                actions with
                    OpenInVSCode = record "code"
                    OpenTerminal = record "terminal"
                    OpenInFileManager = record "files"
            }

        let code = getItem menu "swate-open-arc-in-vscode"
        click code (Some(unbox first.window))
        do! Promise.sleep 1
        first.path <- Some "C:/renamed ARC"
        click code (Some(unbox first.window))
        do! Promise.sleep 1
        click (getItem menu "swate-open-terminal-in-arc") (Some(unbox second.window))
        click (getItem menu "swate-open-arc-in-file-manager") (Some(unbox first.window))
        do! Promise.sleep 1

        Vitest.expect(calls.ToArray()).toEqual [|
            "code", "C:/first ARC"
            "code", "C:/renamed ARC"
            "terminal", "C:/second ARC"
            "files", "C:/renamed ARC"
        |]
    }
)

Vitest.test (
    "VS Code availability is checked again before launching",
    fun () -> promise {
        let vault = TestHelpers.registerVault 105 "C:/ARC"
        let mutable available = true
        let mutable launched = false
        let messages = ResizeArray<string>()

        setMessageBox
            mock
            (fun (_, options) ->
                messages.Add options.message

                box {|
                    response = 0
                    checkboxChecked = false
                |}
            )

        let menu =
            buildMenu {
                actions with
                    IsVSCodeAvailable = fun () -> promise { return available }
                    OpenInVSCode = fun _ -> promise { launched <- true }
            }

        available <- false
        click (getItem menu "swate-open-arc-in-vscode") (Some(unbox vault.window))
        do! Promise.sleep 1
        Vitest.expect(launched).toBe false

        Vitest.expect(messages.ToArray()).toEqual [|
            "Visual Studio Code is not installed or its URL handler is unavailable."
        |]
    }
)

Vitest.test (
    "missing windows and empty or unregistered vaults show an error without launching a tool",
    fun () -> promise {
        let vault = TestHelpers.registerVault 103 "C:/ARC"
        vault.path <- None
        let mutable launched = false
        let messages = ResizeArray<string>()

        setMessageBox
            mock
            (fun (_, options) ->
                messages.Add options.message

                box {|
                    response = 0
                    checkboxChecked = false
                |}
            )

        let menu =
            buildMenu {
                actions with
                    OpenTerminal = fun _ -> promise { launched <- true }
            }

        let item = getItem menu "swate-open-terminal-in-arc"
        click item None
        click item (Some(unbox vault.window))
        ARC_VAULTS.Vaults.Clear()
        click item (Some(unbox vault.window))
        do! Promise.sleep 1
        Vitest.expect(launched).toBe false
        Vitest.expect(messages.ToArray()).toEqual (Array.create 3 "No ARC is open. Open an ARC and try again.")
    }
)

Vitest.test (
    "tool failures are reported in a native dialog attached to the selected window",
    fun () -> promise {
        let vault = TestHelpers.registerVault 104 "C:/ARC"
        let messages = ResizeArray<string>()

        setMessageBox
            mock
            (fun (window, options) ->
                Vitest.expect(window.Value.id).toBe vault.window.id
                messages.Add options.message

                box {|
                    response = 0
                    checkboxChecked = false
                |}
            )

        let menu =
            buildMenu {
                actions with
                    OpenTerminal = fun _ -> promise { return failwith "Terminal failed" }
            }

        click (getItem menu "swate-open-terminal-in-arc") (Some(unbox vault.window))
        do! Promise.sleep 1
        Vitest.expect(messages.ToArray()).toEqual [| "Terminal failed" |]
    }
)

Vitest.test (
    "VS Code receives a folder URL with reserved path characters encoded",
    fun () -> promise {
        let path = Main.Bindings.Path.resolve [| "ARC with spaces # ? % ü" |]
        let mutable opened = ""
        mock.setOpenExternal (fun url -> promise { opened <- url })
        do! ArcExternalTools.openInVSCode path
        Vitest.expect(opened.StartsWith "vscode://file/").toBe true
        Vitest.expect(opened.EndsWith "/").toBe true
        Vitest.expect(opened.Contains "ARC%20with%20spaces%20%23%20%3F%20%25%20%C3%BC").toBe true
    }
)

Vitest.test (
    "file manager opens the ARC itself and propagates Electron's nonempty error result",
    fun () -> promise {
        let mutable opened = ""

        mock.setOpenPath (fun path -> promise {
            opened <- path
            return ""
        })

        do! ArcExternalTools.openInFileManager "C:/my ARC"
        Vitest.expect(opened).toBe "C:/my ARC"
        mock.setOpenPath (fun _ -> promise { return "Folder missing" })
        let mutable failure = ""

        try
            do! ArcExternalTools.openInFileManager "C:/missing ARC"
        with error ->
            failure <- error.Message

        Vitest.expect(failure).toBe "Folder missing"
    }
)

Vitest.test (
    "terminal launchers keep paths separate from commands on Windows, macOS and Linux",
    fun () -> promise {
        let path = "ARC with spaces & ' quotes; $()"
        let calls = ResizeArray<string * string * string[] * string>()

        let launchers: ArcExternalTools.TerminalLaunchers = {
            StartDetached = fun command args cwd -> promise { calls.Add("detached", command, args, cwd) }
            Run = fun command args cwd -> promise { calls.Add("run", command, args, cwd) }
        }

        do! ArcExternalTools.openTerminalWith launchers "win32" path
        do! ArcExternalTools.openTerminalWith launchers "darwin" path
        do! ArcExternalTools.openTerminalWith launchers "linux" path

        Vitest.expect(calls.ToArray()).toEqual [|
            "detached", "wt.exe", [| "-d"; "." |], path
            "run", "/usr/bin/open", [| "-a"; "Terminal"; path |], path
            "detached", "x-terminal-emulator", [||], path
        |]
    }
)

Vitest.test (
    "Windows reports a missing Windows Terminal with an actionable message",
    fun () -> promise {
        let launchers: ArcExternalTools.TerminalLaunchers = {
            Run = fun _ _ _ -> promise { return () }
            StartDetached = fun _ _ _ -> promise { return raise (nodeError "Not installed" "ENOENT") }
        }

        let mutable failure = ""

        try
            do! ArcExternalTools.openTerminalWith launchers "win32" "C:/ARC"
        with error ->
            failure <- error.Message

        Vitest.expect(failure).toBe
            "Windows Terminal could not be found. Install Windows Terminal or enable its wt.exe app execution alias."
    }
)

Vitest.test (
    "Linux tries another terminal when an executable is missing and reports other launch failures",
    fun () -> promise {
        let calls = ResizeArray<string>()

        let launchers: ArcExternalTools.TerminalLaunchers = {
            Run = fun _ _ _ -> promise { return () }
            StartDetached =
                fun command _ _ -> promise {
                    calls.Add command

                    if command = "x-terminal-emulator" then
                        return raise (nodeError "Not installed" "ENOENT")
                }
        }

        do! ArcExternalTools.openTerminalWith launchers "linux" "/ARC"
        Vitest.expect(calls.ToArray()).toEqual [| "x-terminal-emulator"; "gnome-terminal" |]
        calls.Clear()

        let failing = {
            launchers with
                StartDetached =
                    fun command _ _ -> promise {
                        calls.Add command
                        return raise (nodeError "Access denied" "EACCES")
                    }
        }

        let mutable failure = ""

        try
            do! ArcExternalTools.openTerminalWith failing "linux" "/ARC"
        with error ->
            failure <- error.Message

        Vitest.expect(failure).toBe "Access denied"
        Vitest.expect(calls.ToArray()).toEqual [| "x-terminal-emulator" |]
    }
)
