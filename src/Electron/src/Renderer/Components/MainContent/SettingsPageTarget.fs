module Renderer.Components.MainContent.SettingsPageTarget

open System
open Feliz
open Renderer.Components.Helper.ArcVaultHelper
open Renderer.Context.VersionControlDependencyContext
open Swate.Components.Page.GitSidebarTypes
open Swate.Components.PageComponents.SettingsPage
open Swate.Components.PageComponents.SettingsPage.Types
open Swate.Components.Primitive.ErrorModal.Context
open Swate.Electron.Shared.IPCTypes
open Swate.Electron.Shared.IPCTypes.MainToRendererIpc
open Swate.Electron.Shared.VersionControlTypes

let private toGitDefaults (values: UserDefaultValuesDto) : SettingsGitDefaults = {
    LfsAutoTrackThresholdMb = values.LfsAutoTrackThresholdMb
    DownloadLargeFiles = values.DownloadLargeFiles
    DiffIndexingLimitMb = values.DiffIndexingLimitMb
    UploadRightAway = values.UploadRightAway
}

/// The Git executable field. The saved defaults hold the chosen path. The main process owns the
/// dependency check, so everything else in the field comes from its latest state.
/// A Git that could not run is not in use, so its path stays out of the field. The Source line still
/// shows it as the chosen path.
let toGitExecutableSetting
    (chosenPath: string option)
    (state: VersionControlDependencyStateDto option)
    : GitExecutableSetting =
    let statusOf (name: string) =
        state
        |> Option.bind (fun state -> state.Statuses |> Array.tryFind (fun status -> status.Component = name))

    let git = statusOf "git"
    let gitLfs = statusOf "git-lfs"

    {
        ChosenPath = chosenPath
        PathInUse = git |> Option.filter _.Installed |> Option.bind _.ProbeExecutablePath
        GitVersion = git |> Option.bind _.Version
        GitLfsVersion = gitLfs |> Option.bind _.Version
        Problem = state |> Option.bind stateProblemMessage
        IsChecking = state |> Option.exists _.IsChecking
    }

[<ReactComponent(true)>]
let SettingsPage () =
    let errorModal = useErrorModalCtx ()
    let appStateCtx = Renderer.Context.AppStateContext.useAppStateCtx ()

    let onEnsureNotesError =
        createErrorModalCallback errorModal.enqueue "Could not create notes folder" appStateCtx

    let onAutoCreateNotesFolderEnabled () =
        ensureNotesFolder onEnsureNotesError |> Promise.start


    let scaleToPercentage (scale: float) = scale * 100.0 |> Math.Round |> int

    let uiScaling =
        Renderer.MainSyncedState.useMainSyncedState {
            initial = 100
            load =
                fun () -> promise {
                    let! scale = Api.ipcUiSettingsApi.getUiScale ()
                    return scaleToPercentage scale
                }
            subscribe =
                fun update ->
                    Renderer.IpcReceiver.subscribeProxyReceiver<IUiSettingsRendererApi> {
                        uiScaleChanged = scaleToPercentage >> update
                    }
            onError =
                fun error -> createErrorModalCallback errorModal.enqueue "Could not load UI scale" None error.Message
            dependencies = [||]
        }

    let submitUIScaling (newValue: int) =
        promise {
            let newValuePercentile = float newValue / 100.

            match! Api.ipcUiSettingsApi.setUiScale newValuePercentile with
            | Ok() -> ()
            | Error e -> createErrorModalCallback errorModal.enqueue "Error" None e.Message
        }
        |> Promise.start

    // The built-in values stand in until the main process answers. The page then never falls back to the
    // browser storage of the old auto-create setting.
    let userDefaults =
        Renderer.MainSyncedState.useMainSyncedState {
            initial = {
                Effective = Renderer.Context.GitWorkflow.builtInUserDefaults
                BuiltIn = Renderer.Context.GitWorkflow.builtInUserDefaults
            }
            load = fun () -> Api.ipcUserDefaultsApi.getUserDefaults ()
            subscribe =
                fun update ->
                    Renderer.IpcReceiver.subscribeProxyReceiver<IUserDefaultsRendererApi> {
                        userDefaultsChanged = update
                    }
            onError =
                fun error ->
                    createErrorModalCallback errorModal.enqueue "Could not load user defaults" None error.Message
            dependencies = [||]
        }

    let submitUserDefaults (values: UserDefaultValuesDto) =
        promise {
            match! Api.ipcUserDefaultsApi.setUserDefaults values with
            | Ok _ -> ()
            | Error message -> createErrorModalCallback errorModal.enqueue "Could not save user defaults" None message
        }
        |> Promise.start

    let effectiveDefaults = userDefaults.state.Effective

    let submitGitDefaults (gitDefaults: SettingsGitDefaults) =
        submitUserDefaults {
            effectiveDefaults with
                LfsAutoTrackThresholdMb = gitDefaults.LfsAutoTrackThresholdMb
                DownloadLargeFiles = gitDefaults.DownloadLargeFiles
                DiffIndexingLimitMb = gitDefaults.DiffIndexingLimitMb
                UploadRightAway = gitDefaults.UploadRightAway
        }

    let submitAutoCreateNotesFolder (isEnabled: bool) =
        submitUserDefaults {
            effectiveDefaults with
                AutoCreateNotesFolder = isEnabled
        }

    let dependencies = useVersionControlDependencyCtx ()

    // The main process checks the dependencies again after it saved a new path.
    let submitGitExecutablePath (path: string option) =
        submitUserDefaults {
            effectiveDefaults with
                GitExecutablePath = path
        }

    let chooseGitExecutable () =
        promise {
            match! Api.ipcArcVaultApi.pickExecutable () with
            | Error error when isPickCancelled error -> ()
            | Error error ->
                createErrorModalCallback errorModal.enqueue "Could not pick Git executable" None error.Message
            | Ok path -> submitGitExecutablePath (Some path)
        }
        |> Promise.start

    Html.div [
        prop.className "swt:size-full swt:min-w-0 swt:min-h-0 swt:overflow-y-auto"
        prop.testId "main-content-settings-page"
        prop.children [
            Swate.Components.PageComponents.SettingsPage.SettingsPage.SettingsPage(
                onAutoCreateNotesFolderEnabled = onAutoCreateNotesFolderEnabled,
                uiScaling = uiScaling.state,
                onUIScaling = submitUIScaling,
                gitDefaults = toGitDefaults effectiveDefaults,
                gitBuiltInDefaults = toGitDefaults userDefaults.state.BuiltIn,
                onGitDefaultsSave = submitGitDefaults,
                autoCreateNotesFolder = effectiveDefaults.AutoCreateNotesFolder,
                onAutoCreateNotesFolderChange = submitAutoCreateNotesFolder,
                gitExecutable = toGitExecutableSetting effectiveDefaults.GitExecutablePath dependencies.state,
                onGitExecutableChoose = chooseGitExecutable,
                onGitExecutableUseAutomatic = (fun () -> submitGitExecutablePath None),
                onGitExecutableCheckAgain = dependencies.recheck
            )
        ]
    ]
