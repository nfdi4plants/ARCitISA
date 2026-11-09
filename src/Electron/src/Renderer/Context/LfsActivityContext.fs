/// Running large-file actions (Download, Free) per ARC. The state lives above the
/// sidebar panels, so a panel switch that remounts the file tree keeps the busy state, and an
/// action that settles while the file tree is unmounted still clears its entry.
module Renderer.Context.LfsActivityContext

open System
open System.Collections.Generic
open Fable.Core
open Feliz
open Swate.Components.Page.FileExplorer.Types
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.IPCTypes.MainToRendererIpc
open Swate.Electron.Shared.VersionControlTypes

type LfsActivity = {
    /// Shown in the row while the action runs, for example "Freeing".
    Label: string
    /// The tree entry of the path when the action started.
    Entry: FileEntry option
}

/// Activities keyed by the ARC scope and the ARC-relative path.
type LfsActivityState = Map<string * string, LfsActivity>

module LfsActivityState =

    let empty: LfsActivityState = Map.empty

    let isBusy (scopeId: string) (path: string) (state: LfsActivityState) = Map.containsKey (scopeId, path) state

    let start (scopeId: string) (path: string) (activity: LfsActivity) (state: LfsActivityState) : LfsActivityState =
        Map.add (scopeId, path) activity state

    let finish (scopeId: string) (path: string) (state: LfsActivityState) : LfsActivityState =
        Map.remove (scopeId, path) state

    /// The activities of one ARC, keyed by the ARC-relative path.
    let forScope (scopeId: string option) (state: LfsActivityState) : Map<string, LfsActivity> =
        match scopeId with
        | None -> Map.empty
        | Some scopeId ->
            state
            |> Map.toSeq
            |> Seq.choose (fun ((activityScopeId, path), activity) ->
                if activityScopeId = scopeId then
                    Some(path, activity)
                else
                    None
            )
            |> Map.ofSeq

    /// The busy target of a path from the library's listing. The rows look up their busy state by the path of their
    /// tree entry, and that path can differ from the listing path in Unicode form or case. batchTarget keys the
    /// target by the entry's path when the tree has the entry, and by the listing path when it has not.
    let batchTarget (snapshot: FileTreeStore.FileTreeSnapshot) (listingPath: string) : string * FileEntry option =
        match FileTreeStore.tryFind snapshot listingPath with
        | Some entry -> entry.path, Some entry
        | None -> listingPath, None

    /// The last known entries of the busy files, grouped by the normalized path of their parent folder. A tree
    /// rebuild asks for one folder at a time, so the file tree computes the grouping once per change of the
    /// activities.
    let busyEntriesByParent (activities: Map<string, LfsActivity>) : Map<string, FileEntry[]> =
        activities
        |> Map.toArray
        |> Array.choose (fun (_, activity) -> activity.Entry)
        |> Array.groupBy (fun entry -> PathHelpers.tryGetParentPath entry.path |> Option.defaultValue "")
        |> Map.ofArray

    /// A Free briefly replaces the file on disk, so the listing of a folder can miss a busy file. The last known
    /// entry of each busy file in this folder is added back, which keeps its row until the action ends. The same
    /// array comes back when nothing is missing. The cost follows the children plus the busy files of this folder.
    let withBusyChildren
        (busyByParent: Map<string, FileEntry[]>)
        (directoryPath: string)
        (children: FileEntry[])
        : FileEntry[] =
        match Map.tryFind (PathHelpers.normalizePath directoryPath) busyByParent with
        | None -> children
        | Some busy ->
            let listed =
                HashSet<string>(
                    children
                    |> Array.map (fun child -> PathHelpers.normalizePathForFsComparison child.path)
                )

            let missing =
                busy
                |> Array.filter (fun entry ->
                    not (listed.Contains(PathHelpers.normalizePathForFsComparison entry.path))
                )

            if missing.Length = 0 then
                children
            else
                Array.append children missing

/// Every running activity path together with all its ancestors, normalized for filesystem comparison. A lookup
/// answers "does an activity run on or below this folder" without walking the activities, which matters when
/// thousands of files are busy and the tree asks once per visible row.
type LfsActivePathIndex = private { Keys: HashSet<string> }

module LfsActivePathIndex =

    let empty = { Keys = HashSet<string>() }

    /// create normalizes paths the way PathHelpers.isSameOrDescendantPathForFsComparison normalizes them.
    let create (paths: string seq) : LfsActivePathIndex =
        let keys = HashSet<string>()

        for path in paths do
            let key =
                path
                |> PathHelpers.normalizeCanonicalRelativePath
                |> PathHelpers.normalizePathForFsComparison

            if not (String.IsNullOrWhiteSpace key) then
                keys.Add key |> ignore

                for index in 1 .. key.Length - 1 do
                    if key.[index] = '/' then
                        keys.Add(key.Substring(0, index)) |> ignore

        { Keys = keys }

    /// True when an activity runs on the scope path or below it. A blank scope path locks nothing.
    let locksScope (index: LfsActivePathIndex) (scopePath: string) =
        let key = PathHelpers.normalizePathForFsComparison scopePath
        not (String.IsNullOrWhiteSpace key) && index.Keys.Contains key

[<RequireQualifiedAccess>]
type LfsActionKind =
    | Download
    | Free

/// Phase codes of the library's progress events for large-file batches and transfers. The other phases of an
/// operation (per-file events and the like) carry no number that the file tree shows. They only clear the step
/// percent.
module LfsProgressPhase =

    /// Count event of a download batch with more than one path.
    [<Literal>]
    let MaterializeFiles = "lfs-materialize-files"

    /// Count event of a free batch with more than one path.
    [<Literal>]
    let DematerializeFiles = "lfs-dematerialize-files"

    /// Meter of the git lfs fetch step of a download.
    [<Literal>]
    let MaterializeTransfer = "lfs-materialize-transfer"

    /// Meter of the git lfs checkout step of a download.
    [<Literal>]
    let MaterializeCheckout = "lfs-materialize-checkout"

/// One running download or free of the file tree. Count and StepPercent come from the progress events that the
/// library reported for OperationId. The raw output of OperationId lives in LfsRunningLogsCtx, because it changes
/// with every batch and only the Git sidebar shows it.
type LfsRunningAction = {
    OperationId: string
    Kind: LfsActionKind
    /// The ARC-relative path of the folder or the file the action runs on.
    Target: string
    IsFolder: bool
    FileCount: int
    /// Files done and files requested by the latest count event of a batch. None before the first event.
    Count: (int * int) option
    /// Percent of the current transfer step while Git LFS reports one. None when no meter with numbers is active,
    /// and after any event that is not a meter of the transfer or the checkout step.
    StepPercent: float option
    /// Date.now () when `track` listed the action. `track` sets it, so the caller passes any value.
    StartedAt: float
}

module LfsRunningAction =

    /// The name of the action in the Git sidebar, for example "Downloading raw-data".
    let label (action: LfsRunningAction) : string =
        let verb =
            match action.Kind with
            | LfsActionKind.Download -> "Downloading"
            | LfsActionKind.Free -> "Freeing"

        $"{verb} {PathHelpers.getFileName action.Target}"

    /// Applies one progress event. A meter of the transfer or checkout step sets StepPercent. Every other event
    /// clears it, because the step that it measured is over. A count event also sets Count. The action comes
    /// back as the same instance when the event changes nothing, so the caller can skip the state update.
    let withProgress (progress: VersionControlProgressDto) (action: LfsRunningAction) : LfsRunningAction =
        match progress.PhaseCode with
        | LfsProgressPhase.MaterializeTransfer
        | LfsProgressPhase.MaterializeCheckout ->
            // A meter of a single object, and the start event of the transfer, carry no numbers.
            let percent =
                match progress.Completed, progress.Total with
                | Some completed, Some total when total > 0.0 ->
                    Some(Math.Max(0.0, Math.Min(100.0, completed / total * 100.0)))
                | _ -> None

            if percent = action.StepPercent then
                action
            else
                { action with StepPercent = percent }
        | phase ->
            let count =
                match phase, progress.Completed, progress.Total with
                | (LfsProgressPhase.MaterializeFiles | LfsProgressPhase.DematerializeFiles), Some completed, Some total ->
                    Some(int completed, int total)
                | _ -> action.Count

            if count = action.Count && action.StepPercent.IsNone then
                action
            else
                {
                    action with
                        Count = count
                        StepPercent = None
                }

    /// The live text of a folder action: "k/N" with the percent of the current step appended. It reads 0 of
    /// FileCount before the first count event, and a folder of one path keeps reading 0/1 until it is done,
    /// because the library sends count events only for batches of more than one path. A single file has no text.
    let progressText (action: LfsRunningAction) : string option =
        if action.IsFolder then
            let completed, total = action.Count |> Option.defaultValue (0, action.FileCount)

            let counts = $"{completed}/{total}"

            match action.StepPercent with
            | Some percent -> Some $"{counts} {string (int (Math.Floor percent))}%%"
            | None -> Some counts
        else
            None

    /// The percent of a folder action: the percent of the current step while Git LFS reports one, else the share
    /// of finished files. The bar then moves with the detail text. A single file has no percent.
    let progressPercent (action: LfsRunningAction) : float option =
        if action.IsFolder then
            action.StepPercent
            |> Option.orElse (
                match action.Count with
                | Some(completed, total) when total > 0 -> Some(float completed / float total * 100.0)
                | _ -> None
            )
        else
            None

    /// The progress text by target path, for the rows that show it. Actions without a text are left out.
    let busyDetails (actions: LfsRunningAction[]) : Map<string, string> =
        actions
        |> Array.choose (fun action -> progressText action |> Option.map (fun text -> action.Target, text))
        |> Map.ofArray

/// The running actions in start order, each with the ARC scope it started in.
type LfsRunningActionsState = (string * LfsRunningAction) list

module LfsRunningActionsState =

    let empty: LfsRunningActionsState = []

    let start (scopeId: string) (action: LfsRunningAction) (state: LfsRunningActionsState) : LfsRunningActionsState =
        state @ [ scopeId, action ]

    let finish (operationId: string) (state: LfsRunningActionsState) : LfsRunningActionsState =
        state |> List.filter (fun (_, action) -> action.OperationId <> operationId)

    /// True when a running action has this operation id. Progress of any other operation is not ours.
    let owns (operationId: string) (state: LfsRunningActionsState) =
        state |> List.exists (fun (_, action) -> action.OperationId = operationId)

    /// Applies the event to the action that owns its operation id. The same list comes back when the event
    /// changes nothing.
    let applyProgress (progress: VersionControlProgressDto) (state: LfsRunningActionsState) : LfsRunningActionsState =
        let next =
            state
            |> List.map (fun (scopeId, action) ->
                if action.OperationId = progress.OperationId then
                    scopeId, LfsRunningAction.withProgress progress action
                else
                    scopeId, action
            )

        if List.forall2 (fun (_, before) (_, after) -> obj.ReferenceEquals(before, after)) state next then
            state
        else
            next

    /// The running actions of one ARC in start order. The previous result comes back when it holds the same action
    /// records, so a change in another ARC leaves the array that the file tree and the sidebar read alone.
    let forScope
        (scopeId: string option)
        (previous: LfsRunningAction[])
        (state: LfsRunningActionsState)
        : LfsRunningAction[] =
        let next =
            match scopeId with
            | None -> [||]
            | Some scopeId ->
                state
                |> List.choose (fun (actionScopeId, action) -> if actionScopeId = scopeId then Some action else None)
                |> List.toArray

        if
            next.Length = previous.Length
            && Array.forall2 (fun before after -> obj.ReferenceEquals(before, after)) previous next
        then
            previous
        else
            next

/// The raw output of the running actions by operation id. It lives apart from the actions, because output arrives in
/// many batches and only the Git sidebar shows the lines. A batch changes this value and leaves the running actions
/// alone, so the file tree does not render for it.
type LfsRunningLogs = Map<string, OperationLog.OperationLog>

module LfsRunningLogs =

    let empty: LfsRunningLogs = Map.empty

    /// Appends the entries of the output to the log of its operation id.
    let append (output: VersionControlOutputDto) (logs: LfsRunningLogs) : LfsRunningLogs =
        let log =
            logs |> Map.tryFind output.OperationId |> Option.defaultValue OperationLog.empty

        Map.add output.OperationId (OperationLog.append output.Entries log) logs

    /// Drops the log of a settled action. The same map comes back when the action sent no output.
    let finish (operationId: string) (logs: LfsRunningLogs) : LfsRunningLogs =
        if Map.containsKey operationId logs then
            Map.remove operationId logs
        else
            logs

type LfsActivityController = {
    /// Activities of the open ARC, keyed by the ARC-relative path.
    activities: Map<string, LfsActivity>
    /// `run label entry action path` runs the action and marks the path busy until it settles.
    /// A path that is already busy returns Ok without running the action again.
    run:
        string
            -> FileEntry option
            -> (string -> JS.Promise<Result<unit, GitLfsActionFailure>>)
            -> string
            -> JS.Promise<Result<unit, GitLfsActionFailure>>
    /// `runAll label targets action` marks every target path busy that is not busy yet, runs the action once,
    /// and clears exactly the paths it marked. A path that is already busy stays with its owner.
    runAll:
        string
            -> (string * FileEntry option)[]
            -> (unit -> JS.Promise<Result<unit, GitLfsActionFailure>>)
            -> JS.Promise<Result<unit, GitLfsActionFailure>>
    /// `track action work` lists the action as running until work settles. The progress of the action's
    /// operation id is kept with it, see LfsRunningActionsCtx. When work fails, the output that the action sent
    /// until then is appended to the Details of the failure, after the details that work gave. Without an ARC
    /// the result of work comes back unchanged.
    track:
        LfsRunningAction
            -> (unit -> JS.Promise<Result<unit, GitLfsActionFailure>>)
            -> JS.Promise<Result<unit, GitLfsActionFailure>>
}

let LfsActivityCtx =
    React.createContext<LfsActivityController> (
        {
            activities = Map.empty
            run = fun _ _ action path -> action path
            runAll = fun _ _ action -> action ()
            track = fun _ work -> work ()
        }
    )

[<Hook>]
let useLfsActivityCtx () = React.useContext LfsActivityCtx

/// The running actions of the open ARC with their latest progress. A progress event changes this value but
/// not the LfsActivityCtx value, so the file tree does not render for every event. The value changes only when an
/// action starts or ends, or when its count or step percent changes. Output does not change it.
let LfsRunningActionsCtx = React.createContext<LfsRunningAction[]> [||]

[<Hook>]
let useLfsRunningActionsCtx () = React.useContext LfsRunningActionsCtx

/// The raw output of the running actions by operation id. Only the Git sidebar reads it, so the file tree stays out
/// of the renders that a batch of output causes.
let LfsRunningLogsCtx = React.createContext<LfsRunningLogs> LfsRunningLogs.empty

[<Hook>]
let useLfsRunningLogsCtx () = React.useContext LfsRunningLogsCtx

/// The log of the last file tree action that sent output and ended. The value survives an ARC switch and is gone
/// with the window. The readers use LastGitLog.useLastGitLog, which compares it with the log of the Git workflow.
let LfsLastLogCtx =
    React.createContext<OperationLog.FinishedOperationLog option> None

[<Hook>]
let useLfsLastLogCtx () = React.useContext LfsLastLogCtx

let private toScopeId (arcRootPath: string option) =
    arcRootPath
    |> Option.map PathHelpers.normalizePath
    |> Option.filter (String.IsNullOrWhiteSpace >> not)

[<ReactComponent>]
let LfsActivityCtxProvider (children: ReactElement) =
    let scopeId = Renderer.Context.AppStateContext.useAppStateCtx () |> toScopeId
    let state, setState = React.useState LfsActivityState.empty
    // Actions settle outside React renders, so they read and write the latest state through refs.
    let stateRef = React.useRef state
    let scopeIdRef = React.useRef scopeId

    let update next =
        stateRef.current <- next
        setState next

    let actionsState, setActionsState = React.useState LfsRunningActionsState.empty
    let actionsRef = React.useRef actionsState

    let updateActions next =
        actionsRef.current <- next
        setActionsState next

    let logsState, setLogsState = React.useState LfsRunningLogs.empty
    let logsRef = React.useRef logsState

    let updateLogs next =
        logsRef.current <- next
        setLogsState next

    let lastLog, setLastLog =
        React.useState<OperationLog.FinishedOperationLog option> None

    // Each action keeps the numbers and the output of its own operation id and ignores every other operation.
    // An event that changes no number does not touch the state.
    Renderer.IpcReceiver.useProxyReceiver<IVersionControlRendererApi> (
        (fun () -> {
            versionControlProgress =
                fun progress ->
                    if LfsRunningActionsState.owns progress.OperationId actionsRef.current then
                        let next = LfsRunningActionsState.applyProgress progress actionsRef.current

                        if not (obj.ReferenceEquals(next, actionsRef.current)) then
                            updateActions next
            versionControlOperationStarted = ignore
            versionControlOutput =
                fun output ->
                    if
                        output.Entries.Length > 0
                        && LfsRunningActionsState.owns output.OperationId actionsRef.current
                    then
                        updateLogs (LfsRunningLogs.append output logsRef.current)
        }),
        [||]
    )

    // An action keeps running in the library when the user switches the ARC, and it clears its own
    // entry when it settles. Other ARCs' entries therefore stay, so switching back shows them busy.
    React.useEffect ((fun () -> scopeIdRef.current <- scopeId), [| box scopeId |])

    let run
        (label: string)
        (entry: FileEntry option)
        (action: string -> JS.Promise<Result<unit, GitLfsActionFailure>>)
        (path: string)
        =
        promise {
            match scopeIdRef.current with
            | None -> return! action path
            | Some scopeId when LfsActivityState.isBusy scopeId path stateRef.current -> return Ok()
            | Some scopeId ->
                update (LfsActivityState.start scopeId path { Label = label; Entry = entry } stateRef.current)

                try
                    return! action path
                finally
                    update (LfsActivityState.finish scopeId path stateRef.current)
        }

    let runAll
        (label: string)
        (targets: (string * FileEntry option)[])
        (action: unit -> JS.Promise<Result<unit, GitLfsActionFailure>>)
        =
        promise {
            match scopeIdRef.current with
            | None -> return! action ()
            | Some scopeId ->
                let marked =
                    targets
                    |> Array.filter (fun (path, _) -> not (LfsActivityState.isBusy scopeId path stateRef.current))

                update (
                    marked
                    |> Array.fold
                        (fun state (path, entry) ->
                            LfsActivityState.start scopeId path { Label = label; Entry = entry } state
                        )
                        stateRef.current
                )

                try
                    return! action ()
                finally
                    update (
                        marked
                        |> Array.fold
                            (fun state (path, _) -> LfsActivityState.finish scopeId path state)
                            stateRef.current
                    )
        }

    let track (action: LfsRunningAction) (work: unit -> JS.Promise<Result<unit, GitLfsActionFailure>>) = promise {
        match scopeIdRef.current with
        | None -> return! work ()
        | Some scopeId ->
            let started = {
                action with
                    StartedAt = JS.Constructors.Date.now ()
            }

            updateActions (LfsRunningActionsState.start scopeId started actionsRef.current)

            let currentLog () =
                logsRef.current
                |> Map.tryFind action.OperationId
                |> Option.defaultValue OperationLog.empty

            try
                let! result = work ()

                return
                    result
                    |> Result.mapError (fun failure ->
                        let details =
                            match List.choose id [ failure.Details; OperationLog.tryDetails (currentLog ()) ] with
                            | [] -> None
                            | parts -> Some(String.concat "\n\n" parts)

                        { failure with Details = details }
                    )
            finally
                let log = currentLog ()

                if log.Lines.Length > 0 then
                    setLastLog (
                        Some {
                            Label = LfsRunningAction.label action
                            ArcName = Some(PathHelpers.getFileName scopeId)
                            FinishedAt = JS.Constructors.Date.now ()
                            Log = log
                        }
                    )

                updateActions (LfsRunningActionsState.finish action.OperationId actionsRef.current)
                updateLogs (LfsRunningLogs.finish action.OperationId logsRef.current)
    }

    let controller =
        React.useMemo (
            (fun () -> {
                activities = LfsActivityState.forScope scopeId state
                run = run
                runAll = runAll
                track = track
            }),
            [| box state; box scopeId |]
        )

    // The scoped array lives in state. A change of another ARC's action recomputes it and gets the stored one
    // back, so the readers of the context do not render for it. Only a change of the scoped records stores a new
    // array, and React renders this component again at once with it.
    let scopedActions, setScopedActions = React.useState<LfsRunningAction[]> [||]

    let runningActions =
        LfsRunningActionsState.forScope scopeId scopedActions actionsState

    if not (obj.ReferenceEquals(runningActions, scopedActions)) then
        setScopedActions runningActions

    LfsActivityCtx.Provider(
        controller,
        LfsRunningActionsCtx.Provider(
            runningActions,
            LfsRunningLogsCtx.Provider(logsState, LfsLastLogCtx.Provider(lastLog, children))
        )
    )
