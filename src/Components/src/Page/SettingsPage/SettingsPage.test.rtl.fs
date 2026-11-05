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
                ?gitExecutable = setting,
                onGitExecutableChoose = onChoose,
                onGitExecutableUseAutomatic = onUseAutomatic,
                onGitExecutableCheckAgain = onCheckAgain
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
