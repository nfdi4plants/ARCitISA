module ElectronCore.AppMenuTests

open Fable.Core
open Fable.Electron.Main
open Swate.Electron.Shared.IPCTypes
open Vitest

type private ElectronMock =
    abstract reset: unit -> unit
    abstract setBrowserWindowFromId: (int -> BrowserWindow option) -> unit

[<Import("__electronMock", "electron")>]
let private mock: ElectronMock = jsNative

[<Emit("$0.setShowMessageBox((window, options) => $1([window, options]))")>]
let private setMessageBox (_mock: ElectronMock) (_callback: BaseWindow * Dialog.ShowMessageBox.Options -> obj) : unit =
    jsNative

[<Emit("$0.webContents.send = (channel, payload) => $1([channel, payload])")>]
let private captureSend (_window: BrowserWindow) (_callback: string * obj -> unit) : unit = jsNative

[<Emit("$0.click({}, $1, $1.webContents)")>]
let private clickMenuItem (_item: MenuItem) (_window: BrowserWindow) : unit = jsNative

Vitest.afterEach (fun () -> mock.reset ())

Vitest.test (
    "Help replaces the default Help menu and preserves the other menus",
    fun () ->
        let original =
            Menu.buildFromTemplate [|
                U2.Case1(MenuItem.Options(label = "File", id = "original-file"))
                U2.Case1(MenuItem.Options(label = "Help", role = Enums.MenuItem.Options.Role.Help))
            |]

        Menu.setApplicationMenu (Some original)
        Main.AppMenu.install ()
        Main.AppMenu.install ()
        let menu = Menu.getApplicationMenu().Value
        Vitest.expect(menu.items.Length).toBe 3
        Vitest.expect(menu.items.[0].id).toBe "original-file"
        let help = menu.getMenuItemById("swate-help").Value

        Vitest.expect(help.submenu.items |> Array.map _.label).toEqual [|
            "Check for updates ..."
            "About"
            "Show Release Notes"
        |]
)

Vitest.test (
    "About uses a native dialog with the application name and current version",
    fun () -> promise {
        let! settingsRoot = ElectronCore.TestHelpers.createTempDirectoryAsync "swate-help-version-"
        Vitest.vi.stubEnv ("SWATE_TEST_USER_DATA", Some settingsRoot)
        Main.Settings.AppVersion.initialize ()
        Vitest.vi.unstubAllEnvs ()
        do! ElectronCore.TestHelpers.removeDirectoryAsync settingsRoot
        Vitest.expect(System.String.IsNullOrWhiteSpace(Main.Settings.AppVersion.getState().CurrentVersion)).toBe false
        let window = ElectronCore.TestHelpers.testWindow ()
        let parentWindow: BaseWindow = unbox window
        let mutable shown = false

        setMessageBox
            mock
            (fun (parent, options) ->
                shown <- true
                Vitest.expect(parent.id).toBe window.id
                Vitest.expect(options.message).toBe "ARCitISA"

                Vitest.expect(options.detail).toEqual ($"Version {Main.Settings.AppVersion.getState().CurrentVersion}")

                box {|
                    response = 0
                    checkboxChecked = false
                |}
            )

        Main.AppMenu.install ()
        let about = Menu.getApplicationMenu().Value.getMenuItemById("swate-about").Value
        clickMenuItem about window
        do! Promise.sleep 1
        Vitest.expect(shown).toBe true
    }
)

Vitest.test (
    "manual checks report available, current and failed checks in a native dialog",
    fun () -> promise {
        let window = ElectronCore.TestHelpers.testWindow ()
        let parentWindow: BaseWindow = unbox window
        let dialogs = ResizeArray<string>()

        setMessageBox
            mock
            (fun (_, options) ->
                dialogs.Add options.message

                box {|
                    response = 0
                    checkboxChecked = false
                |}
            )

        let state: AppVersionState = {
            CurrentVersion = "1.0.0"
            UpdateVersion = Some "v2.0.0"
            ChangelogVersion = None
        }

        do! Main.HelpMenu.checkForUpdates (Some parentWindow) (fun () -> promise { return Ok state })

        do!
            Main.HelpMenu.checkForUpdates
                (Some parentWindow)
                (fun () -> promise { return Ok { state with UpdateVersion = None } })

        do! Main.HelpMenu.checkForUpdates (Some parentWindow) (fun () -> promise { return Error(exn "Offline") })

        Vitest.expect(dialogs.ToArray()).toEqual [|
            "ARCitISA v2.0.0 is available."
            "ARCitISA is up to date."
            "Could not check for updates."
        |]
    }
)

Vitest.test (
    "release notes sends a fresh request to the selected window on every click",
    fun () ->
        let window = ElectronCore.TestHelpers.testWindow ()
        let messages = ResizeArray<string>()
        mock.setBrowserWindowFromId (fun id -> if id = window.id then Some window else None)
        captureSend window (fun (channel, _) -> messages.Add channel)
        Main.AppMenu.install ()

        let notes =
            Menu.getApplicationMenu().Value.getMenuItemById("swate-release-notes").Value

        clickMenuItem notes window
        clickMenuItem notes window
        Main.HelpMenu.showReleaseNotes None

        Vitest.expect(messages.ToArray()).toEqual [|
            "IReleaseNotesRendererApi:showReleaseNotes"
            "IReleaseNotesRendererApi:showReleaseNotes"
        |]
)
