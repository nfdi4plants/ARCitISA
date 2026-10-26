module ElectronRenderer.LfsActivityContextTests

open Browser.Dom
open Fable.Core
open Feliz
open Renderer.Context.LfsActivityContext
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Vitest

let rec private waitUntil (predicate: unit -> bool, attempts: int) = promise {
    if predicate () then
        return ()
    elif attempts <= 0 then
        failwith "Timed out waiting for React to settle."
    else
        do! Promise.sleep 1
        return! waitUntil (predicate, attempts - 1)
}

let private waitFor predicate = waitUntil (predicate, 100)

let private fileEntry (path: string) =
    FileEntry.create (PathHelpers.getFileName path, path, false)

let private activity label path = {
    Label = label
    Entry = Some(fileEntry path)
}

[<ReactComponent>]
let private ControllerProbe (onController: LfsActivityController -> unit) =
    let controller = useLfsActivityCtx ()
    React.useEffect ((fun () -> onController controller), [| box controller |])
    Html.none

/// `showProbe = false` unmounts the consumer the way a sidebar panel switch unmounts the file tree.
[<ReactComponent>]
let private ProviderHarness (arcRootPath: string option, showProbe: bool, onController: LfsActivityController -> unit) =
    Renderer.Context.AppStateContext.AppStateCtx.Provider(
        arcRootPath,
        LfsActivityCtxProvider(
            if showProbe then
                ControllerProbe onController
            else
                Html.none
        )
    )

type private Deferred = {
    pending: JS.Promise<Result<unit, string>>
    resolve: Result<unit, string> -> unit
}

let private deferred () =
    let mutable resolveResult = ignore

    let pending = Promise.create (fun resolve _ -> resolveResult <- resolve)

    {
        pending = pending
        resolve = fun result -> resolveResult result
    }

Vitest.describe (
    "LfsActivityState",
    fun () ->
        Vitest.test (
            "a busy path missing from the listing keeps its last known entry",
            fun () ->
                let listed = [| fileEntry "runs/other.bin" |]
                let activities = Map.ofList [ "runs/big.bin", activity "Freeing" "runs/big.bin" ]

                let merged = LfsActivityState.withBusyChildren activities "runs" listed

                Vitest.expect(merged |> Array.map _.path).toEqual ([| "runs/other.bin"; "runs/big.bin" |])
        )

        Vitest.test (
            "a busy path that is still listed is not added twice",
            fun () ->
                let listed = [| fileEntry "runs/big.bin" |]

                let activities =
                    Map.ofList [ "runs/big.bin", activity "Downloading" "runs/big.bin" ]

                let merged = LfsActivityState.withBusyChildren activities "runs" listed

                Vitest.expect(obj.ReferenceEquals(merged, listed)).toBe (true)
        )

        Vitest.test (
            "a busy file of another folder is not added",
            fun () ->
                let activities = Map.ofList [ "runs/big.bin", activity "Freeing" "runs/big.bin" ]

                Vitest.expect(LfsActivityState.withBusyChildren activities "assays" [||]).toEqual ([||])
        )

        Vitest.test (
            "each ARC sees only its own activities and other ARCs' entries stay",
            fun () ->
                let state =
                    LfsActivityState.empty
                    |> LfsActivityState.start "C:/arc-a" "a.bin" (activity "Freeing" "a.bin")
                    |> LfsActivityState.start "C:/arc-b" "b.bin" (activity "Downloading" "b.bin")

                let forB = LfsActivityState.forScope (Some "C:/arc-b") state
                Vitest.expect(forB |> Map.toList |> List.map fst).toEqual ([ "b.bin" ])

                Vitest.expect(LfsActivityState.forScope None state |> Map.count).toBe (0)
                Vitest.expect(LfsActivityState.isBusy "C:/arc-a" "a.bin" state).toBe (true)

                let finished = LfsActivityState.finish "C:/arc-a" "a.bin" state
                Vitest.expect(LfsActivityState.isBusy "C:/arc-a" "a.bin" finished).toBe (false)
                Vitest.expect(LfsActivityState.isBusy "C:/arc-b" "b.bin" finished).toBe (true)
        )
)

Vitest.describe (
    "LfsActivityCtxProvider",
    fun () ->
        Vitest.test (
            "an action that settles while no consumer is mounted still clears its busy state",
            fun () -> promise {
                let container = document.createElement ("div") :?> Browser.Types.HTMLDivElement
                document.body.appendChild container |> ignore
                let root = ReactDOM.createRoot container
                let mutable latest: LfsActivityController option = None
                let onController controller = latest <- Some controller

                let busyLabel path =
                    latest
                    |> Option.bind (fun c -> Map.tryFind path c.activities)
                    |> Option.map _.Label

                try
                    root.render (ProviderHarness(Some "C:/arc", true, onController))
                    do! waitFor (fun () -> latest.IsSome)

                    let action = deferred ()
                    let mutable actionCalls = 0

                    let runAction _ =
                        actionCalls <- actionCalls + 1
                        action.pending

                    let running =
                        latest.Value.run "Downloading" (Some(fileEntry "runs/big.bin")) runAction "runs/big.bin"

                    do! waitFor (fun () -> busyLabel "runs/big.bin" = Some "Downloading")

                    let! secondStart =
                        latest.Value.run "Downloading" (Some(fileEntry "runs/big.bin")) runAction "runs/big.bin"

                    Vitest.expect(secondStart).toEqual (Ok())
                    Vitest.expect(actionCalls).toBe (1)

                    root.render (ProviderHarness(Some "C:/arc", false, onController))
                    do! Promise.sleep 0
                    latest <- None
                    root.render (ProviderHarness(Some "C:/arc", true, onController))
                    do! waitFor (fun () -> latest.IsSome)

                    Vitest.expect(busyLabel "runs/big.bin").toEqual (Some "Downloading")

                    root.render (ProviderHarness(Some "C:/arc", false, onController))
                    do! Promise.sleep 0
                    action.resolve (Error "network down")
                    let! result = running
                    Vitest.expect(result).toEqual (Error "network down")

                    latest <- None
                    root.render (ProviderHarness(Some "C:/arc", true, onController))
                    do! waitFor (fun () -> latest.IsSome)

                    Vitest.expect(latest.Value.activities.IsEmpty).toBe (true)
                finally
                    root.unmount ()
                    container.remove ()
            }
        )

        Vitest.test (
            "an action keeps its busy state across an ARC switch and back until it settles",
            fun () -> promise {
                let container = document.createElement ("div") :?> Browser.Types.HTMLDivElement
                document.body.appendChild container |> ignore
                let root = ReactDOM.createRoot container
                let mutable latest: LfsActivityController option = None
                let onController controller = latest <- Some controller
                let action = deferred ()
                let mutable actionCalls = 0

                let runAction _ =
                    actionCalls <- actionCalls + 1
                    action.pending

                let busyLabel path =
                    latest
                    |> Option.bind (fun c -> Map.tryFind path c.activities)
                    |> Option.map _.Label

                try
                    root.render (ProviderHarness(Some "C:/arc-a", true, onController))
                    do! waitFor (fun () -> latest.IsSome)

                    let running = latest.Value.run "Freeing" None runAction "a.bin"
                    do! waitFor (fun () -> busyLabel "a.bin" = Some "Freeing")

                    root.render (ProviderHarness(Some "C:/arc-b", true, onController))
                    do! waitFor (fun () -> busyLabel "a.bin" = None)

                    root.render (ProviderHarness(Some "C:/arc-a", true, onController))
                    do! waitFor (fun () -> busyLabel "a.bin" = Some "Freeing")

                    let! secondStart = latest.Value.run "Freeing" None runAction "a.bin"
                    Vitest.expect(secondStart).toEqual (Ok())
                    Vitest.expect(actionCalls).toBe (1)

                    action.resolve (Ok())
                    let! _ = running
                    do! waitFor (fun () -> busyLabel "a.bin" = None)

                    Vitest.expect(latest.Value.activities.IsEmpty).toBe (true)
                finally
                    root.unmount ()
                    container.remove ()
            }
        )
)
