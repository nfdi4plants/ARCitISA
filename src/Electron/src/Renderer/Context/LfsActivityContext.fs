/// Running large-file actions (Download, Free) per ARC. The state lives above the
/// sidebar panels, so a panel switch that remounts the file tree keeps the busy state, and an
/// action that settles while the file tree is unmounted still clears its entry.
module Renderer.Context.LfsActivityContext

open System
open System.Collections.Generic
open Fable.Core
open Feliz
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
/// operation (per-file events and the like) do not matter to the file tree.
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
/// library reported for OperationId. Log holds the raw output that it sent for OperationId.
type LfsRunningAction = {
    OperationId: string
    Kind: LfsActionKind
    /// The ARC-relative path of the folder or the file the action runs on.
    Target: string
    IsFolder: bool
    FileCount: int
    /// Files done and files requested by the latest count event of a batch. None before the first event.
    Count: (int * int) option
    /// Percent of the current transfer step while Git LFS reports one. None when no meter with numbers is active.
    StepPercent: float option
    Log: OperationLog.OperationLog
}

module LfsRunningAction =

    /// Applies one progress event. The action comes back as the same instance when the event changes nothing, so
    /// the caller can skip the state update.
    let withProgress (progress: VersionControlProgressDto) (action: LfsRunningAction) : LfsRunningAction =
        match progress.PhaseCode with
        | LfsProgressPhase.MaterializeFiles
        | LfsProgressPhase.DematerializeFiles ->
            match progress.Completed, progress.Total with
            | Some completed, Some total ->
                let count = Some(int completed, int total)

                if count = action.Count then
                    action
                else
                    { action with Count = count }
            | _ -> action
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
        | _ -> action

    /// The live text of the action: "k/N" with the percent of the current step appended for a folder, and the
    /// percent alone for a single file. A folder reads 0 of FileCount before the first count event.
    let progressText (action: LfsRunningAction) : string option =
        let percentText =
            action.StepPercent
            |> Option.map (fun percent -> string (int (Math.Floor percent)) + "%")

        if action.IsFolder then
            let completed, total = action.Count |> Option.defaultValue (0, action.FileCount)

            let counts = $"{completed}/{total}"

            match percentText with
            | Some percent -> Some $"{counts} {percent}"
            | None -> Some counts
        else
            percentText

    /// The share of finished files for a folder and the step percent for a single file.
    let progressPercent (action: LfsRunningAction) : float option =
        if action.IsFolder then
            match action.Count with
            | Some(completed, total) when total > 0 -> Some(float completed / float total * 100.0)
            | _ -> None
        else
            action.StepPercent

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

    /// Appends the entries to the log of the action that owns the operation id of the output. Output of an
    /// unknown operation id leaves the state as it is.
    let applyOutput (output: VersionControlOutputDto) (state: LfsRunningActionsState) : LfsRunningActionsState =
        if output.Entries.Length = 0 || not (owns output.OperationId state) then
            state
        else
            state
            |> List.map (fun (scopeId, action) ->
                if action.OperationId = output.OperationId then
                    scopeId,
                    {
                        action with
                            Log = OperationLog.append output.Entries action.Log
                    }
                else
                    scopeId, action
            )

    /// The running actions of one ARC in start order.
    let forScope (scopeId: string option) (state: LfsRunningActionsState) : LfsRunningAction[] =
        match scopeId with
        | None -> [||]
        | Some scopeId ->
            state
            |> List.choose (fun (actionScopeId, action) -> if actionScopeId = scopeId then Some action else None)
            |> List.toArray

type LfsActivityController = {
    /// Activities of the open ARC, keyed by the ARC-relative path.
    activities: Map<string, LfsActivity>
    /// `run label entry action path` runs the action and marks the path busy until it settles.
    /// A path that is already busy returns Ok without running the action again.
    run:
        string
            -> FileEntry option
            -> (string -> JS.Promise<Result<unit, string>>)
            -> string
            -> JS.Promise<Result<unit, string>>
    /// `runAll label targets action` marks every target path busy that is not busy yet, runs the action once,
    /// and clears exactly the paths it marked. A path that is already busy stays with its owner.
    runAll:
        string
            -> (string * FileEntry option)[]
            -> (unit -> JS.Promise<Result<unit, string>>)
            -> JS.Promise<Result<unit, string>>
    /// `track action work` lists the action as running until work settles. The progress of the action's
    /// operation id is kept with it, see LfsRunningActionsCtx.
    track: LfsRunningAction -> (unit -> JS.Promise<Result<unit, string>>) -> JS.Promise<Result<unit, string>>
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
/// not the LfsActivityCtx value, so the file tree does not render for every event.
let LfsRunningActionsCtx = React.createContext<LfsRunningAction[]> [||]

[<Hook>]
let useLfsRunningActionsCtx () = React.useContext LfsRunningActionsCtx

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
                    let next = LfsRunningActionsState.applyOutput output actionsRef.current

                    if not (obj.ReferenceEquals(next, actionsRef.current)) then
                        updateActions next
        }),
        [||]
    )

    // An action keeps running in the library when the user switches the ARC, and it clears its own
    // entry when it settles. Other ARCs' entries therefore stay, so switching back shows them busy.
    React.useEffect ((fun () -> scopeIdRef.current <- scopeId), [| box scopeId |])

    let run
        (label: string)
        (entry: FileEntry option)
        (action: string -> JS.Promise<Result<unit, string>>)
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
        (action: unit -> JS.Promise<Result<unit, string>>)
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

    let track (action: LfsRunningAction) (work: unit -> JS.Promise<Result<unit, string>>) = promise {
        match scopeIdRef.current with
        | None -> return! work ()
        | Some scopeId ->
            updateActions (LfsRunningActionsState.start scopeId action actionsRef.current)

            try
                return! work ()
            finally
                updateActions (LfsRunningActionsState.finish action.OperationId actionsRef.current)
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

    let runningActions =
        React.useMemo (
            (fun () -> LfsRunningActionsState.forScope scopeId actionsState),
            [| box actionsState; box scopeId |]
        )

    LfsActivityCtx.Provider(controller, LfsRunningActionsCtx.Provider(runningActions, children))
