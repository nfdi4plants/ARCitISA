namespace Pages

open System
open Fable.Core
open DataAnnotator
open Model
open Messages
open Feliz
open Arcitisa.Components
open Arcitisa.Components.Composite.Table
open Arcitisa.Components.Page.ArcFileEditor.Types
open Arcitisa.Components.Page.ArcFileEditor.Helper
open Arcitisa.Components.Composite.Table.Types
open Arcitisa.Components.Primitive
open Arcitisa.Components.Primitive.BaseModal
open System
open Components
open Arcitisa.Components.Composite.AnnotationTable.Context
open Arcitisa.Components.Composite.Widgets
open Arcitisa.Components.Composite.Widgets.DataAnnotator.Helper

type DataAnnotator =

    [<ReactComponent>]
    static member Main(model: Model, dispatch: Msg -> unit) =

        let annotationCtx = useAnnotationTableStateCtx ()

        let setArcFile nextArcFile =
            nextArcFile |> Spreadsheet.UpdateArcFile |> SpreadsheetMsg |> dispatch

        match model.SpreadsheetModel.ArcFile with
        | None ->
            Html.div [
                prop.className "swt:p-3 swt:text-sm swt:opacity-70"
                prop.text "Load an ArcFile to use the Data Annotator."
            ]
        | Some arcFile ->
            match model.SpreadsheetModel.ActiveView with
            | ActiveView.Table _
            | ActiveView.DataMap ->
                let tableIndex =
                    match model.SpreadsheetModel.ActiveView with
                    | ActiveView.Table index -> Some index
                    | _ -> None

                let target = CellInsertion.tryGetTarget arcFile tableIndex annotationCtx.state

                Arcitisa.Components.Composite.Widgets.DataAnnotator.DataAnnotator.Main(
                    onInsert = (fun input -> insertAnnotationIntoSelectedCells arcFile setArcFile input target),
                    canInsert = target.IsSome
                )
            | _ ->
                Html.div [
                    prop.className "swt:p-3 swt:text-sm swt:opacity-70"
                    prop.text "Open a table or DataMap to use the Data Annotator."
                ]

    ///// --------------------------------- /////
    // The code below uses has ARCitect specific logic for file upload.
    // To my understanding, this should not be required for a simple upload.
    // I am leaving the commented code here to make recovery easier if we actually need this logic.
    ///// --------------------------------- /////

    // let showModal, setShowModal = React.useState (false)
    // let ref = React.useInputRef ()

    // let uploadFileOnChange =
    //     fun (e: Browser.Types.File) ->
    //         promise {
    //             let! content = e.text ()
    //             let dtf = DataFile.create (e.name, e.``type``, content, e.size)
    //             dtf |> Some |> UpdateDataFile |> DataAnnotatorMsg |> dispatch
    //         }
    //         |> Async.AwaitPromise
    //         |> Async.StartImmediate

    // let rmvFile =
    //     fun _ ->
    //         UpdateDataFile None |> DataAnnotatorMsg |> dispatch

    //         if ref.current.IsSome then
    //             ref.current.Value.value <- null

    // let requestFileFromARCitect =
    //     fun (e: Browser.Types.MouseEvent) ->
    //         e.preventDefault ()

    //         if model.PersistentStorageState.IsARCitect then
    //             Elmish.ApiCall.Start() |> ARCitect.RequestFile |> ARCitectMsg |> dispatch

    // let activateModal = fun _ -> setShowModal true

    // React.Fragment [
    //     ModalMangementContainer [
    //         match model.PersistentStorageState.IsARCitect with
    //         | true ->
    //             DataAnnotatorHelper.DataAnnotatorButtons.RequestPathButton(
    //                 model.DataAnnotatorModel.DataFile |> Option.map _.DataFileName,
    //                 requestFileFromARCitect,
    //                 model.DataAnnotatorModel.Loading
    //             )
    //         | false -> DataAnnotatorHelper.DataAnnotatorButtons.UploadButton ref uploadFileOnChange
    //         Html.div [
    //             prop.className "swt:flex swt:flex-row swt:gap-4"
    //             prop.children [
    //                 DataAnnotatorHelper.DataAnnotatorButtons.ResetButton model rmvFile
    //                 DataAnnotatorHelper.DataAnnotatorButtons.OpenModalButton model activateModal
    //             ]
    //         ]
    //     ]
    //     match model.DataAnnotatorModel, showModal with
    //     | {
    //           DataFile = Some _
    //           ParsedFile = Some _
    //       },
    //       true ->
    //         DataAnnotator.Modal(model, dispatch, rmvFile, (fun _ -> setShowModal false), showModal, setShowModal)
    //     | _, _ -> Html.none
    // ]

    static member Sidebar(model, dispatch) =
        SidebarComponents.SidebarLayout.Container [

            SidebarComponents.SidebarLayout.Header "Data Annotator"

            SidebarComponents.SidebarLayout.Description "Specify exact data points for annotation."

            SidebarComponents.SidebarLayout.LogicContainer [ DataAnnotator.Main(model, dispatch) ]
        ]
