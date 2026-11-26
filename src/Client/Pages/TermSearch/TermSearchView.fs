module TermSearch


open Messages
open Arcitisa.Components.Composite.AnnotationTable
open Elmish
open Model

let update (termSearchMsg: TermSearch.Msg) (currentState: TermSearch.Model) : TermSearch.Model * Cmd<Messages.Msg> =
    match termSearchMsg with
    // Toggle the search by parent ontology option on/off by clicking on a checkbox
    | TermSearch.UpdateParentTerm oa -> { currentState with ParentTerm = oa }, Cmd.none
    | TermSearch.UpdateSelectedTerm oa -> { currentState with SelectedTerm = oa }, Cmd.none

open Feliz
open ARCtrl
open Fable.Core.JsInterop
open Arcitisa.Components.Composite.AnnotationTable.Context
open Arcitisa.Components.Composite.TermSearch.Types

/// "Fill selected cells with this term" - button //
[<ReactComponent>]
let private AddButton (model: Model, dispatch) =
    let ctx = useAnnotationTableStateCtx ()

    let selectedCells =
        ctx.state
        |> Map.tryFind model.SpreadsheetModel.ActiveTable.Name
        |> Option.bind (fun ctx -> ctx.SelectedCells)
        |> Option.map (fun x -> {|
            xStart = x.xStart - 1
            xEnd = x.xEnd - 1
            yStart = x.yStart - 1
            yEnd = x.yEnd - 1
        |})
        |> unbox<Arcitisa.Components.CellCoordinateRange option>

    Html.div [
        prop.className "swt:flex swt:flex-row swt:justify-center"
        prop.children [
            Html.button [
                let hasTerm = model.TermSearchState.SelectedTerm.IsSome

                prop.className [
                    "swt:btn"
                    if hasTerm then "swt:btn-success" else "swt:btn-error"
                ]

                if not hasTerm then
                    prop.disabled true

                prop.onClick (fun _ ->
                    if hasTerm then
                        let oa = model.TermSearchState.SelectedTerm.Value

                        SpreadsheetInterface.InsertOntologyAnnotation(selectedCells, oa)
                        |> InterfaceMsg
                        |> dispatch
                )

                prop.text "Fill selected cells with this term"
            ]
        ]
    ]

[<ReactComponent>]
let Main (model: Model, dispatch) =
    let setTerm =
        fun (term: Term option) ->
            let term = term |> Option.map OntologyAnnotation.from
            TermSearch.UpdateSelectedTerm term |> TermSearchMsg |> dispatch

    let excelGetParentTerm =
        match model.PersistentStorageState.Host with
        | Some Arcitisahost.Excel ->
            fun _ ->
                promise {
                    let! parent = OfficeInterop.Core.Main.getParentTerm ()
                    TermSearch.UpdateParentTerm parent |> TermSearchMsg |> dispatch
                }
                |> Promise.start
            |> Some
        | _ -> None

    SidebarComponents.SidebarLayout.Container [
        SidebarComponents.SidebarLayout.Header "Ontology term search"

        SidebarComponents.SidebarLayout.Description "Search for an ontology term to fill into the selected field(s)"

        SidebarComponents.SidebarLayout.LogicContainer [
            Arcitisa.Components.Composite.TermSearch.TermSearch.TermSearch(
                (model.TermSearchState.SelectedTerm |> Option.map _.ToTerm()),
                setTerm,
                autoFocus = true,
                ?onFocus = excelGetParentTerm,
                classNames = TermSearchStyle(!^"swt:input-lg swt:w-full"),
                ?parentId = (model.TermSearchState.ParentTerm |> Option.map _.TermAccessionShort)
            )
            AddButton(model, dispatch)
        ]
    ]
