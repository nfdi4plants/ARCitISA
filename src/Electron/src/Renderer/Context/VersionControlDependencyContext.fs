module Renderer.Context.VersionControlDependencyContext

open Browser.Dom
open Feliz
open Swate.Electron.Shared.IPCTypes.MainToRendererIpc
open Swate.Electron.Shared.VersionControlTypes

/// One text per component with a problem. The library decides what is required (Git 2.42, Git
/// LFS 3.7 and the LFS filter configuration) and says how to fix it. Its remediation text starts
/// with the cause when a version check failed and can continue with the raw output of the
/// program, so a text can span many lines.
let private dependencyProblems (statuses: DependencyStatusDto[]) : string[] =
    // The library reports the filter configuration as installed when the filter is set up, whatever
    // the state of git-lfs itself. A problem of the configuration is only worth a line of its own
    // while git-lfs works. Otherwise the line of git-lfs says what to do first.
    let gitLfsWorks =
        statuses
        |> Array.tryFind (fun status -> status.Component = "git-lfs")
        |> Option.forall (fun status -> status.Installed && status.Compatible)

    // Git LFS runs through git, so a git that is not installed explains the missing git-lfs too.
    let gitInstalled =
        statuses
        |> Array.tryFind (fun status -> status.Component = "git")
        |> Option.forall (fun status -> status.Installed)

    statuses
    |> Array.filter (fun status -> not status.Installed || not status.Compatible)
    |> Array.choose (fun status ->
        let remediation =
            status.Remediation
            |> Option.map (fun text -> $" {text}")
            |> Option.defaultValue ""

        if status.Component = "git-lfs-configuration" then
            if gitLfsWorks then
                Some $"Git LFS is installed but not set up.{remediation}"
            else
                None
        elif status.Component = "git-lfs" && not gitInstalled then
            None
        elif not status.Installed then
            Some $"{status.Component} was not found or could not be run.{remediation}"
        else
            match status.Version with
            | Some version -> Some $"{version} is not supported.{remediation}"
            | None -> Some $"{status.Component} is not supported.{remediation}"
    )

/// One line per problem, or None without a problem.
let private joinProblems (problems: string[]) : string option =
    if problems.Length = 0 then
        None
    else
        Some(String.concat "\n" problems)

/// The text up to the first line break, without trailing spaces and without the colon that
/// announced the lines after it.
let private firstLine (text: string) : string =
    text.Split('\n').[0].TrimEnd().TrimEnd(':').TrimEnd()

/// The message shown when a version control dependency is missing or too old, with the full
/// remediation of every problem. The settings show this text.
let dependencyProblemMessage (statuses: DependencyStatusDto[]) : string option =
    dependencyProblems statuses |> joinProblems

/// The problems of dependencyProblemMessage with one line each. A remediation can carry the raw
/// output of a program, which would fill the banner, so each problem keeps only its first line.
let dependencyProblemSummary (statuses: DependencyStatusDto[]) : string option =
    dependencyProblems statuses |> Array.map firstLine |> joinProblems

/// What is wrong with the dependencies of a state, as the text for the user. A component that is
/// missing, too old or not set up comes first. The failure of the check itself only shows when no
/// component has a problem. A state without either has none, so a check that still runs for the
/// first time shows nothing.
let stateProblemMessage (state: VersionControlDependencyStateDto) : string option =
    match dependencyProblemMessage state.Statuses with
    | Some message -> Some message
    | None -> state.Failure

/// The short form of stateProblemMessage for the banner. It follows the same order and cuts the
/// failure of the check to its first line.
let stateProblemSummary (state: VersionControlDependencyStateDto) : string option =
    match dependencyProblemSummary state.Statuses with
    | Some message -> Some message
    | None -> state.Failure |> Option.map firstLine

type DependencyContextValue = {
    /// None until the main process has answered the first time.
    state: VersionControlDependencyStateDto option
    /// Asks the main process for a new check. The result arrives as a state update.
    recheck: unit -> unit
}

let VersionControlDependencyCtx =
    React.createContext<DependencyContextValue> ({ state = None; recheck = ignore })

[<Hook>]
let useVersionControlDependencyCtx () =
    React.useContext VersionControlDependencyCtx

/// The main process owns the dependency check, so all windows show the same state. The provider
/// loads the current state and follows its updates. The reply of a recheck only confirms that the
/// check started. The running state and the result both arrive through the update.
[<ReactComponent>]
let Provider (children: ReactElement) =
    let synced =
        Renderer.MainSyncedState.useMainSyncedState {
            initial = None
            load =
                fun () -> promise {
                    let! state = Api.ipcVersionControlApi.getDependencyState ()
                    return Some state
                }
            subscribe =
                fun update ->
                    Renderer.IpcReceiver.subscribeProxyReceiver<IVersionControlDependencyRendererApi> {
                        versionControlDependenciesChanged = Some >> update
                    }
            // A failed load leaves the banner hidden. A broken bridge would make a modal for every window
            // more noise than help, and the next update from the main process still reaches the banner.
            onError = fun error -> console.error ("Could not load the version control dependencies: " + error.Message)
            dependencies = [||]
        }

    let recheck =
        React.useCallback (
            (fun () ->
                promise {
                    try
                        let! _ = Api.ipcVersionControlApi.recheckDependencies ()
                        ()
                    with error ->
                        console.error ("Could not start the version control dependency check: " + error.Message)
                }
                |> Promise.start
            ),
            [||]
        )

    let value =
        React.useMemo (
            (fun () -> {
                state = synced.state
                recheck = recheck
            }),
            [| box synced.state; box recheck |]
        )

    VersionControlDependencyCtx.Provider(value, children)
