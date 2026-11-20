module internal Swate.Components.PageComponents.SettingsPage.SettingsPageTests

open Browser.Types
open Feliz
open Vitest
open Swate.Components.Composite.TermSearch
open Swate.Components.Composite.ThemeSelector
open Swate.Components.PageComponents.SettingsPage.Types

Vitest.afterEach (fun () -> RTL.cleanup ())

let private automatic: GitExecutableSetting = {
    ChosenPath = None
    PathInUse = None
    GitVersion = None
    GitLfsVersion = None
    Problem = None
    IsChecking = false
}

let private chosen: GitExecutableSetting = {
    ChosenPath = Some "/opt/custom/bin/git-chosen"
    PathInUse = Some "/opt/custom/bin/git-in-use"
    GitVersion = Some "9.9.9-test"
    GitLfsVersion = Some "8.8.8-test"
    Problem = Some "the chosen program failed"
    IsChecking = false
}

let private page (settings: ReactElement) =
    ThemeProvider.ThemeProvider(TermSearchConfigProvider.TIBQueryProvider settings)

let private renderWith
    (setting: GitExecutableSetting option)
    (onChoose: unit -> unit)
    (onUseAutomatic: unit -> unit)
    (onCheckAgain: unit -> unit)
    =
    RTL.render (
        page (
            SettingsPage.SettingsPage(
                ?gitExecutable =
                    (setting
                     |> Option.map (fun setting -> {
                         Setting = setting
                         OnChoose = onChoose
                         OnUseAutomatic = onUseAutomatic
                         OnCheckAgain = onCheckAgain
                     }))
            )
        )
    )
    |> ignore

let private render (setting: GitExecutableSetting) =
    renderWith (Some setting) ignore ignore ignore

let private textOf (testId: string) =
    (RTL.screen.getByTestId testId).textContent

let private present (testId: string) =
    (RTL.screen.queryByTestId testId).IsSome

let private button (testId: string) =
    RTL.screen.getByTestId testId :?> HTMLButtonElement

Vitest.describe (
    "SettingsPage git executable",
    fun () ->
        Vitest.test (
            "shows the values of the setting",
            fun () ->
                render chosen

                Vitest.expect(textOf "SettingsGitExecutableSource").toBe "/opt/custom/bin/git-chosen"
                Vitest.expect(textOf "SettingsGitExecutablePathInUse").toBe "/opt/custom/bin/git-in-use"
                Vitest.expect(textOf "SettingsGitExecutableGitVersion").toBe "9.9.9-test"
                Vitest.expect(textOf "SettingsGitExecutableGitLfsVersion").toBe "8.8.8-test"
                Vitest.expect(textOf "SettingsGitExecutableProblem").toBe "the chosen program failed"
        )

        Vitest.test (
            "leaves out the rows of unknown values",
            fun () ->
                render automatic

                Vitest.expect(present "SettingsGitExecutableSource").toBe true
                Vitest.expect(present "SettingsGitExecutablePathInUse").toBe false
                Vitest.expect(present "SettingsGitExecutableGitVersion").toBe false
                Vitest.expect(present "SettingsGitExecutableGitLfsVersion").toBe false
                Vitest.expect(present "SettingsGitExecutableProblem").toBe false
        )

        Vitest.test (
            "calls onChoose when Choose is clicked",
            fun () ->
                let calls = ref 0
                renderWith (Some chosen) (fun () -> calls.Value <- calls.Value + 1) ignore ignore

                RTL.fireEvent.click (button "SettingsGitExecutableChoose")

                Vitest.expect(calls.Value).toBe 1
        )

        Vitest.test (
            "calls onUseAutomatic when Use automatic is clicked",
            fun () ->
                let calls = ref 0
                renderWith (Some chosen) ignore (fun () -> calls.Value <- calls.Value + 1) ignore

                RTL.fireEvent.click (button "SettingsGitExecutableUseAutomatic")

                Vitest.expect(calls.Value).toBe 1
        )

        Vitest.test (
            "calls onCheckAgain when Check again is clicked",
            fun () ->
                let calls = ref 0
                renderWith (Some chosen) ignore ignore (fun () -> calls.Value <- calls.Value + 1)

                RTL.fireEvent.click (button "SettingsGitExecutableCheckAgain")

                Vitest.expect(calls.Value).toBe 1
        )

        Vitest.test (
            "offers Use automatic only with a chosen path",
            fun () ->
                render automatic

                Vitest.expect(present "SettingsGitExecutableUseAutomatic").toBe false
                Vitest.expect(present "SettingsGitExecutableChoose").toBe true
                Vitest.expect(present "SettingsGitExecutableCheckAgain").toBe true
        )

        Vitest.test (
            "disables Check again and shows the checking state while checking",
            fun () ->
                let calls = ref 0

                renderWith
                    (Some { chosen with IsChecking = true })
                    ignore
                    ignore
                    (fun () -> calls.Value <- calls.Value + 1)

                RTL.fireEvent.click (button "SettingsGitExecutableCheckAgain")

                Vitest.expect((button "SettingsGitExecutableCheckAgain").disabled).toBe true
                Vitest.expect(calls.Value).toBe 0
                Vitest.expect(present "SettingsGitExecutableChecking").toBe true
        )

        Vitest.test (
            "enables Check again and hides the checking state when not checking",
            fun () ->
                render chosen

                Vitest.expect((button "SettingsGitExecutableCheckAgain").disabled).toBe false
                Vitest.expect(present "SettingsGitExecutableChecking").toBe false
        )

        Vitest.test (
            "does not render the field without the setting",
            fun () ->
                renderWith None ignore ignore ignore

                Vitest.expect(present "SettingsGitExecutable").toBe false
                Vitest.expect(present "SettingsGitExecutableChoose").toBe false
                Vitest.expect(present "SettingsGitExecutableCheckAgain").toBe false
        )

        Vitest.test (
            "shows the Git executable without Git defaults",
            fun () ->
                render chosen

                Vitest.expect(present "SettingsGitExecutable").toBe true
        )
)

let private renderDataHubs (urls: string[]) (onChange: string[] -> unit) =
    RTL.render (page (SettingsPage.SettingsPage(dataHubs = { Urls = urls; OnChange = onChange })))
    |> ignore

let private typeAddress (text: string) =
    RTL.fireEvent.change (RTL.screen.getByTestId "SettingsDataHubInput", {| target = {| value = text |} |})

let private recordChanges () =
    let changes = ResizeArray<string[]>()
    changes, (fun (urls: string[]) -> changes.Add urls)

Vitest.describe (
    "SettingsPage DataHUB addresses",
    fun () ->
        Vitest.test (
            "lists the saved addresses",
            fun () ->
                renderDataHubs
                    [|
                        "https://gitlab.example.org"
                        "https://other.example.org"
                    |]
                    ignore

                let shown =
                    RTL.screen.getAllByTestId "SettingsDataHub"
                    |> Seq.map _.textContent
                    |> Array.ofSeq

                Vitest.expect(shown).toEqual [|
                    "https://gitlab.example.org"
                    "https://other.example.org"
                |]
        )

        Vitest.test (
            "adds a new address in normalized form and clears the input",
            fun () ->
                let changes, onChange = recordChanges ()
                renderDataHubs [| "https://gitlab.example.org" |] onChange

                typeAddress "https://new.example.org/"
                RTL.fireEvent.click (button "SettingsDataHubAdd")

                Vitest.expect(Array.ofSeq changes).toEqual [|
                    [|
                        "https://gitlab.example.org"
                        "https://new.example.org"
                    |]
                |]

                Vitest.expect((RTL.screen.getByTestId "SettingsDataHubInput" :?> HTMLInputElement).value).toBe ""
        )

        Vitest.test (
            "adds the address on Enter",
            fun () ->
                let changes, onChange = recordChanges ()
                renderDataHubs [||] onChange

                typeAddress "https://new.example.org"

                RTL.fireEvent.custom ("keyDown", RTL.screen.getByTestId "SettingsDataHubInput", {| key = "Enter" |})

                Vitest.expect(Array.ofSeq changes).toEqual [| [| "https://new.example.org" |] |]
        )

        Vitest.test (
            "keeps Add disabled for an address the sign-in already offers or that is no HTTPS address",
            fun () ->
                let changes, onChange = recordChanges ()
                renderDataHubs [| "https://gitlab.example.org" |] onChange

                for address in
                    [
                        ""
                        "https://git.nfdi4plants.org"
                        "https://gitlab.example.org/"
                        "http://new.example.org"
                    ] do
                    typeAddress address
                    RTL.fireEvent.click (button "SettingsDataHubAdd")
                    Vitest.expect((button "SettingsDataHubAdd").disabled).toBe true

                Vitest.expect(changes.Count).toBe 0
        )

        Vitest.test (
            "marks only an address that is no HTTPS address as invalid",
            fun () ->
                renderDataHubs [||] ignore

                let input () =
                    RTL.screen.getByTestId "SettingsDataHubInput"

                typeAddress "http://new.example.org"
                Vitest.expect(input().getAttribute "aria-invalid").toBe "true"

                typeAddress "https://git.nfdi4plants.org"
                Vitest.expect(input().getAttribute "aria-invalid").toBe "false"
        )

        Vitest.test (
            "removes an address",
            fun () ->
                let changes, onChange = recordChanges ()

                renderDataHubs
                    [|
                        "https://gitlab.example.org"
                        "https://other.example.org"
                    |]
                    onChange

                RTL.fireEvent.click (button "SettingsDataHubRemove-https://gitlab.example.org")

                Vitest.expect(Array.ofSeq changes).toEqual [| [| "https://other.example.org" |] |]
        )
)

/// jsdom has no scrollIntoView. The stub records the id of every element scrolled to.
[<Fable.Core.Emit("window.HTMLElement.prototype.scrollIntoView = function () { $0(this.id) }")>]
let private stubScrollIntoView (record: string -> unit) : unit = Fable.Core.Util.jsNative

[<Fable.Core.Emit("delete window.HTMLElement.prototype.scrollIntoView")>]
let private removeScrollIntoView () : unit = Fable.Core.Util.jsNative

let private gitDefaults: GitDefaultsSection = {
    Current = {
        LfsAutoTrackThresholdMb = 10
        DownloadLargeFiles = true
        DiffIndexingLimitMb = 1024
        UploadRightAway = true
    }
    BuiltIn = {
        LfsAutoTrackThresholdMb = 10
        DownloadLargeFiles = true
        DiffIndexingLimitMb = 1024
        UploadRightAway = true
    }
    OnChange = ignore
}

/// Renders the page with Git defaults and records where it scrolls to.
let private renderCategories (category: SettingsCategory option) =
    let scrolledTo = ResizeArray<string>()
    stubScrollIntoView scrolledTo.Add

    RTL.render (page (SettingsPage.SettingsPage(gitDefaults = gitDefaults, ?category = category)))
    |> ignore

    scrolledTo

let private navigationIds () =
    RTL.screen.getAllByTestId (System.Text.RegularExpressions.Regex "^SettingsNavigation-")
    |> Seq.map (fun element -> element.getAttribute "data-testid")
    |> Array.ofSeq

let private isMarked (sectionId: string) =
    (RTL.screen.getByTestId $"SettingsNavigation-{sectionId}").getAttribute "aria-current" = "location"

Vitest.describe (
    "SettingsPage categories",
    fun () ->
        Vitest.afterEach removeScrollIntoView

        Vitest.test (
            "links only the categories that have settings, in page order",
            fun () ->
                renderDataHubs [||] ignore

                Vitest.expect(navigationIds ()).toEqual [|
                    "SettingsNavigation-settings-general"
                    "SettingsNavigation-settings-datahub"
                    "SettingsNavigation-settings-term-search"
                |]

                Vitest.expect(present "settings-git").toBe false
                Vitest.expect(present "settings-large-files").toBe false
        )

        Vitest.test (
            "a link scrolls to its category and marks it",
            fun () ->
                let scrolledTo = renderCategories None
                Vitest.expect(isMarked "settings-general").toBe true

                RTL.fireEvent.click (button "SettingsNavigation-settings-large-files")

                Vitest.expect(Array.ofSeq scrolledTo).toEqual [| "settings-large-files" |]
                Vitest.expect(isMarked "settings-large-files").toBe true
                Vitest.expect(isMarked "settings-general").toBe false
        )

        Vitest.test (
            "opens at the category it is given",
            fun () ->
                let scrolledTo = renderCategories (Some SettingsCategory.Git)

                Vitest.expect(Array.ofSeq scrolledTo).toEqual [| "settings-git" |]
                Vitest.expect(isMarked "settings-git").toBe true
        )
)
