module ElectronRenderer.ArcSyncNoticeRtlTests

open Browser.Dom
open Fable.Core.JsInterop
open Feliz
open Renderer.Components.MainContent.ArcSyncNotice
open Renderer.Context.ArcSyncContext
open Vitest

Vitest.afterEach (fun () -> RTL.cleanup ())

Vitest.test (
    "the notice shows the running text while a sync runs",
    fun () ->
        RTL.render (ArcSyncNotice(true, None, [||], ignore)) |> ignore

        let notice = RTL.screen.getByTestId "arc-sync-running"
        Vitest.expect(notice.textContent.Contains "External changes detected, waiting").toBe true
)

Vitest.test (
    "the notice shows the failure message",
    fun () ->
        RTL.render (ArcSyncNotice(false, Some "boom", [||], ignore)) |> ignore

        let notice = RTL.screen.getByTestId "arc-sync-failed"
        Vitest.expect(notice.textContent.Contains "memory is behind the files on disk").toBe true
        Vitest.expect(notice.textContent.Contains "boom").toBe true
)

Vitest.test (
    "the notice lists replaced files and dismisses",
    fun () -> promise {
        let dismissed = ref 0

        RTL.render (
            ArcSyncNotice(
                false,
                None,
                [| "assays/A/isa.assay.xlsx" |],
                (fun () -> dismissed.Value <- dismissed.Value + 1)
            )
        )
        |> ignore

        let notice = RTL.screen.getByTestId "arc-sync-replaced"
        Vitest.expect(notice.textContent.Contains "assays/A/isa.assay.xlsx").toBe true

        do! RTL.act (fun () -> promise { (RTL.screen.getByLabelText "Dismiss").click () })

        Vitest.expect(dismissed.Value).toBe 1
    }
)

Vitest.test (
    "the notice shows nothing without a sync state",
    fun () ->
        RTL.render (ArcSyncNotice(false, None, [||], ignore)) |> ignore

        Vitest.expect((RTL.screen.queryByTestId "arc-sync-running").IsNone).toBe true
        Vitest.expect((RTL.screen.queryByTestId "arc-sync-failed").IsNone).toBe true
        Vitest.expect((RTL.screen.queryByTestId "arc-sync-replaced").IsNone).toBe true
)

[<ReactComponent>]
let private AreaHarness (expose: (ArcSyncState -> unit) -> unit) =
    let state, setState =
        React.useState {
            ArcSyncState.init () with
                IsSyncing = true
        }

    expose setState

    ArcSyncCtx.Provider(
        {
            state = state
            subscribeSyncFinished = fun _ -> ignore
            dismissReplaced = ignore
        },
        ArcSyncArea(Html.div [ prop.testId "page-child" ])
    )

Vitest.test (
    "input is blocked during a sync and the page stays mounted",
    fun () -> promise {
        let setState = ref (fun (_: ArcSyncState) -> ())
        RTL.render (AreaHarness(fun setter -> setState.Value <- setter)) |> ignore

        let before = RTL.screen.getByTestId "page-child"
        Vitest.expect(before.parentElement.hasAttribute "inert").toBe true

        do! RTL.act (fun () -> promise { setState.Value(ArcSyncState.init ()) })

        let after = RTL.screen.getByTestId "page-child"
        Vitest.expect(after.parentElement.hasAttribute "inert").toBe false
        Vitest.expect(obj.ReferenceEquals(before, after)).toBe true
    }
)

type private SyncFinished = {|
    changedPaths: string[]
    replacedUnsaved: string[]
|}

/// Stands in for the bridge of the main process and keeps the listeners that the provider registers.
type private SyncBridge = {
    Started: (unit -> unit) ref
    Finished: (SyncFinished -> unit) ref
    Failed: (string -> unit) ref
}

let private installSyncBridge () : SyncBridge =
    let bridge = {
        Started = ref ignore
        Finished = ref ignore
        Failed = ref ignore
    }

    window?FABLE_REMOTING_IArcSyncRendererApi <-
        createObj [
            "arcSyncStarted"
            ==> fun (listener: unit -> unit) ->
                bridge.Started.Value <- listener
                ignore
            "arcSyncFinished"
            ==> fun (listener: SyncFinished -> unit) ->
                bridge.Finished.Value <- listener
                ignore
            "arcSyncFailed"
            ==> fun (listener: string -> unit) ->
                bridge.Failed.Value <- listener
                ignore
        ]

    bridge

let private removeSyncBridge () =
    emitJsStatement () "delete window.FABLE_REMOTING_IArcSyncRendererApi"

[<ReactComponent>]
let private ProviderProbe (changed: ResizeArray<string>) =
    let controller = useArcSyncCtx ()

    React.useEffect ((fun () -> controller.subscribeSyncFinished (fun paths -> changed.AddRange paths)), [||])

    Html.div [
        prop.testId "probe"
        prop.text (
            String.concat "|" [
                string controller.state.IsSyncing
                defaultArg controller.state.FailedMessage ""
                String.concat "," controller.state.ReplacedUnsaved
            ]
        )
    ]

[<ReactComponent>]
let private ProviderHarness (changed: ResizeArray<string>, expose: (string option * bool -> unit) -> unit) =
    let root, setRoot = React.useState ((Some "arc-a", false))
    expose setRoot

    Renderer.Context.AppStateContext.AppStateCtx.Provider(fst root, Provider(snd root, ProviderProbe changed))

let private probeText () =
    RTL.screen.getByTestId("probe").textContent

Vitest.test (
    "the provider turns sync messages into notices and calls listeners",
    fun () -> promise {
        let bridge = installSyncBridge ()
        let changed = ResizeArray<string>()

        try
            RTL.render (ProviderHarness(changed, ignore)) |> ignore

            do! RTL.act (fun () -> promise { bridge.Started.Value() })
            Vitest.expect(probeText ()).toBe "true||"

            do!
                RTL.act (fun () -> promise {
                    bridge.Finished.Value {|
                        changedPaths = [| "assays/A/isa.assay.xlsx" |]
                        replacedUnsaved = [| "assays/A/isa.assay.xlsx" |]
                    |}
                })

            Vitest.expect(probeText ()).toBe "false||assays/A/isa.assay.xlsx"
            Vitest.expect(changed.ToArray()).toEqual [| "assays/A/isa.assay.xlsx" |]

            do! RTL.act (fun () -> promise { bridge.Failed.Value "boom" })
            Vitest.expect(probeText ()).toBe "false|boom|assays/A/isa.assay.xlsx"
        finally
            removeSyncBridge ()
    }
)

Vitest.test (
    "the provider keeps its state on a root rename and resets on a new ARC",
    fun () -> promise {
        let bridge = installSyncBridge ()
        let setRoot = ref (fun (_: string option * bool) -> ())

        try
            RTL.render (ProviderHarness(ResizeArray<string>(), (fun setter -> setRoot.Value <- setter)))
            |> ignore

            do! RTL.act (fun () -> promise { bridge.Failed.Value "boom" })

            do! RTL.act (fun () -> promise { setRoot.Value(Some "arc-b", true) })
            Vitest.expect(probeText ()).toBe "false|boom|"

            do! RTL.act (fun () -> promise { setRoot.Value(Some "arc-c", false) })
            Vitest.expect(probeText ()).toBe "false||"
        finally
            removeSyncBridge ()
    }
)
