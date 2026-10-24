module ElectronRenderer.IpcReceiverDisposableTests

open Browser.Dom
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Renderer.IpcReceiver
open Swate.Electron.Shared.IPCTypes.MainToRendererIpc
open Vitest

let private bridgeName typeName = $"FABLE_REMOTING_{typeName}"

let private setBridgeProperty name value = window?(name) <- value

let private clearBridgeProperty name =
    emitJsStatement name "delete window[$0]"

type private PathChange = {| path: string; isRename: bool |}

let private pathChange (path: string) : PathChange option =
    Some {| path = path; isRename = false |}

let private subscribePathChange handler =
    subscribeProxyReceiver<IPathChangeRendererApi> { pathChange = handler }

let rec private waitUntil (predicate: unit -> bool, attempts: int) = promise {
    if predicate () then
        return ()
    elif attempts <= 0 then
        failwith "Timed out waiting for React effect."
    else
        do! Promise.sleep 1
        return! waitUntil (predicate, attempts - 1)
}

let private waitForEffect predicate = waitUntil (predicate, 50)

[<ReactComponent>]
let private PathChangeHookProbe (marker: string, onPathChange: string -> PathChange option -> unit) =
    useProxyReceiver<IPathChangeRendererApi> ((fun () -> { pathChange = onPathChange marker }), [| box marker |])

    Html.none

Vitest.describe (
    "Renderer IPC disposable receivers",
    fun () ->
        Vitest.test (
            "pathChange listener receives events until disposed and dispose is idempotent",
            fun () ->
                let mutable callback: (PathChange option -> unit) option = None
                let mutable removeCount = 0
                let mutable received: string list = []

                let name = bridgeName "IPathChangeRendererApi"

                try
                    setBridgeProperty
                        name
                        (createObj [
                            "pathChange"
                            ==> fun (listener: PathChange option -> unit) ->
                                callback <- Some listener

                                fun () ->
                                    removeCount <- removeCount + 1
                                    callback <- None
                        ])

                    let dispose =
                        subscribePathChange (fun path ->
                            received <- (path |> Option.map _.path |> Option.defaultValue "<none>") :: received
                        )

                    Vitest.expect(callback.IsSome).toBe (true)

                    callback.Value(pathChange "before-dispose")
                    dispose ()
                    dispose ()

                    callback |> Option.iter (fun listener -> listener (pathChange "after-dispose"))

                    Vitest.expect(List.length received).toBe (1)
                    Vitest.expect(List.head received).toBe ("before-dispose")
                    Vitest.expect(removeCount).toBe (1)
                finally
                    clearBridgeProperty name
        )

        Vitest.test (
            "useProxyReceiver registers on mount, re-registers on dependency change, and disposes on unmount",
            fun () -> promise {
                let activeCallbacks = ResizeArray<PathChange option -> unit>()
                let mutable removeCount = 0
                let mutable received: string list = []

                let name = bridgeName "IPathChangeRendererApi"
                let container = document.createElement ("div") :?> Browser.Types.HTMLDivElement
                document.body.appendChild container |> ignore
                let root = ReactDOM.createRoot container
                let mutable rootUnmounted = false

                try
                    setBridgeProperty
                        name
                        (createObj [
                            "pathChange"
                            ==> fun (listener: PathChange option -> unit) ->
                                activeCallbacks.Add listener

                                fun () ->
                                    removeCount <- removeCount + 1
                                    activeCallbacks.Remove listener |> ignore
                        ])

                    let onPathChange marker (path: PathChange option) =
                        let value = path |> Option.map _.path |> Option.defaultValue "<none>"
                        received <- $"{marker}:{value}" :: received

                    root.render (PathChangeHookProbe("first", onPathChange))
                    do! waitForEffect (fun () -> activeCallbacks.Count = 1)

                    Vitest.expect(activeCallbacks.Count).toBe (1)
                    activeCallbacks[0](pathChange "before")

                    root.render (PathChangeHookProbe("second", onPathChange))
                    do! waitForEffect (fun () -> removeCount = 1 && activeCallbacks.Count = 1)

                    Vitest.expect(removeCount).toBe (1)
                    Vitest.expect(activeCallbacks.Count).toBe (1)
                    activeCallbacks[0](pathChange "after")

                    root.unmount ()
                    rootUnmounted <- true
                    do! waitForEffect (fun () -> removeCount = 2)

                    let ordered = List.rev received
                    Vitest.expect(removeCount).toBe (2)
                    Vitest.expect(List.length ordered).toBe (2)
                    Vitest.expect(List.item 0 ordered).toBe ("first:before")
                    Vitest.expect(List.item 1 ordered).toBe ("second:after")
                finally
                    if not rootUnmounted then
                        root.unmount ()

                    container.remove ()
                    clearBridgeProperty name
            }
        )
)
