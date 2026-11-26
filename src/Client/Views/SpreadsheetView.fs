module SpreadsheetView

open Feliz
open Messages
open Arcitisa.Components.Shared
open MainComponents
open Fable.Core.JsInterop
open Model
open ARCtrl

let private WidgetOrderContainer bringWidgetToFront (widget) =
    Html.div [
        prop.onClick bringWidgetToFront
        prop.children [ widget ]
    ]

let private ModalDisplay (widgets: Widget list, displayWidget: Widget -> ReactElement) =

    match widgets.Length with
    | 0 -> Html.none
    | _ ->
        Html.div [
            for widget in widgets do
                displayWidget widget
        ]

open Arcitisa.Components.Shared
open FileImport

[<ReactComponent>]
let Main (model: Model, dispatch) =

    let widgets, setWidgets = React.useState ([])

    let rmvWidget (widget: Widget) =
        widgets |> List.except [ widget ] |> setWidgets

    let bringWidgetToFront (widget: Widget) =
        let newList =
            widgets |> List.except [ widget ] |> (fun x -> widget :: x |> List.rev)

        setWidgets newList

    let displayWidget (widget: Widget) =
        let rmv (widget: Widget) = fun _ -> rmvWidget widget
        let bringWidgetToFront = fun _ -> bringWidgetToFront widget

        match widget with
        | Widget._Template -> Widget.Templates(model, dispatch, rmv widget)
        | Widget._FilePicker -> Widget.FilePicker(model, dispatch, rmv widget)
        | Widget._DataAnnotator -> Widget.DataAnnotator(model, dispatch, rmv widget)
        | Widget._BuildingBlock -> Widget.BuildingBlock(model, dispatch, rmv widget)
        |> WidgetOrderContainer bringWidgetToFront

    let state = model.SpreadsheetModel

    Html.div [
        prop.id "MainWindow"
        prop.className "swt:@container/main swt:min-w-100 swt:flex swt:flex-col swt:h-screen"
        prop.children [
            MainComponents.Navbar.Main(model, dispatch, widgets, setWidgets)
            ModalDisplay(widgets, displayWidget)
            Html.div [
                prop.id "TableContainer"
                prop.className "swt:flex swt:grow swt:flex-col swt:h-full swt:overflow-y-hidden"
                prop.children [
                    match state.ArcFile with
                    | None -> MainComponents.NoFileElement.Main(dispatch)
                    | Some(arcfile) ->
                        match model.SpreadsheetModel.ActiveView with
                        | Spreadsheet.ActiveView.Table _ ->
                            match model.SpreadsheetModel.ActiveTable.ColumnCount with
                            | 0 -> MainComponents.EmptyTableElement.Main(model, dispatch)
                            | _ ->
                                MainComponents.SpreadsheetView.ArcTable.Main(model, dispatch)
                                MainComponents.TableFooter.Main dispatch
                        | Spreadsheet.ActiveView.Metadata ->
                            Html.section [
                                prop.className "swt:overflow-y-auto swt:h-full"
                                prop.children [
                                    Html.div [
                                        prop.children [
                                            match arcfile with
                                            | ArcFiles.Assay assay ->
                                                let setAssay assay =
                                                    assay
                                                    |> ArcFiles.Assay
                                                    |> Spreadsheet.UpdateArcFile
                                                    |> SpreadsheetMsg
                                                    |> dispatch

                                                Components.Metadata.Assay.Main(assay, setAssay, model)
                                            | ArcFiles.Study(study, assays) ->
                                                let setStudy (study, assays) =
                                                    (study, assays)
                                                    |> ArcFiles.Study
                                                    |> Spreadsheet.UpdateArcFile
                                                    |> SpreadsheetMsg
                                                    |> dispatch

                                                Components.Metadata.Study.Main(study, assays, setStudy, model)
                                            | ArcFiles.Investigation investigation ->
                                                let setInvesigation investigation =
                                                    investigation
                                                    |> ArcFiles.Investigation
                                                    |> Spreadsheet.UpdateArcFile
                                                    |> SpreadsheetMsg
                                                    |> dispatch

                                                Components.Metadata.Investigation.Main(
                                                    investigation,
                                                    setInvesigation,
                                                    model
                                                )
                                            | ArcFiles.Run run ->
                                                let setRun run =
                                                    run
                                                    |> ArcFiles.Run
                                                    |> Spreadsheet.UpdateArcFile
                                                    |> SpreadsheetMsg
                                                    |> dispatch

                                                Components.Metadata.Run.Main(run, setRun, model)
                                            | ArcFiles.Workflow workflow ->
                                                let setWorkflow workflow =
                                                    workflow
                                                    |> ArcFiles.Workflow
                                                    |> Spreadsheet.UpdateArcFile
                                                    |> SpreadsheetMsg
                                                    |> dispatch

                                                Components.Metadata.Workflow.Main(workflow, setWorkflow, model)
                                            | ArcFiles.Template template ->
                                                let setTemplate template =
                                                    template
                                                    |> ArcFiles.Template
                                                    |> Spreadsheet.UpdateArcFile
                                                    |> SpreadsheetMsg
                                                    |> dispatch

                                                Components.Metadata.Template.Main(template, setTemplate)
                                            | _ -> Html.none
                                        ]
                                    ]
                                ]
                            ]
                        | Spreadsheet.ActiveView.DataMap ->
                            MainComponents.SpreadsheetView.DataMap.Main(model, dispatch)
                            MainComponents.TableFooter.Main dispatch
                ]
            ]
            if state.ArcFile.IsSome then
                MainComponents.FooterTabs.SpreadsheetSelectionFooter model dispatch
        ]
    ]
