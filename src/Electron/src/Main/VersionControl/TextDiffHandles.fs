/// Remembers which window opened each text diff handle. The library binds a handle to the
/// window that opened it. This registry only closes the handles of a window that closes or
/// reloads, because the renderer that knew them is gone.
module Main.VersionControl.TextDiffHandles

open System
open System.Collections.Generic
open Fable.Core
open Fable.Electron.Main
open VersionControlService.Abstractions

type private OpenHandle = {
    Handle: DiffHandle
    WindowId: int option
    WorkspaceRoot: string
}

let private handles = Dictionary<string, OpenHandle>()

/// Whether the window still exists. Tests replace it, because they have no Electron windows.
let mutable isWindowAlive: int -> bool =
    fun windowId ->
        match BrowserWindow.fromId windowId with
        | Some window -> not (window.isDestroyed ())
        | None -> false

let remove (handleId: string) = handles.Remove handleId |> ignore

/// Closes one diff handle through the entry `TextDiff.Close` of the provider that opened the
/// repository. A provider without it has nothing to close.
let private closeInBackground
    (host: WorkspaceSessionHost.WorkspaceSessionHost)
    (hosted: WorkspaceSessionHost.HostedSession)
    (windowId: int)
    (handle: DiffHandle)
    =
    match hosted.Provider.Functions.TextDiff.Close with
    | Implemented close ->
        // The library binds a handle to the window of the operation that opened it, so the
        // close runs as an operation of that window. The registration happens before the
        // first await, which keeps the session open until the close is done.
        let tracked =
            host.BeginOperation(Guid.NewGuid().ToString(), Some hosted.WorkspaceRoot, Some windowId, false, ignore)

        promise {
            try
                try
                    let! _ = close hosted.Repository handle tracked.Context |> Async.StartAsPromise
                    ()
                with error ->
                    Browser.Dom.console.error ("A text diff handle could not be closed.", error.Message)
            finally
                tracked.Complete()
        }
        |> Promise.start
    | Missing _ -> ()

/// Records the handle of a finished open. When the window closed while the open ran,
/// nobody can use the handle any more, and this function closes it right away.
let recordOrClose (windowId: int option) (hosted: WorkspaceSessionHost.HostedSession) (handle: DiffHandle) =
    match windowId with
    | Some id when not (isWindowAlive id) ->
        match WorkspaceSessionHost.tryCurrent () with
        | Some host -> closeInBackground host hosted id handle
        | None -> ()
    | _ ->
        handles[handle.Id] <- {
            Handle = handle
            WindowId = windowId
            WorkspaceRoot = hosted.WorkspaceRoot
        }

let private closeHandlesOf (windowId: int) =
    let owned =
        handles.Values
        |> Seq.filter (fun entry -> entry.WindowId = Some windowId)
        |> Seq.toArray

    for entry in owned do
        handles.Remove entry.Handle.Id |> ignore

    match WorkspaceSessionHost.tryCurrent () with
    | Some host ->
        for entry in owned do
            match host.TryGetSession entry.WorkspaceRoot with
            | Some hosted -> closeInBackground host hosted windowId entry.Handle
            | None -> ()
    | None -> ()

/// Closes the handles of a reloaded window. An open that finishes after the reload records
/// its handle under the same window, and the handle closes with the window. Closing is
/// idempotent in the library.
let windowReloaded (windowId: int) = closeHandlesOf windowId

/// Closes the handles of a closed window in the background. Opens still running for it
/// find the window gone and close their handle when they finish.
let windowClosed (windowId: int) = closeHandlesOf windowId
