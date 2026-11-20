module Renderer.Context.ArcSyncContext

open Feliz
open Swate.Electron.Shared.IPCTypes

type ArcSyncState = {
    IsSyncing: bool
    /// The message of the last failed sync. Clears when a sync starts and on every finished message.
    FailedMessage: string option
    /// Files whose unsaved edits were replaced since the user last dismissed the notice.
    ReplacedUnsaved: string[]
} with

    static member init() : ArcSyncState = {
        IsSyncing = false
        FailedMessage = None
        ReplacedUnsaved = [||]
    }

    static member started(current: ArcSyncState) : ArcSyncState = {
        current with
            IsSyncing = true
            FailedMessage = None
    }

    /// A finished sync means memory caught up, so an earlier failure goes away. The replaced files add up to the
    /// earlier ones, because the user has not dismissed those yet.
    static member finished (replacedUnsaved: string[]) (current: ArcSyncState) : ArcSyncState = {
        current with
            IsSyncing = false
            FailedMessage = None
            ReplacedUnsaved = Array.distinct (Array.append current.ReplacedUnsaved replacedUnsaved)
    }

    static member failed (message: string) (current: ArcSyncState) : ArcSyncState = {
        current with
            IsSyncing = false
            FailedMessage = Some message
    }

type ArcSyncController = {
    state: ArcSyncState
    /// Calls the listener with the changed paths of every finished sync. Returns the function that removes the listener.
    subscribeSyncFinished: (string[] -> unit) -> (unit -> unit)
    dismissReplaced: unit -> unit
}

let ArcSyncCtx =
    React.createContext<ArcSyncController> (
        {
            state = ArcSyncState.init ()
            subscribeSyncFinished = fun _ -> ignore
            dismissReplaced = ignore
        }
    )

[<Hook>]
let useArcSyncCtx () = React.useContext ArcSyncCtx

/// Receives the sync messages of the main process. The state starts over when the ARC root changes, so the failure of
/// one ARC does not show for the next one. A rename of the same ARC root keeps the state.
[<ReactComponent>]
let Provider (isRename: bool, children: ReactElement) =
    let arcRoot = Renderer.Context.AppStateContext.useAppStateCtx ()
    let state, setState = React.useStateWithUpdater (ArcSyncState.init ())
    let listeners = React.useRef (ResizeArray<string[] -> unit>())

    React.useEffect (
        (fun () ->
            if not isRename then
                setState (fun _ -> ArcSyncState.init ())
        ),
        [| box arcRoot |]
    )

    Renderer.IpcReceiver.useProxyReceiver<IArcSyncRendererApi> (
        (fun () -> {
            arcSyncStarted = fun () -> setState ArcSyncState.started
            arcSyncFinished =
                fun finished ->
                    setState (ArcSyncState.finished finished.replacedUnsaved)

                    for listener in listeners.current.ToArray() do
                        listener finished.changedPaths
            arcSyncFailed = fun message -> setState (ArcSyncState.failed message)
        }),
        [||]
    )

    let subscribeSyncFinished =
        React.useCallback (
            (fun (listener: string[] -> unit) ->
                listeners.current.Add listener
                fun () -> listeners.current.Remove listener |> ignore
            ),
            [||]
        )

    let dismissReplaced =
        React.useCallback ((fun () -> setState (fun current -> { current with ReplacedUnsaved = [||] })), [||])

    let controller =
        React.useMemo (
            (fun () -> {
                state = state
                subscribeSyncFinished = subscribeSyncFinished
                dismissReplaced = dismissReplaced
            }),
            [| box state |]
        )

    ArcSyncCtx.Provider(controller, children)
