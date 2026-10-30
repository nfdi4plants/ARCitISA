namespace Swate.Components.Page.ArcFileEditor

open ARCtrl
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Swate.Components
open Swate.Components.Primitive
open Swate.Components.Primitive.Buttons
open Swate.Components.Primitive.Navbar
open Swate.Components.Composite.Widgets.Context
open Swate.Components.Composite.DataMapTable
open Swate.Components.Composite.Widgets.JsonImport.Types
open Swate.Components.Shared
open Swate.Components.Page.ArcFileEditor.Types
open Swate.Components.Composite.AnnotationTable
open Swate.Components.Composite.Widgets.DataAnnotator.Types
open Swate.Components.Page.ArcFileEditor.EmptyTableView.Types

type private LazyComponents =

    [<ReactLazyComponent>]
    static member LazyDataMap(datamap: Datamap, setDatamap: Datamap -> unit) =
        DataMapTable.DataMapTable(datamap = datamap, setDatamap = setDatamap)

    [<ReactLazyComponent>]
    static member LazyBuildingBlockWidget
        (arcFile: ArcFiles, activeTableIndex: int option, setArcFile: ArcFiles -> unit)
        =
        Swate.Components.Composite.Widgets.BuildingBlockWidget.BuildingBlockWidget.Main(
            arcFile = arcFile,
            activeTableIndex = activeTableIndex,
            setArcFile = setArcFile
        )

    [<ReactLazyComponent>]
    static member LazyTemplateWidget(arcFile: ArcFiles, activeTableIndex: int option, setArcFile: ArcFiles -> unit) =
        Swate.Components.Composite.Widgets.TemplateWidget.TemplateWidget(
            arcFile = arcFile,
            activeTableIndex = activeTableIndex,
            setArcFile = setArcFile
        )

    [<ReactLazyComponent>]
    static member LazyFilePickerWidget
        (
            arcFile: ArcFiles,
            activeTableIndex: int option,
            setArcFile: ArcFiles -> unit,
            onPickPaths: unit -> Fable.Core.JS.Promise<string[]>
        ) =
        Swate.Components.Composite.Widgets.FilePickerWidget.Main(
            arcFile = arcFile,
            activeTableIndex = activeTableIndex,
            setArcFile = setArcFile,
            onPickPaths = onPickPaths
        )

    [<ReactLazyComponent>]
    static member LazyJsonImportWidget
        (
            arcFile: ArcFiles,
            setArcFile: ArcFiles -> unit,
            onImportJson: (JsonImportRequest -> JS.Promise<Result<unit, exn>>) option,
            onError: exn -> unit
        ) =
        Swate.Components.Composite.Widgets.JsonImport.JsonImport.JsonImport(
            arcFile = arcFile,
            setArcFile = setArcFile,
            ?onImportJson = onImportJson,
            onError = onError
        )


    [<ReactLazyComponent>]
    static member LazyJsonExportWidget(arcFile: ArcFiles, onError: exn -> unit) =
        Swate.Components.Composite.Widgets.JsonExport.JsonExport.JsonExport(arcFile = arcFile, onError = onError)

    [<ReactLazyComponent>]
    static member LazyDataAnnotator
        (?onInsert: AnnotationInput -> Result<int, string>, ?canInsert: bool, ?onError: string -> unit)
        =
        Composite.Widgets.DataAnnotator.DataAnnotator.Main(
            ?onInsert = onInsert,
            ?canInsert = canInsert,
            ?onError = onError
        )

    [<ReactLazyComponent>]
    static member LazyArcFileMetadata
        (arcFile: ArcFiles, setArcFile: ArcFiles -> unit, ?onImportPersons: unit -> JS.Promise<Person[]>)
        =
        Swate.Components.Page.Metadata.ArcFileMetadata.ArcFileMetadata(
            arcFile = arcFile,
            setArcFile = setArcFile,
            ?onImportPersons = onImportPersons
        )


[<Erase; Mangle(false)>]
type Main =

    [<ReactComponent>]
    static member private LazyFallback(text: string) =
        Html.div [
            prop.className "swt:flex swt:items-center swt:justify-center"
            prop.children [
                Primitive.LoadingSpinner.LoadingSpinner.LoadingSpinner(text = text)
            ]
        ]

    [<ReactComponent>]
    static member LazyLoaderWithMessage(lazyComponent: ReactElement, message: string) =
        React.Suspense([ lazyComponent ], fallback = Main.LazyFallback(message))

    [<ReactComponent>]
    static member internal DataAnnotatorWidget
        (arcFile: ArcFiles, activeTableIndex: int option, setArcFile: ArcFiles -> unit, onError: string -> unit)
        =
        let annotationCtx = Context.useAnnotationTableStateCtx ()

        let target =
            Composite.Widgets.CellInsertion.tryGetTarget arcFile activeTableIndex annotationCtx.state

        LazyComponents.LazyDataAnnotator(
            onInsert =
                (fun input ->
                    Composite.Widgets.DataAnnotator.Helper.insertAnnotationIntoSelectedCells
                        arcFile
                        setArcFile
                        input
                        target
                ),
            canInsert = target.IsSome,
            onError = onError
        )

    [<ReactComponent>]
    static member private TableView(table: ArcTable, setTableInArcFile: ArcTable -> unit) =

        Html.div [
            prop.className "swt:w-full swt:min-w-0 swt:pb-4"
            prop.children [
                AnnotationTable.AnnotationTable(table, setTableInArcFile)
            ]
        ]

    [<ReactComponent>]
    static member private ArcFileContentView
        (
            activeView: ActiveView,
            arcFileState: ArcFiles,
            setArcFileState: ArcFiles -> unit,
            onImportPersons: (unit -> JS.Promise<Person[]>) option,
            fetchArcNodes: (unit -> JS.Promise<ArcNodeTable[]>) option
        ) =
        match activeView with
        | ActiveView.Metadata ->
            Main.LazyLoaderWithMessage(
                LazyComponents.LazyArcFileMetadata(arcFileState, setArcFileState, ?onImportPersons = onImportPersons),
                "Loading metadata..."
            )
        | ActiveView.Table index ->
            let tables = arcFileState.Tables()

            let activeTable =
                if index < tables.Count && index >= 0 then
                    Some tables.[index]
                else
                    None

            match activeTable with
            | Some table ->
                let setTable (nextTable: ArcTable) =
                    tables.[index] <- nextTable
                    setArcFileState (ArcFiles.refreshRef arcFileState)

                match table.ColumnCount with
                | 0 ->
                    EmptyTableView.Main.EmptyTableView(
                        arcFileState,
                        setArcFileState,
                        Some index,
                        ?fetchArcNodes = fetchArcNodes
                    )
                | _ -> AnnotationTable.AnnotationTable(table, setTable)
            | None ->
                Html.div [
                    prop.className "swt:p-4 swt:text-error"
                    prop.text "Table not found"
                ]
        | ActiveView.DataMap ->
            match arcFileState with
            | ArcFiles.Assay assay when assay.Datamap.IsSome ->
                let setDatamap (nextDatamap: Datamap) =
                    assay.Datamap <- Some nextDatamap
                    setArcFileState (ArcFiles.refreshRef arcFileState)

                Main.LazyLoaderWithMessage(
                    LazyComponents.LazyDataMap(assay.Datamap.Value, setDatamap),
                    "Loading DataMap..."
                )
            | ArcFiles.Study(study, assays) when study.Datamap.IsSome ->
                let setDatamap (nextDatamap: Datamap) =
                    study.Datamap <- Some nextDatamap
                    setArcFileState (ArcFiles.refreshRef (ArcFiles.Study(study, assays)))

                Main.LazyLoaderWithMessage(
                    LazyComponents.LazyDataMap(study.Datamap.Value, setDatamap),
                    "Loading DataMap..."
                )
            | ArcFiles.Run run when run.Datamap.IsSome ->
                let setDatamap (nextDatamap: Datamap) =
                    run.Datamap <- Some nextDatamap
                    setArcFileState (ArcFiles.refreshRef arcFileState)

                Main.LazyLoaderWithMessage(
                    LazyComponents.LazyDataMap(run.Datamap.Value, setDatamap),
                    "Loading DataMap..."
                )
            | ArcFiles.Workflow workflow when workflow.Datamap.IsSome ->
                let setDatamap (nextDatamap: Datamap) =
                    workflow.Datamap <- Some nextDatamap
                    setArcFileState (ArcFiles.refreshRef arcFileState)

                Main.LazyLoaderWithMessage(
                    LazyComponents.LazyDataMap(workflow.Datamap.Value, setDatamap),
                    "Loading DataMap..."
                )
            | ArcFiles.DataMap(parent, datamap) ->
                let setDatamap (nextDatamap: Datamap) =
                    setArcFileState (ArcFiles.DataMap(parent, nextDatamap))

                Main.LazyLoaderWithMessage(LazyComponents.LazyDataMap(datamap, setDatamap), "Loading DataMap...")
            | _ ->
                Html.div [
                    prop.className "swt:p-4 swt:text-error"
                    prop.text "No DataMap available"
                ]

    [<ReactComponent>]
    static member private AddRowsFooter
        (activeView: ActiveView, arcFileState: ArcFiles, setArcFileState: ArcFiles -> unit)
        =
        // Use inputRef to get the number of rows to add. Parsing directly into React.useState does not allow completly removing the number of rows to add.
        let inputRef = React.useInputRef ()

        let addRowsWithCount rowCount =
            match Helper.tryGetAddRowsTarget (activeView, arcFileState) with
            | Some(AddRowsTarget.Table table) ->
                table.AddRowsEmptyKeepingUnits rowCount
                setArcFileState (ArcFiles.refreshRef arcFileState)
            | Some(AddRowsTarget.DataMap dataMap) ->
                dataMap.DataContexts.AddRange(Array.init rowCount (fun _ -> DataContext()))
                setArcFileState (ArcFiles.refreshRef arcFileState)
            | None -> ()

        let addRows =
            fun () ->
                match inputRef.current with
                | Some input ->
                    match System.Int32.TryParse(input.value) with
                    | true, value when value > 0 -> addRowsWithCount value
                    | true, value -> console.error $"Invalid row count {value}"
                    | false, _ -> console.error $"Invalid row input: {input.value}"
                | None -> ()

        let canAddRows =
            match Helper.tryGetAddRowsTarget (activeView, arcFileState) with
            | Some(AddRowsTarget.Table table) -> table.ColumnCount > 0
            | Some(AddRowsTarget.DataMap dataMap) -> dataMap.ColumnCount > 0
            | None -> false

        if not canAddRows then
            Html.none
        else
            Html.div [
                prop.className
                    "swt:w-full swt:flex swt:justify-center swt:items-center swt:shrink-0 swt:p-2 swt:bg-base-200 swt:border-t swt:border-base-300"
                prop.title "Add Rows"
                prop.children [
                    Html.div [
                        prop.className "swt:join"
                        prop.children [
                            Html.input [
                                prop.ref inputRef
                                prop.className "swt:input swt:join-item swt:border-current swt:validator"
                                prop.type'.number
                                prop.min 1
                                prop.required true
                                prop.defaultValue 1
                                prop.onKeyDown (key.enter, fun _ -> addRows ())
                                prop.style [ style.width 100 ]
                                prop.title "Must be a positive number"
                            ]
                            Html.button [
                                prop.className "swt:btn swt:btn-outline swt:join-item"
                                prop.onClick (fun _ -> addRows ())
                                prop.children [ Icons.Plus() ]
                            ]
                        ]
                    ]
                ]
            ]


    /// <summary>Renders an editor for an ARC file.</summary>
    /// <param name="startingActiveView">
    /// The initial active view. This value is read only when the component mounts; to apply a later value,
    /// the caller must remount the editor by changing the React key.
    /// </param>
    /// <param name="fetchArcNodes">
    /// Loads tables from the whole ARC so an empty table can reuse their input and output columns.
    /// Without it, only the other tables of the open file are offered.
    /// </param>
    [<ReactComponent>]
    static member ArcFileEditor
        (
            arcFile: ArcFiles,
            setArcFile: ArcFiles -> unit,
            pickPaths: unit -> Fable.Core.JS.Promise<string[]>,
            ?onAddDataMap: unit -> unit,
            ?onDeleteDataMap: unit -> unit,
            ?trailingNavbarElements: ArcFileEditorHeaderProps -> ReactElement,
            ?startingActiveView: ActiveView,
            ?onImportJson: JsonImportRequest -> JS.Promise<Result<unit, exn>>,
            ?onError: string -> unit,
            ?onImportPersons: unit -> JS.Promise<Person[]>,
            ?fetchArcNodes: unit -> JS.Promise<ArcNodeTable[]>
        ) =

        let onError =
            defaultArg onError (fun errorMsg -> console.error ("Error in ArcFileEditor: " + errorMsg))

        let activeView, setActiveView =
            React.useState (startingActiveView |> Option.defaultValue ActiveView.Metadata)

        // The ARC file is caller-owned and can be replaced independently of this component.
        // Keep the internally owned view valid when the available file views change.
        React.useEffect (
            (fun () ->
                let nextActiveView = ActiveView.Forward(arcFile, activeView)

                if nextActiveView <> activeView then
                    setActiveView nextActiveView
            ),
            [|
                box activeView
                box (arcFile.Tables().Count)
                box (arcFile.CanRenderDataMapView())
                box (arcFile.HasMetadata())
            |]
        )

        let headerProps = {
            arcFile = arcFile
            activeView = activeView
            setActiveView = setActiveView
        }

        let activeTableIndex = activeView.TryTableIndex

        let canAddDataMap =
            onAddDataMap.IsSome
            && arcFile.TryGetDataMapParentInfo().IsSome
            && not (arcFile.CanRenderDataMapView())

        let trailingNavbarElement =
            match trailingNavbarElements with
            | Some renderTrailingNavbarElements -> renderTrailingNavbarElements headerProps
            | None -> Html.none

        let navbar =
            Navbar.Main(
                left = Swate.Components.Page.ArcFileEditor.Widgets.Main.WidgetToggleBtns(),
                right = trailingNavbarElement,
                className = "swt:border swt:border-base-300"
            )


        let widgetElements = {|
            buildingBlock =
                Main.LazyLoaderWithMessage(
                    LazyComponents.LazyBuildingBlockWidget(arcFile, activeTableIndex, setArcFile),
                    "Loading Building Block Widget..."
                )
            template =
                Main.LazyLoaderWithMessage(
                    LazyComponents.LazyTemplateWidget(arcFile, activeTableIndex, setArcFile),
                    "Loading Template Widget..."
                )
            filePicker =
                Main.LazyLoaderWithMessage(
                    LazyComponents.LazyFilePickerWidget(arcFile, activeTableIndex, setArcFile, pickPaths),
                    "Loading File Picker Widget..."
                )
            dataAnnotator =
                match activeView with
                | ActiveView.Table _
                | ActiveView.DataMap ->
                    Main.LazyLoaderWithMessage(
                        Main.DataAnnotatorWidget(arcFile, activeTableIndex, setArcFile, onError),
                        "Loading Data Annotator Widget..."
                    )
                | _ ->
                    Html.div [
                        prop.className "swt:p-3 swt:text-sm swt:opacity-70"
                        prop.text "Open a table or DataMap to use the Data Annotator."
                    ]
            jsonImport =
                Main.LazyLoaderWithMessage(
                    LazyComponents.LazyJsonImportWidget(
                        arcFile,
                        setArcFile,
                        onImportJson,
                        (fun exn -> onError exn.Message)
                    ),
                    "Loading JSON Import Widget..."
                )
            jsonExport =
                Main.LazyLoaderWithMessage(
                    LazyComponents.LazyJsonExportWidget(arcFile, (fun exn -> onError exn.Message)),
                    "Loading JSON Export Widget..."
                )
        |}

        let content =
            Html.div [
                prop.className "swt:grow swt:flex swt:flex-col swt:overflow-hidden"
                prop.children [
                    navbar
                    Html.div [
                        prop.className "swt:grow swt:flex swt:flex-col swt:overflow-hidden"
                        prop.children [
                            Main.ArcFileContentView(activeView, arcFile, setArcFile, onImportPersons, fetchArcNodes)
                        ]
                    ]
                    Main.AddRowsFooter(activeView, arcFile, setArcFile)
                    ArcFileFooterTabs.Main(arcFile, activeView, setActiveView, setArcFile, onDeleteDataMap)
                ]
            ]

        AnnotationTableContextProvider.AnnotationTableContextProvider(
            Swate.Components.Page.ArcFileEditor.Widgets.Main.Widgets(
                content,
                widgetElements.buildingBlock,
                widgetElements.template,
                widgetElements.filePicker,
                widgetElements.dataAnnotator,
                widgetElements.jsonImport,
                widgetElements.jsonExport
            )
        )

    [<ReactComponent(true)>]
    static member Entry(?debug: bool) =

        let startAssay =
            React.useMemo (fun _ ->
                let startAssay = ArcAssay.init ("Test")

                let fullerTable = ArcTable("Fuller Table")

                fullerTable.AddColumn(CompositeHeader.Input IOType.Source)
                fullerTable.AddColumn(CompositeHeader.Output IOType.Sample)
                fullerTable.AddColumn(CompositeHeader.ProtocolREF)
                fullerTable.AddColumn(CompositeHeader.ProtocolDescription)
                fullerTable.AddColumn(CompositeHeader.ProtocolType)
                fullerTable.AddColumn(CompositeHeader.ProtocolUri)
                fullerTable.AddColumn(CompositeHeader.ProtocolVersion)

                fullerTable.AddColumn(
                    CompositeHeader.Component(
                        OntologyAnnotation("Component Name", "Component Accession", "Component Source")
                    )
                )

                fullerTable.AddRowsEmpty 5

                for i in 0..2 do
                    if i = 0 then
                        startAssay.AddTable(fullerTable)
                    else
                        startAssay.AddTable(ArcTable.init (sprintf "Table %i" i))

                startAssay.Datamap <- Some(ARCtrl.Datamap.init ())

                startAssay
            )

        let (arcFile: ArcFiles), setArcFile = React.useState (ArcFiles.Assay(startAssay))

        let loadTemplates =
            fun () ->
                match debug with
                | Some true -> promise {
                    let STORY_TEMPLATE_NAME = "Story Import Template"

                    let createImportTemplate =
                        let template = Template.init (STORY_TEMPLATE_NAME)
                        template.Organisation <- Organisation.DataPLANT
                        template.Table.AddColumn(CompositeHeader.Input(IOType.Source))
                        template.Table.AddColumn(CompositeHeader.Output(IOType.Sample))

                        template.Table.AddColumn(
                            CompositeHeader.Component(
                                OntologyAnnotation("My Awesome component", tsr = "COMASS", tan = "COMASS:12345")
                            )
                        )

                        template.Table.AddRowsEmpty(1)

                        // A unitized parameter lets stories cover the "With Units" import mode.
                        template.Table.AddColumn(
                            CompositeHeader.Parameter(OntologyAnnotation("Temperature", "NCIT", "NCIT:C25206")),
                            ResizeArray [
                                CompositeCell.createUnitized (
                                    "21",
                                    OntologyAnnotation("degree celsius", "UO", "UO:0000027")
                                )
                            ]
                        )

                        template.Version <- "1.0.0"
                        template

                    return Ok([| createImportTemplate |])
                  }
                | _ ->

                    promise {
                        let! json = Api.SwateApi.SwateTemplateApi.getTemplates () |> Async.StartAsPromise
                        return Ok(ARCtrl.Json.Templates.fromJsonString json)
                    }
                    |> Promise.catch (fun error ->
                        // Handle error, e.g., log it or show a notification
                        Error(sprintf "Error loading templates: %s" error.Message)
                    )

        let ColumnCountTestDisplay () =
            let firstTableColumnCount =
                arcFile.TryGetActiveTable(Some 0)
                |> Option.map (fun (_, t) -> string t.ColumnCount)
                |> Option.defaultValue "N/A"

            Html.div [
                prop.className "swt:p-4 swt:text-sm swt:opacity-70"
                prop.testId "arc-file-editor-column-count"
                prop.text ("Column count: " + firstTableColumnCount)
            ]

        let pickPathsMockFn () = promise {
            return [|
                yield "myImage.png"
                yield "README.md"
                for i in 0..40 -> (sprintf "Path %d" i)
            |]
        }

        Swate.Components.Composite.Template.TemplateCacheProvider.TemplateCacheProvider(
            loadTemplates,
            React.Fragment [
                ColumnCountTestDisplay()
                Main.ArcFileEditor(arcFile, setArcFile, pickPathsMockFn, startingActiveView = ActiveView.Table 0)
            ]
        )
