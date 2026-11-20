module ElectronRenderer.SettingsPageTargetTests

open Fable.Core.JsInterop
open Renderer.Components.MainContent.SettingsPageTarget
open Renderer.Context.VersionControlDependencyContext
open Swate.Electron.Shared.VersionControlTypes
open Vitest

// The target reaches the IPC bridges through the Api module. The preload is absent in tests,
// so the bridges are replaced by empty objects that no test calls.
Vitest.vi.mock (
    "./src/Electron/src/Renderer/Api.js",
    box (fun () ->
        createObj [
            "ipcGitLabApi" ==> createObj []
            "ipcVersionControlApi" ==> createObj []
            "ipcArcVaultApi" ==> createObj []
            "ipcAuthApi" ==> createObj []
            "ipcTemplateApi" ==> createObj []
            "ipcValidationPackageApi" ==> createObj []
            "ipcUserDefaultsApi" ==> createObj []
            "ipcUiSettingsApi" ==> createObj []
        ]
    )
)
|> ignore

let private gitStatus: DependencyStatusDto = {
    Component = "git"
    Installed = true
    Version = Some "git version 2.45.1"
    Compatible = true
    Remediation = None
    ProbeExecutablePath = Some "/tools/git"
}

let private gitLfsStatus: DependencyStatusDto = {
    Component = "git-lfs"
    Installed = true
    Version = Some "git-lfs/3.7.0"
    Compatible = true
    Remediation = None
    ProbeExecutablePath = Some "/tools/git"
}

let private stateOf (statuses: DependencyStatusDto[]) (failure: string option) (isChecking: bool) = {
    Statuses = statuses
    Failure = failure
    IsChecking = isChecking
    CheckedAt = Some 1000.0
}

Vitest.describe (
    "SettingsPageTarget toGitExecutableSetting",
    fun () ->
        Vitest.test (
            "knows only the chosen path before the main process has answered",
            fun () ->
                let setting = toGitExecutableSetting (Some "/opt/git") None

                Vitest.expect(setting.ChosenPath).toEqual (Some "/opt/git")
                Vitest.expect(setting.PathInUse).toEqual None
                Vitest.expect(setting.GitVersion).toEqual None
                Vitest.expect(setting.GitLfsVersion).toEqual None
                Vitest.expect(setting.Problem).toEqual None
                Vitest.expect(setting.IsChecking).toEqual false
        )

        Vitest.test (
            "reports the path in use and both versions of a working Git",
            fun () ->
                let setting =
                    toGitExecutableSetting None (Some(stateOf [| gitStatus; gitLfsStatus |] None false))

                Vitest.expect(setting.ChosenPath).toEqual None
                Vitest.expect(setting.PathInUse).toEqual (Some "/tools/git")
                Vitest.expect(setting.GitVersion).toEqual (Some "git version 2.45.1")
                Vitest.expect(setting.GitLfsVersion).toEqual (Some "git-lfs/3.7.0")
                Vitest.expect(setting.Problem).toEqual None
                Vitest.expect(setting.IsChecking).toEqual false
        )

        Vitest.test (
            "takes the Git LFS version from the git-lfs component only",
            fun () ->
                let setting = toGitExecutableSetting None (Some(stateOf [| gitStatus |] None false))

                Vitest.expect(setting.GitVersion).toEqual (Some "git version 2.45.1")
                Vitest.expect(setting.GitLfsVersion).toEqual None
        )

        Vitest.test (
            "does not name a Git that could not run as the one in use",
            fun () ->
                let missingGit = {
                    gitStatus with
                        Installed = false
                        Version = None
                        Compatible = false
                        Remediation = Some "Install Git."
                        ProbeExecutablePath = Some "/chosen/git"
                }

                let state = stateOf [| missingGit; gitLfsStatus |] None false
                let setting = toGitExecutableSetting (Some "/chosen/git") (Some state)

                Vitest.expect(setting.ChosenPath).toEqual (Some "/chosen/git")
                Vitest.expect(setting.PathInUse).toEqual None
                Vitest.expect(setting.GitVersion).toEqual None
                Vitest.expect(setting.Problem.IsSome).toEqual true
                Vitest.expect(setting.Problem).toEqual (stateProblemMessage state)
        )

        Vitest.test (
            "keeps the version and the path of a Git that is too old and names the problem",
            fun () ->
                let oldGit = {
                    gitStatus with
                        Compatible = false
                        Remediation = Some "Update Git."
                }

                let state = stateOf [| oldGit; gitLfsStatus |] None false
                let setting = toGitExecutableSetting None (Some state)

                Vitest.expect(setting.PathInUse).toEqual (Some "/tools/git")
                Vitest.expect(setting.GitVersion).toEqual (Some "git version 2.45.1")
                Vitest.expect(setting.Problem.IsSome).toEqual true
                Vitest.expect(setting.Problem).toEqual (stateProblemMessage state)
        )

        Vitest.test (
            "shows the failure of the check when no component has a problem",
            fun () ->
                let setting =
                    toGitExecutableSetting None (Some(stateOf [||] (Some "The check could not run.") false))

                Vitest.expect(setting.Problem).toEqual (Some "The check could not run.")
        )

        Vitest.test (
            "marks a running check and keeps the result of the last one",
            fun () ->
                let setting =
                    toGitExecutableSetting None (Some(stateOf [| gitStatus; gitLfsStatus |] None true))

                Vitest.expect(setting.IsChecking).toEqual true
                Vitest.expect(setting.PathInUse).toEqual (Some "/tools/git")
        )
)
