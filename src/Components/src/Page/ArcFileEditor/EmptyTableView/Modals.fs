namespace Swate.Components.Page.ArcFileEditor.EmptyTableView

open Feliz
open Fable.Core
open ARCtrl
open Swate.Components
open Swate.Components.Shared
open Swate.Components.Primitive.BaseModal

[<Erase; Mangle(false)>]
type Modals =

    [<ReactComponent>]
    static member PreviousTableSelect
        (
            arcFile: ArcFiles,
            activeTableIndex: int option,
            setArcFile: ArcFiles -> unit,
            isOpen: bool,
            setIsOpen: bool -> unit
        ) =

        let relevantTables = Helper.getOutputTables arcFile

        let (selectedTable: ArcTable option), setSelectedTable =
            React.useState (
                if relevantTables.Length > 0 then
                    Some relevantTables.[0]
                else
                    None
            )

        let previewColumn = selectedTable |> Option.bind Helper.tryCreatePreviewColumn

        let canImport =
            activeTableIndex.IsSome && selectedTable.IsSome && previewColumn.IsSome

        BaseModal.Modal(
            isOpen = isOpen,
            setIsOpen = setIsOpen,
            header = Html.text "Select table for source",
            children =
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
                                    prop.valueOrDefault (
                                        selectedTable |> Option.map (fun table -> table.Name) |> Option.defaultValue ""
                                    )
                                    prop.onChange (fun (tableName: string) ->
                                        let table =
                                            relevantTables |> Array.find (fun candidate -> candidate.Name = tableName)

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

                        match previewColumn with
                        | Some previewColumn ->
                            Html.div [
                                prop.className "swt:overflow-auto swt:max-h-64"
                                prop.children [
                                    Html.table [
                                        prop.className "swt:table swt:table-sm"
                                        prop.children [
                                            Html.caption [
                                                prop.className
                                                    "swt:text-lg swt:font-semibold swt:pb-2 swt:text-left swt:align-left"
                                                prop.text "Preview:"
                                            ]
                                            Html.thead [
                                                Html.tr [
                                                    Html.th [
                                                        prop.className "swt:truncate swt:align-left"
                                                        prop.text (previewColumn.Header.ToString())
                                                        prop.title (previewColumn.Header.ToString())
                                                    ]
                                                ]
                                            ]
                                            Html.tbody [
                                                let previewCells = Helper.previewCells previewColumn.Cells

                                                if previewCells.Length = 0 then
                                                    Html.tr [
                                                        Html.td [
                                                            prop.className "swt:opacity-70"
                                                            prop.text "No cells to preview."
                                                        ]
                                                    ]
                                                else
                                                    for cellText in previewCells do
                                                        Html.tr [
                                                            Html.td [
                                                                prop.className "swt:truncate swt:align-left"
                                                                prop.text cellText
                                                                prop.title cellText
                                                            ]
                                                        ]
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        | None ->
                            Html.div [
                                prop.className "swt:text-sm swt:text-error"
                                prop.text "The selected table does not expose a usable output column."
                            ]

                        Html.button [
                            prop.className "swt:btn swt:w-full swt:btn-primary"
                            prop.text "Import selected output column"
                            prop.disabled (not canImport)
                            prop.onClick (fun _ ->
                                if
                                    Helper.importSelectedPreviousOutput
                                        arcFile
                                        activeTableIndex
                                        selectedTable
                                        setArcFile
                                then
                                    setIsOpen false
                            )
                        ]
                    ]
                ],
            className = "swt:max-w-3xl"
        )
