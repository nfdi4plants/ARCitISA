module ElectronRenderer.ArcSyncNoticeRtlTests

open Browser.Dom
open Fable.Core.JsInterop
open Feliz
open Renderer.Components.MainContent.ArcSyncNotice
open Renderer.Context.ArcSyncContext
open Swate.Components.Composite.Layout
open Swate.Components.Primitive.Banner.Types
open Vitest

Vitest.afterEach (fun () -> RTL.cleanup ())

[<ReactComponent>]
let private SyncLayout (includeDependency: bool) =
    let syncBanners = useBannerEntries ()

    let dependencyBanners =
        if includeDependency then
            [|
                BannerEntry.create ("dependency-banner", "Dependency warning", variant = BannerVariant.Warning)
            |]
        else
            [||]

    Layout.Main(
        children = ArcSyncArea(Html.div [ prop.testId "page-child" ]),
        navbar = Html.div [ prop.testId "navbar-child" ],
        banners = Array.append dependencyBanners syncBanners
    )

[<ReactComponent>]
let private LayoutHarness
    (initialState: ArcSyncState, expose: (ArcSyncState -> unit) -> unit, includeDependency: bool)
    =
    let state, setState = React.useState initialState
    expose setState

    ArcSyncCtx.Provider(
        {
            state = state
            subscribeSyncFinished = fun _ -> ignore
            dismissReplaced = fun () -> setState { state with ReplacedUnsaved = [||] }
        },
        SyncLayout includeDependency
    )

let private renderNotices state =
    RTL.render (LayoutHarness(state, ignore, false)) |> ignore

let private statusText () =
    (RTL.screen.getByTestId "arc-sync-status").textContent

Vitest.test (
    "a running load adds no banner, blocks the page with the wait cursor and tells screen readers that it waits",
    fun () ->
        renderNotices {
            ArcSyncState.init () with
                IsSyncing = true
        }

        let area = RTL.screen.getByTestId "arc-sync-area"
        let page = RTL.screen.getByTestId "page-child"
        let status = RTL.screen.getByTestId "arc-sync-status"

        Vitest.expect((RTL.screen.queryByTestId "layout-main-banners").IsNone).toBe true
        Vitest.expect(page.parentElement.hasAttribute "inert").toBe true
        Vitest.expect(area.classList.contains "swt:cursor-wait").toBe true
        Vitest.expect(status.getAttribute "role").toBe "status"
        Vitest.expect(area.contains status).toBe true
        Vitest.expect(page.parentElement.contains status).toBe false
        Vitest.expect(status.textContent).toBe "Loading external changes…"
)

Vitest.test (
    "the replaced-edit banner stays while a load runs",
    fun () ->
        renderNotices {
            ArcSyncState.init () with
                IsSyncing = true
                ReplacedUnsaved = [| "assays/A/isa.assay.xlsx" |]
        }

        let notice = RTL.screen.getByTestId "arc-sync-replaced"
        Vitest.expect(notice.textContent.Contains "assays/A/isa.assay.xlsx").toBe true
        Vitest.expect((RTL.screen.queryByLabelText "Close").IsSome).toBe true
)

Vitest.test (
    "the failure banner shows the error and cannot be closed",
    fun () ->
        renderNotices {
            ArcSyncState.init () with
                FailedMessage = Some "boom"
        }

        let notice = RTL.screen.getByTestId "arc-sync-failed"
        Vitest.expect(notice.textContent.Contains "memory is behind the files on disk").toBe true
        Vitest.expect(notice.textContent.Contains "boom").toBe true
        Vitest.expect((RTL.screen.queryByLabelText "Close").IsNone).toBe true
)

Vitest.test (
    "closing the replaced-edit banner leaves the failure and dependency banners visible",
    fun () -> promise {
        RTL.render (
            LayoutHarness(
                {
                    IsSyncing = false
                    FailedMessage = Some "boom"
                    ReplacedUnsaved = [| "assays/A/isa.assay.xlsx"; "studies/B/isa.study.xlsx" |]
                },
                ignore,
                true
            )
        )
        |> ignore

        let notice = RTL.screen.getByTestId "arc-sync-replaced"
        Vitest.expect(notice.textContent.Contains "assays/A/isa.assay.xlsx").toBe true
        Vitest.expect(notice.textContent.Contains "studies/B/isa.study.xlsx").toBe true
        Vitest.expect(RTL.screen.queryAllByRole("alert").Count).toBe 3

        do! RTL.act (fun () -> promise { (RTL.screen.getByLabelText "Close").click () })

        Vitest.expect((RTL.screen.queryByTestId "arc-sync-replaced").IsNone).toBe true
        Vitest.expect((RTL.screen.queryByTestId "arc-sync-failed").IsSome).toBe true
        Vitest.expect((RTL.screen.queryByTestId "dependency-banner").IsSome).toBe true
        Vitest.expect(RTL.screen.queryAllByRole("alert").Count).toBe 2
    }
)

Vitest.test (
    "the layout has no banners, no wait cursor and no status without a sync state",
    fun () ->
        renderNotices (ArcSyncState.init ())

        Vitest.expect((RTL.screen.queryByTestId "arc-sync-failed").IsNone).toBe true
        Vitest.expect((RTL.screen.queryByTestId "arc-sync-replaced").IsNone).toBe true
        Vitest.expect((RTL.screen.queryByTestId "layout-main-banners").IsNone).toBe true
        Vitest.expect((RTL.screen.getByTestId "arc-sync-area").classList.contains "swt:cursor-wait").toBe false
        Vitest.expect(statusText ()).toBe ""
)

Vitest.test (
    "input is blocked only during a sync and layout updates keep the page and navbar mounted",
    fun () -> promise {
        let setState = ref (fun (_: ArcSyncState) -> ())

        RTL.render (LayoutHarness(ArcSyncState.init (), (fun setter -> setState.Value <- setter), false))
        |> ignore

        let before = RTL.screen.getByTestId "page-child"
        let navbarBefore = RTL.screen.getByTestId "navbar-child"
        Vitest.expect(before.parentElement.hasAttribute "inert").toBe false

        do! RTL.act (fun () -> promise { setState.Value(ArcSyncState.started (ArcSyncState.init ())) })

        Vitest.expect(before.parentElement.hasAttribute "inert").toBe true
        Vitest.expect(statusText ()).toBe "Loading external changes…"

        do! RTL.act (fun () -> promise { setState.Value(ArcSyncState.failed "boom" (ArcSyncState.init ())) })

        Vitest.expect(before.parentElement.hasAttribute "inert").toBe false
        Vitest.expect(statusText ()).toBe ""
        Vitest.expect((RTL.screen.queryByTestId "arc-sync-failed").IsSome).toBe true

        do! RTL.act (fun () -> promise { setState.Value(ArcSyncState.init ()) })

        let after = RTL.screen.getByTestId "page-child"
        let navbarAfter = RTL.screen.getByTestId "navbar-child"
        Vitest.expect(after.parentElement.hasAttribute "inert").toBe false
        Vitest.expect(obj.ReferenceEquals(before, after)).toBe true
        Vitest.expect(obj.ReferenceEquals(navbarBefore, navbarAfter)).toBe true
        Vitest.expect((RTL.screen.queryByTestId "layout-main-banners").IsNone).toBe true
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
