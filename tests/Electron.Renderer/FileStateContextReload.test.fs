module ElectronRenderer.FileStateContextReloadTests

open System.Collections.Generic
open Browser.Dom
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Renderer.Components.Helper
open Renderer.Context.FileStateContext
open Renderer.Context.FileTreeStore
open Swate.Electron.Shared.IPCTypes
open Renderer.Types
open ARCtrl
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.FileIOHelper
open Swate.Components.Shared
open Swate.Components.Page.ArcFileEditor.Types
open Vitest

type private RendererPageState = Renderer.Types.PageState

let private bridgeName typeName = $"FABLE_REMOTING_{typeName}"

let private setBridgeProperty name value = window?(name) <- value

let private clearBridgeProperty name =
    emitJsStatement name "delete window[$0]"

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
let private FileTreeProbe (onFileTree: string[] -> unit) =
    let snapshot = Renderer.Context.FileTreeHooks.useFileTreeSnapshot ()

    React.useEffect (
        (fun () ->
            snapshot.Entries
            |> Seq.map (fun entry -> entry.Value.path)
            |> Seq.toArray
            |> onFileTree
        ),
        [| box snapshot |]
    )

    Html.none

[<ReactComponent>]
let private FileImportProbe (onImport: ActiveFileImportState option -> unit) =
    let fileStateCtx = useFileStateCtx ()

    React.useEffect ((fun () -> onImport fileStateCtx.activeFileImport), [| box fileStateCtx.activeFileImport |])

    Html.none

let private dirEntry (path: string) =
    FileEntry.create (path.Split('/') |> Array.last, path, true)

let private fileEntry (path: string) =
    FileEntry.create (path.Split('/') |> Array.last, path, false)

let private createSnapshot (treeVersion: int) : FileTreeSnapshotDto = {|
    treeVersion = treeVersion
    caseInsensitive = false
    entries = [|
        FileEntry.create ("arc", "", true)
        dirEntry "assays"
        dirEntry "assays/assay-1"
        fileEntry "assays/assay-1/isa.assay.xlsx"
        dirEntry "assays/assay-1/dataset"
    |]
    loadedDirectories = [| ""; "assays"; "assays/assay-1" |]
|}

let private source (arcRootPath: string option) (changeVersion: int) : FileTreeSource = {
    arcRootPath = arcRootPath
    caseInsensitive = false
    changeVersion = changeVersion
}

let private fileImportApi loadActiveImport = {
    loadActiveImport = loadActiveImport
    pickAbsolutePaths = fun () -> JS.Constructors.Promise.resolve (Ok None)
    runImport = fun _ -> JS.Constructors.Promise.resolve (Ok ImportExternalFilesResult.Completed)
    cancelImport = fun _ -> JS.Constructors.Promise.resolve (Ok())
}

/// Subscribes to nothing and returns a disposer that does nothing.
let private noopSubscription: obj = emitJsExpr () "(() => () => {})"

Vitest.describe (
    "FileStateContext reload hydration",
    fun () ->
        Vitest.test (
            "loads the current file tree snapshot when the provider mounts",
            fun () -> promise {
                let name = bridgeName "IFileTreeRendererApi"
                let importBridgeName = bridgeName "IFileImportRendererApi"
                let observedFileTrees = ResizeArray<string[]>()
                let mutable listenerRegistered = false
                let mutable disposeCalled = false
                let mutable snapshotLoadCalls = 0
                let mutable importSubscriptionRegistered = false
                let mutable importDisposeCalled = false

                let container = document.createElement ("div") :?> Browser.Types.HTMLDivElement
                document.body.appendChild container |> ignore
                let root = ReactDOM.createRoot container
                let mutable rootUnmounted = false

                try
                    setBridgeProperty
                        name
                        (createObj [
                            "fileTreeDirectoryUpdate"
                            ==> fun (_listener: FileTreeDirectoryUpdateDto -> unit) ->
                                listenerRegistered <- true

                                fun () -> disposeCalled <- true
                            "fileContentChanged" ==> noopSubscription
                            "largeObjectIndexUpdate" ==> noopSubscription
                        ])

                    setBridgeProperty
                        importBridgeName
                        (createObj [
                            "fileImportStateUpdate"
                            ==> fun (_listener: ActiveFileImportState option -> unit) ->
                                importSubscriptionRegistered <- true
                                fun () -> importDisposeCalled <- true
                        ])

                    let loadSnapshot () = promise {
                        snapshotLoadCalls <- snapshotLoadCalls + 1
                        return Ok(createSnapshot 1)
                    }

                    root.render (
                        FileStateCtxProviderWithSnapshots(
                            source (Some "arc") 1,
                            loadSnapshot,
                            fileImportApi (fun () -> JS.Constructors.Promise.resolve (Ok None)),
                            FileTreeProbe(fun paths -> observedFileTrees.Add paths)
                        )
                    )

                    do!
                        waitForEffect (fun () ->
                            observedFileTrees |> Seq.exists (Array.contains "assays/assay-1/isa.assay.xlsx")
                        )

                    Vitest.expect(listenerRegistered).toBe (true)
                    Vitest.expect(snapshotLoadCalls).toBe (1)

                    root.unmount ()
                    rootUnmounted <- true
                    do! waitForEffect (fun () -> disposeCalled)

                    Vitest.expect(disposeCalled).toBe (true)
                finally
                    if not rootUnmounted then
                        root.unmount ()

                    container.remove ()
                    clearBridgeProperty name
                    clearBridgeProperty importBridgeName
            }
        )

        Vitest.test (
            "keeps an active import visible when the file explorer child is unmounted and remounted",
            fun () -> promise {
                let fileTreeBridgeName = bridgeName "IFileTreeRendererApi"
                let importBridgeName = bridgeName "IFileImportRendererApi"
                let mutable publishImportState = ignore
                let mutable importListenerRegistered = false
                let mutable fileTreeDisposeCalled = false
                let mutable importDisposeCalled = false
                let observedImports = ResizeArray<ActiveFileImportState option>()
                let container = document.createElement ("div") :?> Browser.Types.HTMLDivElement
                document.body.appendChild container |> ignore
                let root = ReactDOM.createRoot container

                let render child =
                    root.render (
                        FileStateCtxProviderWithSnapshots(
                            source (Some "arc") 1,
                            (fun () -> JS.Constructors.Promise.resolve (Ok(createSnapshot 1))),
                            fileImportApi (fun () -> JS.Constructors.Promise.resolve (Ok None)),
                            child
                        )
                    )

                try
                    setBridgeProperty
                        fileTreeBridgeName
                        (createObj [
                            "fileTreeDirectoryUpdate"
                            ==> fun (_: FileTreeDirectoryUpdateDto -> unit) ->
                                fileTreeDisposeCalled <- false
                                fun () -> fileTreeDisposeCalled <- true
                            "fileContentChanged" ==> noopSubscription
                            "largeObjectIndexUpdate" ==> noopSubscription
                        ])

                    setBridgeProperty
                        importBridgeName
                        (createObj [
                            "fileImportStateUpdate"
                            ==> fun (listener: ActiveFileImportState option -> unit) ->
                                publishImportState <- listener
                                importListenerRegistered <- true
                                fun () -> importDisposeCalled <- true
                        ])

                    render (FileImportProbe observedImports.Add)
                    do! waitForEffect (fun () -> importListenerRegistered)

                    let activeImport =
                        Some {
                            requestId = "survives-remount"
                            phase = FileImportPhase.Copying
                        }

                    publishImportState activeImport
                    do! waitForEffect (fun () -> observedImports |> Seq.contains activeImport)

                    render Html.none
                    do! Promise.sleep 0
                    render (FileImportProbe observedImports.Add)
                    do! waitForEffect (fun () -> observedImports |> Seq.filter ((=) activeImport) |> Seq.length >= 2)
                finally
                    root.unmount ()
                    container.remove ()
                    clearBridgeProperty fileTreeBridgeName
                    clearBridgeProperty importBridgeName
            }
        )
)

Vitest.describe (
    "File explorer state reconciliation",
    fun () ->
        Vitest.test (
            "isSelectionMissing clears a known-absent selection and keeps an unknown one",
            fun () ->
                let store = FileTreeStore()
                store.BeginLoad()
                store.ApplySnapshot(createSnapshot 1)

                // assays/assay-1 is loaded and lists no assay-b. The dataset folder is not loaded.
                Vitest
                    .expect(
                        FileExplorerStateReconciliation.isSelectionMissing
                            store.Snapshot
                            (Some "assays/assay-1/missing.txt")
                    )
                    .toBe (true)

                Vitest
                    .expect(
                        FileExplorerStateReconciliation.isSelectionMissing
                            store.Snapshot
                            (Some "assays/assay-1/dataset/deep/file.txt")
                    )
                    .toBe (false)

                Vitest
                    .expect(
                        FileExplorerStateReconciliation.isSelectionMissing
                            store.Snapshot
                            (Some "assays/assay-1/isa.assay.xlsx")
                    )
                    .toBe (false)

                Vitest.expect(FileExplorerStateReconciliation.isSelectionMissing store.Snapshot None).toBe (false)
        )

        Vitest.test (
            "shouldResetPageStateAfterSelectionRemoval only resets file-preview states",
            fun () ->
                let workflowArcFile =
                    ArcWorkflow.init "DeletePreviewWorkflow"
                    |> Swate.Components.Shared.ARCtrlHelper.ArcFiles.Workflow

                Vitest
                    .expect(
                        FileExplorerStateReconciliation.shouldResetPageStateAfterSelectionRemoval (
                            Some(RendererPageState.ArcFilePage(workflowArcFile, None))
                        )
                    )
                    .toBe (true)

                Vitest
                    .expect(
                        FileExplorerStateReconciliation.shouldResetPageStateAfterSelectionRemoval (
                            Some(RendererPageState.MarkdownPage "# md")
                        )
                    )
                    .toBe (true)

                Vitest
                    .expect(
                        FileExplorerStateReconciliation.shouldResetPageStateAfterSelectionRemoval (
                            Some(RendererPageState.PDFPage "pdf")
                        )
                    )
                    .toBe (true)

                Vitest
                    .expect(
                        FileExplorerStateReconciliation.shouldResetPageStateAfterSelectionRemoval (
                            Some(RendererPageState.PNGImage "png")
                        )
                    )
                    .toBe (true)


                Vitest
                    .expect(
                        FileExplorerStateReconciliation.shouldResetPageStateAfterSelectionRemoval (
                            Some(RendererPageState.JPGImage "jpg")
                        )
                    )
                    .toBe (true)

                Vitest
                    .expect(
                        FileExplorerStateReconciliation.shouldResetPageStateAfterSelectionRemoval (
                            Some(RendererPageState.JPGImage "jpeg")
                        )
                    )
                    .toBe (true)

                Vitest
                    .expect(
                        FileExplorerStateReconciliation.shouldResetPageStateAfterSelectionRemoval (
                            Some(RendererPageState.SVGImage "svg")
                        )
                    )
                    .toBe (true)

                Vitest
                    .expect(
                        FileExplorerStateReconciliation.shouldResetPageStateAfterSelectionRemoval (
                            Some(RendererPageState.TextPage "txt")
                        )
                    )
                    .toBe (true)

                Vitest
                    .expect(
                        FileExplorerStateReconciliation.shouldResetPageStateAfterSelectionRemoval (
                            Some RendererPageState.UnknownPage
                        )
                    )
                    .toBe (true)

                Vitest
                    .expect(
                        FileExplorerStateReconciliation.shouldResetPageStateAfterSelectionRemoval (
                            Some(RendererPageState.LargeFilePage("data/big.txt", 1234.0, NoPreviewReason.TooLarge))
                        )
                    )
                    .toBe (true)

                Vitest
                    .expect(
                        FileExplorerStateReconciliation.shouldResetPageStateAfterSelectionRemoval (
                            Some(RendererPageState.ErrorPage "err")
                        )
                    )
                    .toBe (true)

                Vitest
                    .expect(
                        FileExplorerStateReconciliation.shouldResetPageStateAfterSelectionRemoval (
                            Some RendererPageState.NotesDraftPage
                        )
                    )
                    .toBe (false)

                Vitest
                    .expect(
                        FileExplorerStateReconciliation.shouldResetPageStateAfterSelectionRemoval (
                            Some RendererPageState.ProvenanceGroupingPage
                        )
                    )
                    .toBe (false)

                Vitest
                    .expect(
                        FileExplorerStateReconciliation.shouldResetPageStateAfterSelectionRemoval (
                            Some(RendererPageState.ProvenanceGraphPage "request")
                        )
                    )
                    .toBe (false)

                Vitest
                    .expect(FileExplorerStateReconciliation.shouldResetPageStateAfterSelectionRemoval None)
                    .toBe (false)
        )

        Vitest.test (
            "DataMap tree changes reload a stale open parent preview",
            fun () ->
                let assay = ArcAssay.init "DataMapAssay"
                assay.DataMap <- Some(DataMap.init ())

                let pageState =
                    Some(RendererPageState.ArcFilePage(ArcFiles.Assay assay, Some ActiveView.DataMap))

                let fileTreeWithDataMap = [|
                    FileEntry.create ("isa.datamap.xlsx", "assays/DataMapAssay/isa.datamap.xlsx", false)
                |]

                Vitest.expect(FileExplorerStateReconciliation.tryGetDataMapMismatchReload true pageState).toEqual (None)

                let fileTree = [|
                    FileEntry.create ("DataMapAssay", "assays/DataMapAssay", true)
                    FileEntry.create ("isa.assay.xlsx", "assays/DataMapAssay/isa.assay.xlsx", false)
                |]

                Vitest
                    .expect(FileExplorerStateReconciliation.tryGetDataMapMismatchReload false pageState)
                    .toEqual (Some("assays/DataMapAssay/isa.assay.xlsx", Some ActiveView.Metadata))

                assay.DataMap <- None

                Vitest
                    .expect(FileExplorerStateReconciliation.tryGetDataMapMismatchReload true pageState)
                    .toEqual (Some("assays/DataMapAssay/isa.assay.xlsx", Some ActiveView.DataMap))

                let standaloneDataMapPage =
                    Some(
                        RendererPageState.ArcFilePage(
                            ArcFiles.DataMap(
                                Some(DatamapParentInfo.create "DataMapAssay" DataMapParent.Assay),
                                DataMap.init ()
                            ),
                            Some ActiveView.DataMap
                        )
                    )

                Vitest
                    .expect(FileExplorerStateReconciliation.tryGetDataMapMismatchReload false standaloneDataMapPage)
                    .toEqual (None)
        )

        Vitest.test (
            "fromFileContentDTO maps markdown files to MarkdownPage",
            fun () ->
                let dto: FileContentDTO = {|
                    fileType = FileContentType.Markdown
                    content = "# My Note"
                    path = "notes/my-note.md"
                    noPreview = None
                |}

                let pageState = RendererPageState.fromFileContentDTO dto

                match pageState with
                | RendererPageState.MarkdownPage markdownContent -> Vitest.expect(markdownContent).toBe ("# My Note")
                | _ -> failwith "Expected MarkdownPage for markdown file content DTO."
        )

        Vitest.test (
            "fromFileContentDTO displays pdf files to PDFPage",
            fun () ->
                let dto: FileContentDTO = {|
                    fileType = FileContentType.PDF
                    content = "PDF content"
                    path = "protocols/my-protocol.pdf"
                    noPreview = None
                |}

                let pageState = RendererPageState.fromFileContentDTO dto

                match pageState with
                | RendererPageState.PDFPage pdfContent -> Vitest.expect(pdfContent).toBe ("PDF content")
                | _ -> failwith "Expected PDFPage for PDF file content DTO."
        )

        Vitest.test (
            "fromFileContentDTO displays png files to the appropriate image page state",
            fun () ->
                let dto: FileContentDTO = {|
                    fileType = FileContentType.PNGImage
                    content = "PNG image content"
                    path = "images/my-image.png"
                |}

                let pageState = RendererPageState.fromFileContentDTO dto

                match pageState with
                | RendererPageState.PNGImage pngContent -> Vitest.expect(pngContent).toBe ("PNG image content")
                | _ -> failwith "Expected PNGImage for PNG file content DTO."
        )

        Vitest.test (
            "fromFileContentDTO displays jpg files to the appropriate image page state",
            fun () ->
                let dto: FileContentDTO = {|
                    fileType = FileContentType.JPGImage
                    content = "JPG image content"
                    path = "images/my-image.jpg"
                |}

                let pageState = RendererPageState.fromFileContentDTO dto

                match pageState with
                | RendererPageState.JPGImage jpgContent -> Vitest.expect(jpgContent).toBe ("JPG image content")
                | _ -> failwith "Expected JPGImage for JPG file content DTO."
        )

        Vitest.test (
            "fromFileContentDTO displays svg files to the appropriate image page state",
            fun () ->
                let dto: FileContentDTO = {|
                    fileType = FileContentType.SVGImage
                    content = "SVG content"
                    path = "images/my-image.svg"
                |}

                let pageState = RendererPageState.fromFileContentDTO dto

                match pageState with
                | RendererPageState.SVGImage svgContent -> Vitest.expect(svgContent).toBe ("SVG content")
                | _ -> failwith "Expected SVGImage for SVG file content DTO."
        )



        Vitest.test (
            "fromFileContentDTO opens an ARC workbook without tables on Metadata",
            fun () ->
                let dto =
                    Swate.Electron.Shared.FileIOHelper.FileContentDTO.fromArcFile (
                        ArcFiles.Assay(ArcAssay.init "assay")
                    )
                    |> Option.defaultWith (fun () -> failwith "Expected an assay DTO.")

                match RendererPageState.fromFileContentDTO dto with
                | RendererPageState.ArcFilePage(_, Some ActiveView.Metadata) -> ()
                | _ -> failwith "Expected the Metadata starting view."
        )

        Vitest.test (
            "fromFileContentDTO opens an ARC workbook with tables on its first table",
            fun () ->
                let assay = ArcAssay.init "assay"
                assay.AddTable(ArcTable.init "table")

                let dto =
                    Swate.Electron.Shared.FileIOHelper.FileContentDTO.fromArcFile (ArcFiles.Assay assay)
                    |> Option.defaultWith (fun () -> failwith "Expected an assay DTO.")

                match RendererPageState.fromFileContentDTO dto with
                | RendererPageState.ArcFilePage(_, Some(ActiveView.Table 0)) -> ()
                | _ -> failwith "Expected the first table starting view."
        )

        Vitest.test (
            "fromFileContentDTO opens a DataMap workbook on DataMap",
            fun () ->
                let parent = DatamapParentInfo.create "assay" DataMapParent.Assay

                let dto =
                    Swate.Electron.Shared.FileIOHelper.FileContentDTO.fromArcFile (
                        ArcFiles.DataMap(Some parent, DataMap.init ())
                    )
                    |> Option.defaultWith (fun () -> failwith "Expected a DataMap DTO.")

                match RendererPageState.fromFileContentDTO dto with
                | RendererPageState.ArcFilePage(_, Some ActiveView.DataMap) -> ()
                | _ -> failwith "Expected the DataMap starting view."
        )

        Vitest.test (
            "a redirected DataMap sidebar click opens the owning workbook on DataMap",
            fun () ->
                let assay = ArcAssay.init "assay"
                assay.AddTable(ArcTable.init "table")

                let pageState =
                    RendererPageState.ArcFilePage(ArcFiles.Assay assay, Some(ActiveView.Table 0))
                    |> Renderer.Components.Helper.ArcViewSelection.applyRequestedPathView
                        "assays/assay/isa.datamap.xlsx"

                match pageState with
                | RendererPageState.ArcFilePage(_, Some ActiveView.DataMap) -> ()
                | _ -> failwith "Expected the redirected DataMap click to select the DataMap view."
        )

        Vitest.test (
            "tryGetReloadableSelectedFilePath reloads a no-preview page like an unknown page",
            fun () ->
                let fileTree = [| FileEntry.create ("big.txt", "data/big.txt", false) |]

                for pageState in
                    [
                        RendererPageState.UnknownPage
                        RendererPageState.LargeFilePage("data/big.txt", 1234.0, NoPreviewReason.TooLarge)
                    ] do
                    Vitest
                        .expect(
                            FileExplorerStateReconciliation.tryGetReloadableSelectedFilePath
                                (fun _ -> None)
                                (Some fileTree.[0])
                                (Some pageState)
                        )
                        .toEqual (Some "data/big.txt")
        )

        Vitest.test (
            "fromFileContentDTO maps a file without preview to LargeFilePage",
            fun () ->
                let dto =
                    FileContentDTO.createNoPreview "data/big.txt" 1234.0 NoPreviewReason.TooLarge

                match RendererPageState.fromFileContentDTO dto with
                | RendererPageState.LargeFilePage(path, sizeBytes, reason) ->
                    Vitest.expect(path).toBe "data/big.txt"
                    Vitest.expect(sizeBytes).toBe 1234.0
                    Vitest.expect(reason).toEqual NoPreviewReason.TooLarge
                | _ -> failwith "Expected LargeFilePage for a file without preview."
        )
)

[<ReactComponent>]
let private ControllerProbe (onController: FileStateController -> unit) =
    let ctx = useFileStateCtx ()
    React.useEffect ((fun () -> onController ctx), [| box ctx |])
    Html.none

let private deferred<'T> () : JS.Promise<'T> * ('T -> unit) =
    let resolver = ref (fun (_: 'T) -> ())

    let promise =
        JS.Constructors.Promise.Create(fun resolve _ -> resolver.Value <- resolve)

    promise, (fun value -> resolver.Value value)

let private snapshotWithMarker (treeVersion: int) (marker: string) : FileTreeSnapshotDto =
    let snapshot = createSnapshot treeVersion

    {|
        snapshot with
            entries = Array.append snapshot.entries [| fileEntry marker |]
    |}

type private MountedProvider = {
    Render: FileTreeSource -> FileTreeSnapshotLoader -> unit
    Publish: FileTreeDirectoryUpdateDto -> unit
    Paths: unit -> string[]
    Dispose: unit -> unit
}

let private mountProvider () : MountedProvider =
    let treeBridge = bridgeName "IFileTreeRendererApi"
    let importBridge = bridgeName "IFileImportRendererApi"
    let listener = ref (fun (_: FileTreeDirectoryUpdateDto) -> ())
    let latest = ref (None: FileStateController option)

    setBridgeProperty
        treeBridge
        (createObj [
            "fileTreeDirectoryUpdate"
            ==> fun (callback: FileTreeDirectoryUpdateDto -> unit) ->
                listener.Value <- callback
                fun () -> listener.Value <- ignore
            "fileContentChanged" ==> noopSubscription
            "largeObjectIndexUpdate" ==> noopSubscription
        ])

    setBridgeProperty importBridge (createObj [ "fileImportStateUpdate" ==> noopSubscription ])

    let container = document.createElement ("div") :?> Browser.Types.HTMLDivElement
    document.body.appendChild container |> ignore
    let root = ReactDOM.createRoot container

    {
        Render =
            fun src loader ->
                root.render (
                    FileStateCtxProviderWithSnapshots(
                        src,
                        loader,
                        fileImportApi (fun () -> JS.Constructors.Promise.resolve (Ok None)),
                        ControllerProbe(fun controller -> latest.Value <- Some controller)
                    )
                )
        Publish = fun update -> listener.Value update
        Paths =
            fun () ->
                latest.Value
                |> Option.map (fun controller ->
                    controller.fileTreeStore.Snapshot.Entries
                    |> Seq.map (fun entry -> entry.Value.path)
                    |> Seq.toArray
                    |> Array.sort
                )
                |> Option.defaultValue [||]
        Dispose =
            fun () ->
                root.unmount ()
                container.remove ()
                clearBridgeProperty treeBridge
                clearBridgeProperty importBridge
    }

let private okSnapshot (snapshot: FileTreeSnapshotDto) =
    JS.Constructors.Promise.resolve (Ok snapshot)

Vitest.describe (
    "FileStateContext file tree pull",
    fun () ->
        Vitest.test (
            "pulls on mount, on a root change and on a repeated pathChange of the same root",
            fun () -> promise {
                let mounted = mountProvider ()
                let calls = ref 0

                let loader () =
                    calls.Value <- calls.Value + 1
                    okSnapshot (createSnapshot calls.Value)

                try
                    mounted.Render (source (Some "arc-a") 1) loader
                    do! waitForEffect (fun () -> calls.Value = 1)
                    mounted.Render (source (Some "arc-b") 2) loader
                    do! waitForEffect (fun () -> calls.Value = 2)
                    mounted.Render (source (Some "arc-b") 3) loader
                    do! waitForEffect (fun () -> calls.Value = 3)
                    do! Promise.sleep 20
                    Vitest.expect(calls.Value).toBe 3
                finally
                    mounted.Dispose()
            }
        )

        Vitest.test (
            "drops the result of a pull that belongs to an older root",
            fun () -> promise {
                let mounted = mountProvider ()
                let slowRoot, releaseSlowRoot = deferred<Result<FileTreeSnapshotDto, exn>> ()

                try
                    mounted.Render (source (Some "arc-a") 1) (fun () -> slowRoot)

                    mounted.Render (source (Some "arc-b") 2) (fun () -> okSnapshot (snapshotWithMarker 8 "from-b.txt"))
                    do! waitForEffect (fun () -> mounted.Paths() |> Array.contains "from-b.txt")

                    releaseSlowRoot (Ok(snapshotWithMarker 9 "from-a.txt"))
                    do! Promise.sleep 20

                    Vitest.expect(mounted.Paths() |> Array.contains "from-a.txt").toBe false
                    Vitest.expect(mounted.Paths() |> Array.contains "from-b.txt").toBe true
                finally
                    mounted.Dispose()
            }
        )

        Vitest.test (
            "discards an update that is older than the snapshot and applies a newer one",
            fun () -> promise {
                let mounted = mountProvider ()

                try
                    mounted.Render (source (Some "arc") 1) (fun () -> okSnapshot (createSnapshot 5))
                    do! waitForEffect (fun () -> mounted.Paths().Length > 0)

                    let withoutFile version : FileTreeDirectoryUpdateDto = {|
                        treeVersion = version
                        directoryPath = "assays/assay-1"
                        children = [| dirEntry "assays/assay-1/dataset" |]
                    |}

                    mounted.Publish(withoutFile 4)
                    do! Promise.sleep 10
                    Vitest.expect(mounted.Paths() |> Array.contains "assays/assay-1/isa.assay.xlsx").toBe true

                    mounted.Publish(withoutFile 6)

                    do!
                        waitForEffect (fun () ->
                            not (mounted.Paths() |> Array.contains "assays/assay-1/isa.assay.xlsx")
                        )
                finally
                    mounted.Dispose()
            }
        )

        Vitest.test (
            "applies an update that arrives during the pull after the snapshot",
            fun () -> promise {
                let mounted = mountProvider ()
                let pending, releasePending = deferred<Result<FileTreeSnapshotDto, exn>> ()
                let started = ref false

                try
                    mounted.Render
                        (source (Some "arc") 1)
                        (fun () ->
                            started.Value <- true
                            pending
                        )

                    do! waitForEffect (fun () -> started.Value)

                    mounted.Publish {|
                        treeVersion = 7
                        directoryPath = "assays/assay-1"
                        children = [| dirEntry "assays/assay-1/dataset" |]
                    |}

                    releasePending (Ok(createSnapshot 5))
                    do! waitForEffect (fun () -> mounted.Paths().Length > 0)

                    do!
                        waitForEffect (fun () ->
                            not (mounted.Paths() |> Array.contains "assays/assay-1/isa.assay.xlsx")
                        )
                finally
                    mounted.Dispose()
            }
        )

        Vitest.test (
            "a failed pull leaves the previous root cleared and the next root change recovers",
            fun () -> promise {
                let mounted = mountProvider ()
                let ok () = okSnapshot (createSnapshot 3)

                let failing () =
                    JS.Constructors.Promise.resolve (Error(exn "pull failed"))

                try
                    mounted.Render (source (Some "arc-a") 1) ok
                    do! waitForEffect (fun () -> mounted.Paths().Length > 0)

                    mounted.Render (source (Some "arc-b") 2) failing
                    do! waitForEffect (fun () -> mounted.Paths().Length = 0)

                    mounted.Render (source (Some "arc-c") 3) ok
                    do! waitForEffect (fun () -> mounted.Paths().Length > 0)
                finally
                    mounted.Dispose()
            }
        )

        Vitest.test (
            "closing the ARC empties the store",
            fun () -> promise {
                let mounted = mountProvider ()

                try
                    mounted.Render (source (Some "arc") 1) (fun () -> okSnapshot (createSnapshot 3))
                    do! waitForEffect (fun () -> mounted.Paths().Length > 0)
                    mounted.Render (source None 2) (fun () -> okSnapshot (createSnapshot 4))
                    do! waitForEffect (fun () -> mounted.Paths().Length = 0)
                finally
                    mounted.Dispose()
            }
        )
)
