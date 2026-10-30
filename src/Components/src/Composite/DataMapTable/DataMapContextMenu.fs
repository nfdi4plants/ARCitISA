namespace Swate.Components.Composite.DataMapTable

open ARCtrl
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Swate.Components
open Swate.Components.Primitive
open Swate.Components.Primitive.ContextMenu
open Swate.Components.Primitive.ContextMenu.Types
open Swate.Components.Composite.DataMapTable.Types
open Swate.Components.Composite.Table.Helper
open Swate.Components.Composite.Table.Types

module DataMapTableHelper = Swate.Components.Composite.DataMapTable.Helper
module TableHelper = Swate.Components.Composite.Table.Helper

[<Erase; Mangle(false)>]
type DataMapContextMenu =

    [<ReactComponent>]
    static member ContextMenu
        (
            dataMap: Datamap,
            setDataMap: Datamap -> unit,
            setModal: Modal option -> unit,
            tableRef: IRefValue<TableHandle>,
            containerRef,
            ?debug: bool
        ) =
        ContextMenu.ContextMenu(
            (fun data ->
                let index = data |> unbox<CellCoordinate>

                let selectedCoordinates =
                    if tableRef.current.SelectHandle.contains index then
                        tableRef.current.SelectHandle.getSelectedCells () |> Seq.toArray
                    else
                        [| index |]

                let selectedRowCount = selectedCoordinates |> Array.distinctBy _.y |> Array.length

                [
                    ContextMenuItem(
                        text = Html.div "Details",
                        icon = Icons.MagnifyingGlassPlus(),
                        kbdbutton = ATCMC.KbdHint("D"),
                        onClick = (fun _ -> setModal (Some(Modal.Details index)))
                    )
                    ContextMenuItem(
                        text = Html.div "Fill Column",
                        icon = Icons.Pen(),
                        kbdbutton = ATCMC.KbdHint("F"),
                        onClick =
                            (fun _ ->
                                DataMapTableHelper.updateDataMap
                                    dataMap
                                    setDataMap
                                    (fun nextDataMap ->
                                        TableHelper.fillColumn
                                            nextDataMap.RowCount
                                            index
                                            (fun coordinate ->
                                                nextDataMap.GetCell(coordinate.x - 1, coordinate.y - 1)
                                            )
                                            _.Copy()
                                            (fun coordinate cell ->
                                                nextDataMap.SetCell(coordinate.x - 1, coordinate.y - 1, cell)
                                            )
                                    )
                            )
                    )
                    ContextMenuItem(
                        text = Html.div "Clear Column",
                        icon = Icons.Eraser(),
                        kbdbutton = ATCMC.KbdHint("ClC"),
                        onClick =
                            (fun _ ->
                                DataMapTableHelper.updateDataMap
                                    dataMap
                                    setDataMap
                                    (fun nextDataMap ->
                                        for rowIndex in 0 .. nextDataMap.RowCount - 1 do
                                            nextDataMap.Clear(index.x - 1, rowIndex)
                                    )
                            )
                    )
                    ContextMenuItem(isDivider = true)
                    ContextMenuItem(
                        text = Html.div "Copy",
                        icon = Icons.Copy(),
                        kbdbutton = ATCMC.KbdHint("C"),
                        onClick =
                            (fun _ -> selectedCoordinates |> DataMapTableHelper.copyCells dataMap |> Promise.start)
                    )
                    ContextMenuItem(
                        text = Html.div "Cut",
                        icon = Icons.Scissor(),
                        kbdbutton = ATCMC.KbdHint("X"),
                        onClick =
                            (fun _ ->
                                DataMapTableHelper.cutCells dataMap selectedCoordinates setDataMap
                                |> Promise.start
                            )
                    )
                    ContextMenuItem(
                        text = Html.div "Paste",
                        icon = Icons.Paste(),
                        kbdbutton = ATCMC.KbdHint("V"),
                        onClick =
                            (fun _ ->
                                DataMapTableHelper.pasteFromClipboard dataMap index selectedCoordinates setDataMap
                                |> Promise.start
                            )
                    )
                    ContextMenuItem(
                        text = Html.div "Clear",
                        icon = Icons.Eraser(),
                        kbdbutton = ATCMC.KbdHint("Del"),
                        onClick =
                            (fun _ ->
                                DataMapTableHelper.updateDataMap dataMap setDataMap _.ClearCells(selectedCoordinates)
                            )
                    )
                    ContextMenuItem(isDivider = true)
                    ContextMenuItem(
                        text =
                            Html.div (
                                if selectedRowCount > 1 then
                                    "Delete Selected Rows"
                                else
                                    "Delete Row"
                            ),
                        icon = Icons.DeleteLeft(),
                        kbdbutton = ATCMC.KbdHint("DelR"),
                        onClick =
                            (fun _ ->
                                DataMapTableHelper.updateDataMap
                                    dataMap
                                    setDataMap
                                    (fun nextDataMap ->
                                        selectedCoordinates
                                        |> TableHelper.selectedRowIndices nextDataMap.RowCount
                                        |> Array.iter nextDataMap.DataContexts.RemoveAt
                                    )
                            )
                    )
                ]
            ),
            ref = containerRef,
            onSpawn =
                (fun event ->
                    let target = event.target :?> Browser.Types.HTMLElement

                    match target.closest ("[data-row][data-column]"), containerRef.current with
                    | Some cell, Some container when container.contains (cell) ->
                        let cell = cell :?> Browser.Types.HTMLElement
                        let row = int cell?dataset?row
                        let column = int cell?dataset?column

                        if column > 0 && row > 0 then
                            Some {| x = column; y = row |}
                        else
                            None
                    | _ -> None
                ),
            ?debug = debug
        )
