namespace MainComponents

open Feliz
open Types.FileImport

open ARCtrl

open Model
open Arcitisa.Components
open Arcitisa.Components.Primitive.BaseModal
open Fable.Core.JsInterop
open BuildingBlock.SearchComponent

module EmptyTableModals =

    type EmptyTableModals =


        [<ReactComponent>]
        static member BuildingBlock(model, isOpen, setIsOpen, dispatch) =
            let content = SearchComponent.Main(model, dispatch)
            BaseModal.Modal(isOpen, setIsOpen, Html.text "Select a building block", content)

        [<ReactComponent>]
        static member Templates(model: Model, isOpen, setIsOpen, dispatch) =

            let content = Protocol.Templates.TemplateSelect(model, dispatch)

            BaseModal.Modal(isOpen, setIsOpen, Html.text "Select template(s)", content)

        [<ReactComponent>]
        static member PreviousTableSelect(model: Model, isOpen, setIsOpen, dispatch) =

            let tables = model.SpreadsheetModel.ArcFile.Value.ArcTables().Tables |> Array.ofSeq

            let activeTableIndex =
                let activeTableName = model.SpreadsheetModel.ActiveTable.Name
                tables |> Array.findIndex (fun table -> table.Name = activeTableName)
            //Get all tables with a output column because the others cannot be used as input for a new table
            let relevantTables =
                if model.SpreadsheetModel.ArcFile.IsSome then
                    tables
                    |> Array.ofSeq
                    |> Array.filter (fun table -> table.TryGetOutputColumn().IsSome)
                else
                    [||]

            let (selectedTable: ArcTable option), setSelectedTable =
                React.useState (
                    if relevantTables.Length > 0 then
                        Some relevantTables.[0]
                    else
                        None
                )

            //Create input column based on output column of selected table
            let newInputColumn =
                if selectedTable.IsSome then
                    let table = selectedTable.Value
                    let outputColumn = table.GetOutputColumn()
                    let headerType = outputColumn.Header.TryIOType()
                    let newInputHeader = CompositeHeader.Input headerType.Value
                    Some(CompositeColumn.create (newInputHeader, outputColumn.Cells))
                else
                    None

            let previewTable =
                if newInputColumn.IsSome then
                    Some(
                        Html.div [
                            prop.className "swt:overflow-auto swt:max-h-64"
                            prop.children [
                                Html.table [
                                    prop.className "swt:table swt:table-sm"
                                    prop.children [
                                        Html.caption [
                                            prop.className
                                                "swt:text-lg swt:font-semibold swt:pb-2 swt:text-left swt:align-left"
                                            prop.text "Preview: "
                                        ]
                                        Html.thead [
                                            Html.tr [
                                                Html.th [
                                                    prop.className "swt:truncate swt:align-left"
                                                    prop.text (newInputColumn.Value.Header.ToString())
                                                    prop.title (newInputColumn.Value.Header.ToString())
                                                ]
                                            ]
                                        ]
                                        Html.tbody [
                                            if newInputColumn.Value.Cells.Count > 10 then
                                                for i in 0..9 do
                                                    let cell = newInputColumn.Value.Cells.[i]

                                                    Html.tr [
                                                        Html.td [
                                                            prop.className "swt:truncate swt:align-left"
                                                            prop.text (cell.ToString())
                                                            prop.title (cell.ToString())
                                                        ]
                                                    ]
                                            else
                                                for cell in newInputColumn.Value.Cells do
                                                    Html.tr [
                                                        Html.td [
                                                            prop.className "swt:truncate swt:align-left"
                                                            prop.text (cell.ToString())
                                                            prop.title (cell.ToString())
                                                        ]
                                                    ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    )
                else
                    None

            let content =
                Html.div [
                    prop.className "swt:flex swt:flex-col swt:gap-2"
                    prop.children [
                        Html.div [
                            prop.className "swt:join swt:mt-2"
                            prop.children [
                                Html.span [
                                    prop.className
                                        "swt:join-item swt:btn swt:btn-ghost swt:shadow-none swt:pointer-events-none"
                                    prop.text "Tables:"
                                ]
                                Html.select [
                                    prop.className "swt:select swt:join-item swt:min-w-fit"
                                    prop.onChange (fun (e: Browser.Types.Event) ->
                                        let tableName = e.target?value
                                        let table = relevantTables |> Array.find (fun table -> table.Name = tableName)
                                        Some table |> setSelectedTable
                                    )
                                    prop.children (
                                        relevantTables
                                        |> Array.map (fun table ->
                                            Html.option [ prop.value table.Name; prop.text table.Name ]
                                        )
                                    )
                                ]
                            ]
                        ]

                        if previewTable.IsSome then
                            previewTable.Value

                        Html.button [
                            prop.className "swt:btn swt:w-full swt:btn-square swt:btn-primary"
                            prop.text "Import selected output column"
                            prop.disabled newInputColumn.IsNone
                            prop.onClick (fun _ ->
                                let newTable = ArcTable.init (model.SpreadsheetModel.ActiveTable.Name)
                                newTable.AddColumn(newInputColumn.Value.Header, newInputColumn.Value.Cells)
                                Spreadsheet.RemoveTable(activeTableIndex) |> Messages.SpreadsheetMsg |> dispatch

                                Spreadsheet.AddTable(newTable) |> Messages.SpreadsheetMsg |> dispatch
                                setIsOpen false
                            )
                        ]
                    ]
                ]

            BaseModal.Modal(isOpen, setIsOpen, Html.text "Select table for source", content)
