/// Running large-file actions (Download, Free) per ARC. The state lives above the
/// sidebar panels, so a panel switch that remounts the file tree keeps the busy state, and an
/// action that settles while the file tree is unmounted still clears its entry.
module Renderer.Context.LfsActivityContext

open System
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

    /// A Free briefly replaces the file on disk, so the listing of a folder can miss a busy file. The last known
    /// entry of each busy file in this folder is added back, which keeps its row until the action ends. The same
    /// array comes back when nothing is missing.
    let withBusyChildren
        (activities: Map<string, LfsActivity>)
        (directoryPath: string)
        (children: FileEntry[])
        : FileEntry[] =
        if activities.IsEmpty then
            children
        else
            let parent = PathHelpers.normalizePath directoryPath

            let missing =
                activities
                |> Map.toArray
                |> Array.choose (fun (_, activity) -> activity.Entry)
                |> Array.filter (fun entry ->
                    PathHelpers.tryGetParentPath entry.path |> Option.defaultValue "" = parent
                    && not (
                        children
                        |> Array.exists (fun child ->
                            PathHelpers.pathsEqual
                                (PathHelpers.normalizePath child.path)
                                (PathHelpers.normalizePath entry.path)
                        )
                    )
                )

            if missing.Length = 0 then
                children
            else
                Array.append children missing

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
}

let LfsActivityCtx =
    React.createContext<LfsActivityController> (
        {
            activities = Map.empty
            run = fun _ _ action path -> action path
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

    let controller =
        React.useMemo (
            (fun () -> {
                activities = LfsActivityState.forScope scopeId state
                run = run
            }),
            [| box state; box scopeId |]
        )

    LfsActivityCtx.Provider(controller, children)
