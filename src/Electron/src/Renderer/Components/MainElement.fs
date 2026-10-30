module Renderer.Components.MainElement

open Feliz
open Swate.Components
open Browser.Dom
open ARCtrl
open Renderer.MetadataForms
open WidgetRegistry

[<RequireQualifiedAccess>]
type PreviewActiveView =
    | Metadata
    | Table of int
    | DataMap
    | Error of string option

[<ReactComponent>]
let CreateTablePreview (table: ARCtrl.ArcTable) (setTableInArcFile: ArcTable -> unit) =
    let tableState, setTableState = React.useState (table)

    let setTable (nextTable: ArcTable) =
        setTableState nextTable
        setTableInArcFile nextTable

    React.useEffect (
        (fun () ->
            console.log ("TablePreview received table object")
            setTableState (table)
        ),
        [| box table |]
    )

    Html.div [
        //It works but not as clean as we want it
        prop.className "swt:w-screen swt:pb-4"
        prop.children [ AnnotationTable.AnnotationTable(tableState, setTable) ]
    ]

[<ReactComponent>]
let CreateARCitectNavbar
    (arcFile: ArcFiles)
    (activeView: PreviewActiveView)
    (activeTableIndex: int option)
    (setArcFileState: ArcFiles -> unit)
    (onSaveClick: Browser.Types.MouseEvent -> unit)
    =

    let templateImportType, setTemplateImportType =
        React.useState (TableJoinOptions.Headers)

    let widgetHostView =
        match activeView with
        | PreviewActiveView.Table _ -> WidgetHostView.TableView
        | PreviewActiveView.DataMap -> WidgetHostView.DataMapView
        | PreviewActiveView.Metadata -> WidgetHostView.MetadataView
        | PreviewActiveView.Error _ -> WidgetHostView.PreviewErrorView

    let widgets =
        createWidgets arcFile widgetHostView activeTableIndex setArcFileState templateImportType setTemplateImportType

    let hasSelectedTable = activeTableIndex.IsSome

    Widget.WidgetController(
        widgets,
        closeAllWhen = (not hasSelectedTable),
        children = [
            Components.BaseNavbar.Main [
                NavbarButtons(widgetTypes, hasSelectedTable)
                QuickAccessButton.QuickAccessButton("Save", Icons.Save(), onSaveClick)
            ]
        ]
    )

let private canCreateTables (arcFile: ArcFiles) =
    match arcFile with
    | ArcFiles.Assay _
    | ArcFiles.Study _
    | ArcFiles.Run _ -> true
    | _ -> false

[<ReactComponent>]
let CreateAddRowsFooter (arcFile: ArcFiles) (activeView: PreviewActiveView) (setArcFile: ArcFiles -> unit) =
    let tables = arcFile.Tables()
    let minRowsToAdd = 1
    let rowsToAdd, setRowsToAdd = React.useState minRowsToAdd
    let rowInputRef = React.useInputRef ()

    let clampRowsToAdd rows = max minRowsToAdd rows

    let canAddRows =
        match activeView with
        | PreviewActiveView.Table tableIndex -> tableIndex >= 0 && tableIndex < tables.Count
        | PreviewActiveView.DataMap ->
            match arcFile with
            | ArcFiles.Assay assay -> assay.Datamap.IsSome
            | ArcFiles.Study(study, _) -> study.Datamap.IsSome
            | ArcFiles.Run run -> run.Datamap.IsSome
            | ArcFiles.DataMap _ -> true
            | _ -> false
        | PreviewActiveView.Metadata -> false
        | PreviewActiveView.Error _ -> false

    let addRows () =
        let rowCount = clampRowsToAdd rowsToAdd

        if canAddRows then
            match activeView, arcFile with
            | PreviewActiveView.Table tableIndex, _ when tableIndex >= 0 && tableIndex < tables.Count ->
                tables.[tableIndex].AddRowsEmpty rowCount
                setArcFile (WidgetArcFile.refreshRef arcFile)
            | PreviewActiveView.DataMap, ArcFiles.Assay assay when assay.Datamap.IsSome ->
                assay.Datamap.Value.DataContexts.AddRange(Array.init rowCount (fun _ -> DataContext()))
                setArcFile (WidgetArcFile.refreshRef arcFile)
            | PreviewActiveView.DataMap, ArcFiles.Study(study, _) when study.Datamap.IsSome ->
                study.Datamap.Value.DataContexts.AddRange(Array.init rowCount (fun _ -> DataContext()))
                setArcFile (WidgetArcFile.refreshRef arcFile)
            | PreviewActiveView.DataMap, ArcFiles.Run run when run.Datamap.IsSome ->
                run.Datamap.Value.DataContexts.AddRange(Array.init rowCount (fun _ -> DataContext()))
                setArcFile (WidgetArcFile.refreshRef arcFile)
            | PreviewActiveView.DataMap, ArcFiles.DataMap(_, dataMap) ->
                dataMap.DataContexts.AddRange(Array.init rowCount (fun _ -> DataContext()))
                setArcFile (WidgetArcFile.refreshRef arcFile)
            | _ -> ()

    if canAddRows then
        Html.div [
            prop.className
                "swt:w-full swt:flex swt:justify-center swt:items-center swt:shrink-0 swt:p-2 swt:bg-base-200 swt:border-t swt:border-base-300"
            prop.title "Add Rows"
            prop.children [
                Html.div [
                    prop.className "swt:join"
                    prop.children [
                        Html.input [
                            prop.className "swt:input swt:join-item swt:border-current"
                            prop.type'.number
                            prop.ref rowInputRef
                            prop.min minRowsToAdd
                            prop.defaultValue minRowsToAdd
                            prop.onChange (fun (value: int) -> setRowsToAdd (clampRowsToAdd value))
                            prop.onKeyDown (key.enter, fun _ -> addRows ())
                            prop.style [ style.width 100 ]
                        ]
                        Html.button [
                            prop.className "swt:btn swt:btn-outline swt:join-item"
                            prop.onClick (fun _ ->
                                rowInputRef.current.Value.value <- unbox minRowsToAdd
                                setRowsToAdd minRowsToAdd
                                addRows ()
                            )
                            prop.children [ Icons.Plus() ]
                        ]
                    ]
                ]
            ]
        ]
    else
        Html.none

// let MainMetadata (model: Model, dispatch: Messages.Msg -> unit) =
//     let id = "Metadata-Tab"
//     let nav = Spreadsheet.ActiveView.Metadata
//     let order = nav.ViewIndex

//     Html.div [
//         prop.className [
//             "swt:tab"
//             if model.SpreadsheetModel.ActiveView = nav then
//                 "swt:tab-active"
//         ]
//         prop.key id
//         prop.id id
//         prop.onClick (fun _ -> Spreadsheet.UpdateActiveView nav |> Messages.SpreadsheetMsg |> dispatch)
//         prop.style [
//             style.custom ("order", order)
//             style.height (length.percent 100)
//             style.cursor.pointer
//         ]
//         prop.children [ Icons.InfoCircle([||]); Html.text model.SpreadsheetModel.FileType ]
//     ]

[<ReactComponent>]
let CreateARCitectFooter
    (arcFile: ArcFiles)
    (activeView: PreviewActiveView)
    (setActiveView: PreviewActiveView -> unit)
    (setArcFile: ArcFiles -> unit)
    =
    let tables = arcFile.Tables()
    let canAddTable = canCreateTables arcFile

    let addNewTable () =
        if canAddTable then
            let nextName = createNewTableName tables
            let nextTable = ArcTable.init nextName

            tables.Add(nextTable)
            setArcFile (WidgetArcFile.refreshRef arcFile)
            setActiveView (PreviewActiveView.Table(tables.Count - 1))

    let footerTabBaseClasses = "swt:tab"

    Html.div [
        prop.className "swt:flex swt:gap-0 swt:p-2 swt:bg-base-200 swt:border-t swt:border-base-300 swt:overflow-x-auto"
        prop.children [|
            // Metadata tab
            Html.button [
                prop.className [
                    footerTabBaseClasses
                    if activeView = PreviewActiveView.Metadata then
                        "swt:tab-active"
                    else
                        "swt:btn-ghost"
                ]
                prop.onClick (fun _ -> setActiveView PreviewActiveView.Metadata)
                prop.children [|
                    Html.span [ prop.className "swt:i-fluent--info-24-regular" ]
                    Html.span [ prop.text "Metadata" ]
                |]
            ]
            // Table tabs
            for i = 0 to tables.Count - 1 do
                let table = tables.[i]

                Html.button [
                    prop.key (string i)
                    prop.className [
                        footerTabBaseClasses
                        if activeView = PreviewActiveView.Table i then
                            "swt:btn-primary swt:border-2 swt:!border-white"
                        else
                            "swt:btn-ghost"
                    ]
                    prop.onClick (fun _ -> setActiveView (PreviewActiveView.Table i))
                    prop.children [|
                        Html.span [ prop.className "swt:i-fluent--table-24-regular" ]
                        Html.span [ prop.text table.Name ]
                    |]
                ]
            // DataMap tab
            match arcFile with
            | ArcFiles.Assay a when a.Datamap.IsSome ->
                Html.button [
                    prop.className [
                        footerTabBaseClasses
                        if activeView = PreviewActiveView.DataMap then
                            "swt:btn-primary swt:border-2 swt:!border-white"
                        else
                            "swt:btn-ghost"
                    ]
                    prop.onClick (fun _ -> setActiveView PreviewActiveView.DataMap)
                    prop.children [|
                        Html.span [ prop.className "swt:i-fluent--database-24-regular" ]
                        Html.span [ prop.text "DataMap" ]
                    |]
                ]
            | ArcFiles.Study(s, _) when s.Datamap.IsSome ->
                Html.button [
                    prop.className [
                        footerTabBaseClasses
                        if activeView = PreviewActiveView.DataMap then
                            "swt:btn-primary swt:border-2 swt:!border-white"
                        else
                            "swt:btn-ghost"
                    ]
                    prop.onClick (fun _ -> setActiveView PreviewActiveView.DataMap)
                    prop.children [|
                        Html.span [ prop.className "swt:i-fluent--database-24-regular" ]
                        Html.span [ prop.text "DataMap" ]
                    |]
                ]
            | ArcFiles.Run r when r.Datamap.IsSome ->
                Html.button [
                    prop.className [
                        footerTabBaseClasses
                        if activeView = PreviewActiveView.DataMap then
                            "swt:btn-primary swt:border-2 swt:!border-white"
                        else
                            "swt:btn-ghost"
                    ]
                    prop.onClick (fun _ -> setActiveView PreviewActiveView.DataMap)
                    prop.children [|
                        Html.span [ prop.className "swt:i-fluent--database-24-regular" ]
                        Html.span [ prop.text "DataMap" ]
                    |]
                ]
            | ArcFiles.DataMap _ ->
                Html.button [
                    prop.className [
                        footerTabBaseClasses
                        if activeView = PreviewActiveView.DataMap then
                            "swt:btn-primary swt:border-2 swt:!border-white"
                        else
                            "swt:btn-ghost"
                    ]
                    prop.onClick (fun _ -> setActiveView PreviewActiveView.DataMap)
                    prop.children [|
                        Html.span [ prop.className "swt:i-fluent--database-24-regular" ]
                        Html.span [ prop.text "DataMap" ]
                    |]
                ]
            | _ -> ()
            if canAddTable then
                Html.button [
                    prop.key "new-table-button"
                    prop.title "+"
                    prop.className
                        "swt:btn swt:btn-sm swt:btn-outline swt:items-center swt:border swt:!border-white swt:hover:!border-white swt:rounded-none"
                    prop.onClick (fun _ -> addNewTable ())
                    prop.children [| Html.span [ prop.text "+" ] |]
                ]
        |]
    ]

/// Render metadata view based on ArcFile type using editable form components
[<ReactComponent>]
let CreateMetadataPreview (arcFile: ArcFiles, setArcFile: ArcFiles -> unit) =
    Html.div [
        prop.className "swt:p-4 swt:h-full"
        prop.children [
            match arcFile with
            | ArcFiles.Investigation inv ->
                InvestigationMetadata(inv, fun updated -> setArcFile (ArcFiles.Investigation updated))
            | ArcFiles.Study(study, assays) ->
                StudyMetadata(study, fun updated -> setArcFile (ArcFiles.Study(updated, assays)))
            | ArcFiles.Assay assay -> AssayMetadata(assay, fun updated -> setArcFile (ArcFiles.Assay updated))
            | ArcFiles.Run run -> RunMetadata(run, fun updated -> setArcFile (ArcFiles.Run updated))
            | ArcFiles.Workflow workflow ->
                WorkflowMetadata(workflow, fun updated -> setArcFile (ArcFiles.Workflow updated))
            | ArcFiles.DataMap(_, datamap) -> DataMapMetadata(datamap)
            | ArcFiles.Template template ->
                TemplateMetadata(template, fun updated -> setArcFile (ArcFiles.Template updated))
        ]
    ]

[<ReactComponent>]
let CreateDataMapPreview (datamap: Datamap, setDatamapInArcFile: Datamap -> unit) =
    DataMapTable.DataMapTable(datamap, setDatamapInArcFile)

let CreateTableView activeView arcFileState setArcFileState =
    match activeView with
    | PreviewActiveView.Metadata -> CreateMetadataPreview(arcFileState, setArcFileState)
    | PreviewActiveView.Table index ->
        let tables = arcFileState.Tables()

        if index < tables.Count then
            let setTable (nextTable: ArcTable) =
                tables.[index] <- nextTable
                setArcFileState (WidgetArcFile.refreshRef arcFileState)

            CreateTablePreview tables.[index] setTable
        else
            Html.div [
                prop.className "swt:p-4 swt:text-error"
                prop.text "Table not found"
            ]
    | PreviewActiveView.DataMap ->
        match arcFileState with
        | ArcFiles.Assay assay when assay.Datamap.IsSome ->
            let setDatamap (nextDatamap: Datamap) =
                assay.Datamap <- Some nextDatamap
                setArcFileState (WidgetArcFile.refreshRef arcFileState)

            CreateDataMapPreview(assay.Datamap.Value, setDatamap)
        | ArcFiles.Study(study, assays) when study.Datamap.IsSome ->
            let setDatamap (nextDatamap: Datamap) =
                study.Datamap <- Some nextDatamap
                setArcFileState (ArcFiles.Study(study, assays))

            CreateDataMapPreview(study.Datamap.Value, setDatamap)
        | ArcFiles.Run run when run.Datamap.IsSome ->
            let setDatamap (nextDatamap: Datamap) =
                run.Datamap <- Some nextDatamap
                setArcFileState (WidgetArcFile.refreshRef arcFileState)

            CreateDataMapPreview(run.Datamap.Value, setDatamap)
        | ArcFiles.DataMap(parent, datamap) ->
            let setDatamap (nextDatamap: Datamap) =
                setArcFileState (ArcFiles.DataMap(parent, nextDatamap))

            CreateDataMapPreview(datamap, setDatamap)
        | _ ->
            Html.div [
                prop.className "swt:p-4 swt:text-error"
                prop.text "No DataMap available"
            ]
    | PreviewActiveView.Error msg ->
        Html.div [
            prop.className "swt:p-4 swt:text-error"
            prop.text $"The following error occured: {msg}"
        ]
