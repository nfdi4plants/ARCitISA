module Renderer.Context.FileTreeHooks

open Feliz
open Swate.Electron.Shared.FileIOTypes
open Renderer.Context.FileStateContext
open Renderer.Context.FileTreeStore

/// Reads a value from the store snapshot. The selector runs on every store change, and the previous result is
/// kept as long as isEqual says the new one is the same, so React sees a stable value and skips the render.
[<Hook>]
let useFileTreeSelector (selector: FileTreeSnapshot -> 'T) (isEqual: 'T -> 'T -> bool) : 'T =
    let store = (useFileStateCtx ()).fileTreeStore
    let subscribe = React.useMemo ((fun () -> store.Subscribe), [| box store |])
    let cache = React.useRef (None: 'T option)

    let getSnapshot () =
        let next = selector store.Snapshot

        match cache.current with
        | Some previous when isEqual previous next -> previous
        | _ ->
            cache.current <- Some next
            next

    React.useSyncExternalStore (subscribe, getSnapshot)

/// The whole snapshot. Only the file tree itself needs this.
[<Hook>]
let useFileTreeSnapshot () : FileTreeSnapshot =
    useFileTreeSelector id (fun left right -> obj.ReferenceEquals(left, right))

/// The entry of a path. The instance stays the same until that entry changes.
[<Hook>]
let useFileTreeEntry (path: string option) : FileEntry option =
    useFileTreeSelector
        (fun snapshot -> path |> Option.bind (tryFind snapshot))
        (fun left right ->
            match left, right with
            | None, None -> true
            | Some a, Some b -> obj.ReferenceEquals(a, b)
            | _ -> false
        )

/// The children of a directory. The array stays the same until the directory lists other children or one of
/// its children changes.
[<Hook>]
let useFileTreeChildren (path: string) : FileEntry[] =
    useFileTreeSelector
        (fun snapshot -> children snapshot path)
        (fun left right ->
            left.Length = right.Length
            && Array.forall2 (fun a b -> obj.ReferenceEquals(a, b)) left right
        )

/// Whether the tree has the path (Present), lists its absence (Absent) or has not loaded its parent (Unknown).
[<Hook>]
let useFileTreeKnows (path: string option) : KnownState =
    useFileTreeSelector
        (fun snapshot ->
            match path with
            | Some path -> knows snapshot path
            | None -> Unknown
        )
        (fun left right ->
            match left, right with
            | Present a, Present b -> obj.ReferenceEquals(a, b)
            | Absent, Absent
            | Unknown, Unknown -> true
            | _ -> false
        )
