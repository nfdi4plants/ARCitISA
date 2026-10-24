/// The large object (Git LFS) state of the open ARC. The main process owns the index and pushes snapshots.
/// This store keeps the latest one and joins it to file tree rows by path at render time.
module Renderer.Context.LargeObjectStore

open Fable.Core
open Feliz
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOHelper
open Swate.Electron.Shared.IPCTypes
open Swate.Electron.Shared.VersionControlTypes

type LargeObjectSnapshot = {
    Version: int
    State: LargeObjectIndexStatus
    CaseInsensitive: bool
    ByPathKey: Map<PathKey, ObjectStateDto>
    ByComparisonKey: Map<string, ObjectStateDto>
}

module LargeObjectSnapshot =

    let empty: LargeObjectSnapshot = {
        Version = 0
        State = LargeObjectIndexStatus.NotLoaded
        CaseInsensitive = false
        ByPathKey = Map.empty
        ByComparisonKey = Map.empty
    }

    let ofIpc (snapshot: LargeObjectIndexSnapshot) : LargeObjectSnapshot = {
        Version = snapshot.version
        State = snapshot.state
        CaseInsensitive = snapshot.caseInsensitive
        ByPathKey =
            snapshot.entries
            |> Array.map (fun dto -> PathKey.ofPath snapshot.caseInsensitive dto.Path, dto)
            |> Map.ofArray
        ByComparisonKey =
            snapshot.entries
            |> Array.map (fun dto -> dto.Path, dto)
            |> buildComparisonKeyMap
    }

    /// The exact path key wins. The Unicode comparison key finds names that differ only in their normalization form.
    let tryFind (relativePath: string) (snapshot: LargeObjectSnapshot) : ObjectStateDto option =
        match Map.tryFind (PathKey.ofPath snapshot.CaseInsensitive relativePath) snapshot.ByPathKey with
        | Some found -> Some found
        | None -> Map.tryFind (PathHelpers.normalizeForUnicodeComparison relativePath) snapshot.ByComparisonKey

type LargeObjectStore() =
    let mutable snapshot = LargeObjectSnapshot.empty
    let listeners = ResizeArray<unit -> unit>()

    let notify () =
        for listener in listeners.ToArray() do
            listener ()

    member _.Snapshot = snapshot

    member _.Subscribe(listener: unit -> unit) : unit -> unit =
        listeners.Add listener
        fun () -> listeners.Remove listener |> ignore

    /// Drops snapshots older than the current one. At the same version a snapshot applies when its state differs
    /// (loading, then ready). A notLoaded answer at the same version is the older fact and never replaces anything.
    member _.Apply(incoming: LargeObjectIndexSnapshot) =
        let isNewer =
            incoming.version > snapshot.Version
            || (incoming.version = snapshot.Version
                && incoming.state <> snapshot.State
                && incoming.state <> LargeObjectIndexStatus.NotLoaded)

        if isNewer then
            snapshot <- LargeObjectSnapshot.ofIpc incoming
            notify ()

    /// Forgets the state of the previous ARC root.
    member _.Clear() =
        if snapshot <> LargeObjectSnapshot.empty then
            snapshot <- LargeObjectSnapshot.empty
            notify ()

    member _.TryFind(relativePath: string) : ObjectStateDto option =
        LargeObjectSnapshot.tryFind relativePath snapshot

/// The store that receives pushes from the main process. The provider sets it while it is mounted.
let mutable private activeStore: LargeObjectStore option = None

let setActiveStore (store: LargeObjectStore option) = activeStore <- store

/// Handler for the largeObjectIndexUpdate push. Pushes that arrive without a mounted provider are dropped,
/// because the provider pulls the current state when it mounts.
let forwardToActiveStore (snapshot: LargeObjectIndexSnapshot) =
    activeStore |> Option.iter (fun store -> store.Apply snapshot)

let LargeObjectStoreCtx = React.createContext<LargeObjectStore> (LargeObjectStore())

/// Owns the store of the open ARC. It pulls the vault's current index on mount and whenever the ARC root changes.
/// Pushes reach the store through forwardToActiveStore, which is registered before the pull starts.
[<ReactComponent>]
let LargeObjectStoreProviderWith
    (arcRootPath: string option, pull: unit -> JS.Promise<Result<LargeObjectIndexSnapshot, exn>>, children: ReactElement) =
    let store, _ = React.useState (fun () -> LargeObjectStore())

    React.useEffect (
        (fun () ->
            setActiveStore (Some store)

            { new System.IDisposable with
                member _.Dispose() =
                    if activeStore |> Option.exists (fun active -> obj.ReferenceEquals(active, store)) then
                        setActiveStore None
            }
        ),
        [| box store |]
    )

    React.useEffect (
        (fun () ->
            store.Clear()
            let isCurrent = ref true

            if arcRootPath.IsSome then
                promise {
                    match! pull () with
                    | Ok snapshot ->
                        if isCurrent.Value then
                            store.Apply snapshot
                    | Error error ->
                        Browser.Dom.console.error ("Failed to load the large object index.", error.Message)
                }
                |> Promise.catch (fun error ->
                    Browser.Dom.console.error ("Failed to load the large object index.", error.Message)
                )
                |> Promise.start

            { new System.IDisposable with
                member _.Dispose() = isCurrent.Value <- false
            }
        ),
        [| box store; box arcRootPath |]
    )

    LargeObjectStoreCtx.Provider(store, children)

[<Hook>]
let useLargeObjectStore () = React.useContext LargeObjectStoreCtx

/// A lookup from an ARC-relative path to its large object. Its identity changes only when the store applies a
/// newer snapshot, so memoized rows that depend on it re-render exactly then.
[<Hook>]
let useLargeObjectLookup () : string -> ObjectStateDto option =
    let store = useLargeObjectStore ()

    let snapshot =
        React.useSyncExternalStore (
            (fun (callback: unit -> unit) -> store.Subscribe callback),
            (fun () -> store.Snapshot)
        )

    React.useMemo ((fun () -> fun (path: string) -> LargeObjectSnapshot.tryFind path snapshot), [| box snapshot |])

[<Hook>]
let useLargeObject (relativePath: string) : ObjectStateDto option =
    let lookup = useLargeObjectLookup ()
    lookup relativePath
