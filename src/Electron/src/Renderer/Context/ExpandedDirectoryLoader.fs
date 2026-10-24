module Renderer.Context.ExpandedDirectoryLoader

open Fable.Core
open Feliz
open Swate.Components.Shared
open Swate.Electron.Shared.IPCTypes
open Renderer.Context.FileTreeStore

/// What an expanded set needs from a snapshot. Expanded keeps the ids that are not known to be gone. ToLoad lists
/// the expanded directories that are in the tree but not loaded, parents first.
type ExpansionPlan = {
    Expanded: Set<string>
    ToLoad: string list
}

let planExpansion (snapshot: FileTreeSnapshot) (expandedIds: Set<string>) : ExpansionPlan =
    let states = expandedIds |> Seq.map (fun id -> id, knows snapshot id) |> Seq.toList

    let expanded =
        states
        |> List.filter (fun (_, state) -> state <> Absent)
        |> List.map fst
        |> Set.ofList

    let toLoad =
        states
        |> List.choose (fun (id, state) ->
            match state with
            | Present entry when entry.isDirectory && not (isLoaded snapshot id) -> Some id
            | _ -> None
        )
        |> List.sortBy (fun id -> (PathHelpers.normalizePath id).Split('/').Length, id)

    { Expanded = expanded; ToLoad = toLoad }

type DirectoryLoader = string -> JS.Promise<Result<FileTreeDirectoryUpdateDto, exn>>

/// Keeps the expanded directories of a file tree usable when the store changes. Expanded ids that the tree knows
/// are gone are dropped. An expanded directory that is in the tree but not loaded is loaded with loadDirectory,
/// parents first and one at a time. A directory whose load fails or does not take effect is not tried again until
/// requestLoad is called for it. The returned requestLoad loads a directory right away when it is not loaded.
[<Hook>]
let useExpandedDirectoryLoader
    (
        store: FileTreeStore,
        snapshot: FileTreeSnapshot,
        expandedIds: Set<string>,
        setExpandedIds: (Set<string> -> Set<string>) -> unit,
        loadDirectory: DirectoryLoader
    ) : string -> unit =
    let inFlight = React.useRef (None: string option)
    let skipped = React.useRef (Set.empty: Set<string>)
    let isMounted = React.useRef true
    let tick, setTick = React.useStateWithUpdater 0
    let latestSnapshot = React.useRef snapshot
    latestSnapshot.current <- snapshot

    React.useEffect (
        (fun () ->
            isMounted.current <- true

            { new System.IDisposable with
                member _.Dispose() = isMounted.current <- false
            }
        ),
        [||]
    )

    let load (path: string) =
        if inFlight.current.IsNone then
            inFlight.current <- Some path

            promise {
                try
                    match! loadDirectory path with
                    | Ok update -> store.ApplyDirectoryUpdate update
                    | Error error ->
                        skipped.current <- skipped.current.Add path
                        Browser.Dom.console.error ($"Failed to load directory '{path}'.", error.Message)
                with error ->
                    skipped.current <- skipped.current.Add path
                    Browser.Dom.console.error ($"Failed to load directory '{path}'.", error.Message)

                // A load that did not make the directory loaded would repeat forever.
                if not (isLoaded store.Snapshot path) && knows store.Snapshot path <> Absent then
                    skipped.current <- skipped.current.Add path

                inFlight.current <- None

                if isMounted.current then
                    setTick (fun current -> current + 1)
            }
            |> Promise.start

    React.useEffect (
        (fun () ->
            let plan = planExpansion snapshot expandedIds

            if plan.Expanded <> expandedIds then
                setExpandedIds (fun current -> Set.intersect current plan.Expanded)

            plan.ToLoad
            |> List.tryFind (fun path -> not (skipped.current.Contains path))
            |> Option.iter load
        ),
        [| box snapshot; box expandedIds; box tick |]
    )

    React.useCallback (
        (fun (path: string) ->
            skipped.current <- skipped.current.Remove path

            if not (isLoaded latestSnapshot.current path) then
                load path
        ),
        [||]
    )
