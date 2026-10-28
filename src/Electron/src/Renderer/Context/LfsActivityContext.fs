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

    /// The last known entries of the busy files, grouped by the normalized path of their parent folder. A tree
    /// rebuild asks for one folder at a time, so the grouping is computed once per change of the activities.
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

    /// Paths are normalized the way PathHelpers.isSameOrDescendantPathForFsComparison normalizes them.
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
}

let LfsActivityCtx =
    React.createContext<LfsActivityController> (
        {
            activities = Map.empty
            run = fun _ _ action path -> action path
            runAll = fun _ _ action -> action ()
        }
    )

[<Hook>]
let useLfsActivityCtx () = React.useContext LfsActivityCtx

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

    let controller =
        React.useMemo (
            (fun () -> {
                activities = LfsActivityState.forScope scopeId state
                run = run
                runAll = runAll
            }),
            [| box state; box scopeId |]
        )

    LfsActivityCtx.Provider(controller, children)
