module ElectronRenderer.ProvenanceGraphPageTests

// The Electron renderer builds without the React compiler, so a component there memoizes nothing by itself.
// The provenance graph page must not lay out its canvas again when the parent renders with the same index
// object, and it returns to the datasets when the parent passes another index object. This test renders the
// page the way the renderer does, through ReactDOM.createRoot without a compiler, counts the layouts through
// the layout seam of the page, and builds the index from a structuredClone of the data, like the renderer
// gets it over IPC. A second test checks that the page leaves a focus that the user set alone when the parent
// renders again, which needs a ref callback with a stable identity. A third test renders the target of the
// renderer with a stub for the IPC call and checks that a failed load shows its message. A fourth test does
// the same for a page that cannot show the loaded data. A fifth test opens the page again with a new request
// id after a failed load, like the sidebar button does, and expects a new load that succeeds. A sixth test
// checks the boundary of the area around a content that throws.

open Browser.Dom
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Vitest
open ARCtrl
open Arcitisa.Components.Shared.ProvenanceGraph.Types
open Arcitisa.Components.Shared.ProvenanceGraph.BuildIndex
open Arcitisa.Components.Composite.GraphCanvas.Types
open Arcitisa.Components.Composite.GraphCanvas.Layout
open Arcitisa.Components.Page.ProvenanceGraph

// The target loads the data through the IPC bridge of the arc vault. The preload is absent in tests, so the
// bridge is a stub. It counts its calls and answers with the function a test sets. The factory runs before the
// rest of the module, so it reads both variables only inside the stub.
let mutable private loadCalls = 0

let mutable private loadAnswer: int -> JS.Promise<Result<ProvenanceGraphData, exn>> =
    fun _ -> promise { return failwith "No answer set." }

Vitest.vi.mock (
    "./src/Electron/src/Renderer/Api.js",
    box (fun () ->
        createObj [
            "ipcArcVaultApi"
            ==> createObj [
                "loadProvenanceGraph"
                ==> fun () ->
                    loadCalls <- loadCalls + 1
                    loadAnswer loadCalls
            ]
        ]
    )
)
|> ignore

let rec private waitUntil (what: string) (predicate: unit -> bool) (attempts: int) = promise {
    if predicate () then
        return ()
    elif attempts <= 0 then
        failwith $"Timed out waiting for {what}."
    else
        do! Promise.sleep 10
        return! waitUntil what predicate (attempts - 1)
}

let private renderToBody (element: ReactElement) = promise {
    let container = document.createElement ("div") :?> HTMLDivElement
    document.body.appendChild container |> ignore
    let root = ReactDOM.createRoot container
    root.render element
    do! Promise.sleep 0

    return
        container,
        (fun () ->
            root.unmount ()
            container.remove ()
        )
}

[<Emit("$0.dispatchEvent(new MouseEvent('click', { bubbles: true }))")>]
let private dispatchClick (el: Element) : unit = jsNative

[<Emit("$0.focus()")>]
let private focus (el: Element) : unit = jsNative

let private isFocused (el: Element) : bool =
    obj.ReferenceEquals(document.activeElement, el)

[<Emit("structuredClone($0)")>]
let private structuredClone<'T> (value: 'T) : 'T = jsNative

let private text (value: string) = CompositeCell.createFreeText value

let private table (name: string) (headers: CompositeHeader list) (rows: CompositeCell list list) =
    let rows: ResizeArray<ResizeArray<CompositeCell>> =
        rows
        |> List.map (fun (row: CompositeCell list) -> ResizeArray<CompositeCell>(row :> seq<CompositeCell>))
        |> ResizeArray

    ArcTable.createFromRows (name, ResizeArray<CompositeHeader>(headers :> seq<CompositeHeader>), rows)

/// A study that makes two samples, an assay that reads them and an assay without tables. The data is a clone,
/// like the renderer gets it over IPC.
let private buildData () : ProvenanceGraphData =
    let headers = [
        CompositeHeader.Input IOType.Sample
        CompositeHeader.Output IOType.Sample
    ]

    let study =
        ArcStudy.create (
            identifier = "S",
            tables =
                ResizeArray [
                    table "sampling" headers [
                        [ text "source-a"; text "sample-a" ]
                        [ text "source-b"; text "sample-b" ]
                    ]
                ]
        )

    let assay =
        ArcAssay.create (
            identifier = "A",
            tables =
                ResizeArray [
                    table "extraction" headers [
                        [ text "sample-a"; text "extract-a" ]
                        [ text "sample-b"; text "extract-b" ]
                    ]
                ]
        )

    let arc =
        ArcInvestigation.create (
            identifier = "arc-1",
            studies = ResizeArray [ study ],
            assays = ResizeArray [ assay; ArcAssay.create (identifier = "Empty") ]
        )

    structuredClone (ProvenanceGraphData.ofInvestigation arc)

/// Every call builds a new index object.
let private buildIndex () : ProvenanceIndex = ProvenanceIndex.ofData (buildData ())

/// The data with a first dataset whose identifier throws when the page reads it. The index builds without the
/// identifier, and the datasets view needs it, so the load succeeds and the page fails.
[<Emit("""(() => {
    const broken = { ...$0.Datasets[0] };
    Object.defineProperty(broken, 'Identifier', { get() { throw new Error('The dataset cannot be read.') } });
    return { ...$0, Datasets: [broken, ...$0.Datasets.slice(1)] };
})()""")>]
let private withUnreadableDataset (data: ProvenanceGraphData) : ProvenanceGraphData = jsNative

/// The parent holds the index in state, like the page that loads an ARC. The counter forces a render of the
/// parent without a change of the index.
[<ReactComponent>]
let private Parent
    (
        first: ProvenanceIndex,
        second: ProvenanceIndex,
        layout: CanvasNode[] -> CanvasEdge[] -> string[] -> MeasureText -> LayoutOptions -> JS.Promise<GraphLayout>
    ) =
    let useSecond, setUseSecond = React.useState false
    let renders, setRenders = React.useState 0

    Html.div [
        Html.button [
            prop.testId "render-again"
            prop.onClick (fun _ -> setRenders (renders + 1))
            prop.text $"{renders}"
        ]
        Html.button [
            prop.testId "other-index"
            prop.onClick (fun _ -> setUseSecond true)
        ]
        Html.div [
            prop.style [ style.height 400; style.width 800 ]
            prop.children [
                ProvenanceGraph.ProvenanceGraph((if useSecond then second else first), layout = layout)
            ]
        ]
    ]

let private find (container: HTMLElement) (testId: string) : Element =
    container.querySelector $"[data-testid='{testId}']"

let private present (container: HTMLElement) (testId: string) : bool = not (isNull (find container testId))

let private crumbCount (container: HTMLElement) : int =
    container.querySelectorAll("[data-testid^='provenance-crumb-']:not([data-testid*='menu'])").length

Vitest.afterEach (fun () -> document.body.innerHTML <- "")

[<Emit("""(() => {
    globalThis.__provenancePageOriginalError = console.error;
    console.error = () => {};
})()""")>]
let private silenceConsoleError () : unit = jsNative

[<Emit("(globalThis.__provenancePageOriginalError && (console.error = globalThis.__provenancePageOriginalError), delete globalThis.__provenancePageOriginalError)")>]
let private restoreConsoleError () : unit = jsNative

/// The area of the page for the request id of the state. The button opens the page again with the next id.
[<ReactComponent>]
let private Reopen () =
    let requestId, setRequestId = React.useState "1"

    Html.div [
        Html.button [
            prop.testId "reopen"
            prop.onClick (fun _ -> setRequestId "2")
        ]
        Renderer.Components.MainContent.ProvenanceGraphArea.ProvenanceGraphArea
            requestId
            (Renderer.Components.MainContent.ProvenanceGraphTarget.ProvenanceGraphTarget())
    ]

[<ReactComponent>]
let private Throws () : ReactElement = failwith "The content failed."

Vitest.describe (
    "ProvenanceGraph page without the React compiler",
    fun () ->
        Vitest.test (
            "lays out once per view while the parent renders again with the same index object, and starts over for a new one",
            TestOptions(timeout = 60000),
            fun () -> promise {
                let first = buildIndex ()
                let second = buildIndex ()
                let layouts = ref 0

                // The real layout, with a count of its calls.
                let countingLayout
                    (nodes: CanvasNode[])
                    (edges: CanvasEdge[])
                    (expanded: string[])
                    (measure: MeasureText)
                    (options: LayoutOptions)
                    =
                    layouts.Value <- layouts.Value + 1
                    layoutGraph nodes edges expanded measure options

                let! container, cleanup = renderToBody (Parent(first, second, countingLayout))

                try
                    do! waitUntil "the datasets" (fun () -> present container "graph-node-studies/S/") 500

                    dispatchClick (find container "graph-node-studies/S/")
                    do! waitUntil "the processes" (fun () -> present container "graph-node-studies/S/::sampling") 500
                    Vitest.expect(crumbCount container).toBe 2

                    // The datasets and the processes were laid out. Nothing else is running now.
                    do! Promise.sleep 100
                    let afterNavigation = layouts.Value
                    Vitest.expect(afterNavigation).toBeGreaterThanOrEqual 2

                    // A render of the parent with the same index object leaves the view alone and starts no layout.
                    for round in 1..3 do
                        dispatchClick (find container "render-again")

                        do!
                            waitUntil
                                "the next render"
                                (fun () -> (find container "render-again").textContent = string round)
                                500

                        do! Promise.sleep 100

                    Vitest.expect(crumbCount container).toBe 2
                    Vitest.expect(present container "graph-node-studies/S/::sampling").toBe true
                    Vitest.expect(layouts.Value).toBe afterNavigation

                    // Another index object, even with the same data, is another ARC.
                    dispatchClick (find container "other-index")
                    do! waitUntil "the datasets again" (fun () -> present container "graph-node-assays/A/") 500
                    Vitest.expect(crumbCount container).toBe 1
                    Vitest.expect(present container "graph-node-studies/S/::sampling").toBe false
                    Vitest.expect(layouts.Value).toBeGreaterThan afterNavigation
                finally
                    cleanup ()
            }
        )

        Vitest.test (
            "leaves the focus where the user put it when the parent renders again",
            TestOptions(timeout = 60000),
            fun () -> promise {
                let first = buildIndex ()
                let second = buildIndex ()
                let! container, cleanup = renderToBody (Parent(first, second, layoutGraph))

                try
                    do! waitUntil "the datasets" (fun () -> present container "graph-node-assays/Empty/") 500

                    // A dataset without processes shows the message, which takes the focus after the navigation.
                    dispatchClick (find container "graph-node-assays/Empty/")
                    do! waitUntil "the message" (fun () -> present container "provenance-empty") 500
                    Vitest.expect(isFocused (find container "provenance-empty")).toBe true

                    // The user moves the focus to the first crumb. A render of the parent with the same
                    // index object must leave it there.
                    let crumb = find container "provenance-crumb-0"
                    focus crumb
                    Vitest.expect(isFocused crumb).toBe true

                    dispatchClick (find container "render-again")
                    do! waitUntil "the next render" (fun () -> (find container "render-again").textContent = "1") 500
                    do! Promise.sleep 100

                    Vitest.expect(isFocused crumb).toBe true
                finally
                    cleanup ()
            }
        )

        Vitest.test (
            "shows the message of a failed load and loads once",
            TestOptions(timeout = 60000),
            fun () -> promise {
                // The call of the bridge rejects.
                loadCalls <- 0
                loadAnswer <- fun _ -> promise { return failwith "The bridge is down." }

                let! container, cleanup =
                    renderToBody (Renderer.Components.MainContent.ProvenanceGraphTarget.ProvenanceGraphTarget())

                try
                    do! waitUntil "the load error" (fun () -> present container "provenance-graph-error") 500
                    do! Promise.sleep 50

                    Vitest
                        .expect((find container "provenance-graph-error").textContent.Contains "The bridge is down.")
                        .toBe
                        true

                    Vitest.expect(present container "provenance-graph").toBe false
                    Vitest.expect(loadCalls).toBe 1
                finally
                    cleanup ()
            }
        )

        Vitest.test (
            "shows the message of the page when the loaded data cannot be shown",
            TestOptions(timeout = 60000),
            fun () -> promise {
                // The load succeeds, and the datasets view cannot read the first dataset.
                loadCalls <- 0
                loadAnswer <- fun _ -> promise { return Ok(withUnreadableDataset (buildData ())) }

                let! container, cleanup =
                    renderToBody (Renderer.Components.MainContent.ProvenanceGraphTarget.ProvenanceGraphTarget())

                try
                    // The page shows its own alert with the breadcrumbs above it. The load itself did not fail.
                    do! waitUntil "the page error" (fun () -> present container "provenance-error") 500

                    Vitest
                        .expect(
                            (find container "provenance-error-message").textContent.Contains
                                "The dataset cannot be read."
                        )
                        .toBe
                        true

                    Vitest.expect(present container "provenance-crumb-0").toBe true
                    Vitest.expect(present container "provenance-graph-error").toBe false
                    Vitest.expect(loadCalls).toBe 1
                finally
                    cleanup ()
            }
        )

        Vitest.test (
            "loads again and shows the data when the page is opened again after a failed load",
            TestOptions(timeout = 60000),
            fun () -> promise {
                // The first load fails. The new request id of the next open mounts the target again.
                let data = buildData ()
                loadCalls <- 0

                loadAnswer <-
                    fun call ->
                        if call = 1 then
                            promise { return failwith "The bridge is down." }
                        else
                            promise { return Ok data }

                let! container, cleanup = renderToBody (Reopen())

                try
                    do! waitUntil "the load error" (fun () -> present container "provenance-graph-error") 500
                    Vitest.expect(loadCalls).toBe 1

                    dispatchClick (find container "reopen")
                    do! waitUntil "the datasets" (fun () -> present container "graph-node-assays/Empty/") 500

                    Vitest.expect(loadCalls).toBe 2
                    Vitest.expect(present container "provenance-graph-error").toBe false
                finally
                    cleanup ()
            }
        )

        Vitest.test (
            "shows the message of a content that throws inside the area",
            TestOptions(timeout = 60000),
            fun () -> promise {
                silenceConsoleError ()

                let! container, cleanup =
                    renderToBody (
                        Renderer.Components.MainContent.ProvenanceGraphArea.ProvenanceGraphArea "1" (Throws())
                    )

                try
                    do! waitUntil "the alert" (fun () -> present container "provenance-graph-error") 500

                    Vitest
                        .expect((find container "provenance-graph-error").textContent.Contains "The content failed.")
                        .toBe
                        true
                finally
                    cleanup ()
                    restoreConsoleError ()
            }
        )

)
