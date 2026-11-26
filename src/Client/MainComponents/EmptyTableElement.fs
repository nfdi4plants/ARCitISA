namespace MainComponents

open Feliz
open EmptyTableModals
open Arcitisa.Components
open Arcitisa.Components.Primitive
open Arcitisa.Components.Primitive.CardGrid
open Model
open ARCtrl

module private EmptyTableElementHelpers =
    [<RequireQualifiedAccess>]
    type Modals =
        | BuildingBlock
        | Templates
        | PreviousTableSelect

open EmptyTableElementHelpers

type EmptyTableElement =

    static member private createMinimalTable(model, dispatch) =
        let tables = model.SpreadsheetModel.ArcFile.Value.ArcTables().Tables |> Array.ofSeq

        let activeTableIndex =
            let activeTableName = model.SpreadsheetModel.ActiveTable.Name
            tables |> Array.findIndex (fun table -> table.Name = activeTableName)

        let activeTable = tables.[activeTableIndex]

        let newColumns =
            let inputColumn =
                let header = CompositeHeader.Input IOType.Sample
                CompositeColumn.create (header)

            let parameterColumn =
                let header = CompositeHeader.ProtocolUri
                CompositeColumn.create (header)

            let outPutColumn =
                let header = CompositeHeader.Output IOType.Sample
                CompositeColumn.create (header)

            [| inputColumn; parameterColumn; outPutColumn |]

        activeTable.AddColumns(newColumns)
        activeTable.AddRowsEmpty(3)

        Spreadsheet.UpdateTable(activeTable) |> Messages.SpreadsheetMsg |> dispatch

    [<ReactComponent>]
    static member Main(model: Model, dispatch) =

        let modal, setModal = React.useState (None: Modals option)

        let setIsOpen (modal: Modals) =
            function
            | true -> setModal (Some modal)
            | false -> setModal None

        React.Fragment [
            EmptyTableModals.BuildingBlock(
                model,
                isOpen = (modal = Some Modals.BuildingBlock),
                setIsOpen = setIsOpen Modals.BuildingBlock,
                dispatch = dispatch
            )
            EmptyTableModals.Templates(
                model,
                isOpen = (modal = Some Modals.Templates),
                setIsOpen = setIsOpen Modals.Templates,
                dispatch = dispatch
            )
            EmptyTableModals.PreviousTableSelect(
                model,
                isOpen = (modal = Some Modals.PreviousTableSelect),
                setIsOpen = setIsOpen Modals.PreviousTableSelect,
                dispatch = dispatch
            )
            Html.div [
                prop.className "swt:flex swt:justify-center swt:h-full swt:items-center"
                prop.children [
                    Arcitisa.Components.Primitive.CardGrid.CardGrid.CardGrid(
                        React.Fragment [
                            Arcitisa.Components.Primitive.CardGrid.CardGrid.CardGridButton(
                                Arcitisa.Components.Primitive.Icons.Templates(),
                                "Start with template!",
                                "Select a full template as a starting point.",
                                fun _ -> Modals.Templates |> Some |> setModal
                            )
                            Arcitisa.Components.Primitive.CardGrid.CardGrid.CardGridButton(
                                Arcitisa.Components.Primitive.Icons.BuildingBlock(),
                                "Start from scratch!",
                                "Select a building block as a starting point.",
                                fun _ -> Modals.BuildingBlock |> Some |> setModal
                            )
                            Arcitisa.Components.Primitive.CardGrid.CardGrid.CardGridButton(
                                Arcitisa.Components.Primitive.Icons.BasicTable(),
                                "Create basic table!",
                                "Create a table with columns: Input, Protocol, Output.",
                                fun _ -> EmptyTableElement.createMinimalTable (model, dispatch)
                            )
                            Arcitisa.Components.Primitive.CardGrid.CardGrid.CardGridButton(
                                Arcitisa.Components.Primitive.Icons.OutputColumn(),
                                "Utilize prior output!",
                                "Select an output column of one table as new input column.",
                                (fun _ -> Modals.PreviousTableSelect |> Some |> setModal),
                                (model.SpreadsheetModel.Tables.TableCount <= 1)
                            )
                        ]
                    )
                ]
            ]
        ]
