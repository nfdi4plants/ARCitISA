module Swate.Components.Composite.DataMapTable.Helper

open ARCtrl
open Browser.Dom
open Fable.Core
open Swate.Components
open Swate.Components.Shared
open Swate.Components.Composite.DataMapTable.Types
open Swate.Components.Composite.DataMapTable.ClipboardTarget
open Swate.Components.Composite.Table.Types

let private captureCells
    (dataMap: Datamap)
    (coordinates: seq<CellCoordinate>)
    : ClipboardContract.Types.ClipboardContent =
    let coordinates =
        coordinates
        |> Seq.filter (fun coordinate -> coordinate.x > 0 && coordinate.y > 0)
        |> Seq.toArray

    let cells =
        Swate.Components.ClipboardContract.Contract.Capture.matrix
            (fun coordinate -> dataMap.GetCell(coordinate.x - 1, coordinate.y - 1))
            coordinates

    {
        PlainText = Swate.Components.ClipboardContract.Contract.Capture.toPlainText cells
        Cells = Some cells
    }

let copyCells (dataMap: Datamap) (coordinates: seq<CellCoordinate>) =
    let content = captureCells dataMap coordinates
    Swate.Components.ClipboardCodec.write content.PlainText content.Cells

let updateDataMap (dataMap: Datamap) (setDataMap: Datamap -> unit) (update: Datamap -> unit) =
    // Always mutate a copy so memoized views compare against the unchanged current value.
    let nextDataMap = DataMapCopyWorkaround.copy dataMap
    update nextDataMap
    setDataMap nextDataMap

let cutCells (dataMap: Datamap) (coordinates: CellCoordinate[]) (setDataMap: Datamap -> unit) =
    Swate.Components.ClipboardContract.Actions.cut
        (captureCells dataMap coordinates)
        (fun () -> updateDataMap dataMap setDataMap _.ClearCells(coordinates))

let pasteFromClipboard
    (dataMap: Datamap)
    (coordinate: CellCoordinate)
    (selection: CellCoordinate[])
    (setDataMap: Datamap -> unit)
    =
    promise {
        let! content = Swate.Components.ClipboardCodec.read ()

        updateDataMap
            dataMap
            setDataMap
            (fun nextDataMap ->
                match content.Cells with
                | Some cells -> nextDataMap.PasteStructuredCells(coordinate, selection, cells)
                | None -> nextDataMap.PasteTabText(coordinate, selection, content.PlainText)
            )
    }
