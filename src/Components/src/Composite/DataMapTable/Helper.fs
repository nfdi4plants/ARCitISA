module Arcitisa.Components.Composite.DataMapTable.Helper

open ARCtrl
open Browser.Dom
open Fable.Core
open Arcitisa.Components
open Arcitisa.Components.Shared
open Arcitisa.Components.Composite.DataMapTable.Types
open Arcitisa.Components.Composite.DataMapTable.ClipboardTarget
open Arcitisa.Components.Composite.Table.Types

let private captureCells
    (dataMap: Datamap)
    (coordinates: seq<CellCoordinate>)
    : ClipboardContract.Types.ClipboardContent =
    let coordinates =
        coordinates
        |> Seq.filter (fun coordinate -> coordinate.x > 0 && coordinate.y > 0)
        |> Seq.toArray

    let cells =
        Arcitisa.Components.ClipboardContract.Contract.Capture.matrix
            (fun coordinate -> dataMap.GetCell(coordinate.x - 1, coordinate.y - 1))
            coordinates

    {
        PlainText = Arcitisa.Components.ClipboardContract.Contract.Capture.toPlainText cells
        Cells = Some cells
    }

let copyCells (dataMap: Datamap) (coordinates: seq<CellCoordinate>) =
    let content = captureCells dataMap coordinates
    Arcitisa.Components.ClipboardCodec.write content.PlainText content.Cells

let updateDataMap (dataMap: Datamap) (setDataMap: Datamap -> unit) (update: Datamap -> unit) =
    // Always mutate a copy so memoized views compare against the unchanged current value.
    let nextDataMap = DataMapCopyWorkaround.copy dataMap
    update nextDataMap
    setDataMap nextDataMap

let duplicateRows (dataMap: Datamap) (coordinates: seq<CellCoordinate>) (setDataMap: Datamap -> unit) =
    updateDataMap
        dataMap
        setDataMap
        (fun nextDataMap ->
            for rowIndex in Arcitisa.Components.Composite.Table.Helper.selectedRowIndices dataMap.RowCount coordinates do
                let source = dataMap.DataContexts.[rowIndex]
                let duplicate = source.Copy()
                // ARCtrl's copy omits Label and shares mutable ontology annotations and comments.
                duplicate.Label <- source.Label
                duplicate.Explication <- source.Explication |> Option.map _.Copy()
                duplicate.Unit <- source.Unit |> Option.map _.Copy()
                duplicate.ObjectType <- source.ObjectType |> Option.map _.Copy()
                duplicate.Comments <- source.Comments |> Seq.map _.Copy() |> ResizeArray
                nextDataMap.DataContexts.Insert(rowIndex + 1, duplicate)
        )

let cutCells (dataMap: Datamap) (coordinates: CellCoordinate[]) (setDataMap: Datamap -> unit) =
    Arcitisa.Components.ClipboardContract.Actions.cut
        (captureCells dataMap coordinates)
        (fun () -> updateDataMap dataMap setDataMap _.ClearCells(coordinates))

let pasteFromClipboard
    (dataMap: Datamap)
    (coordinate: CellCoordinate)
    (selection: CellCoordinate[])
    (setDataMap: Datamap -> unit)
    =
    promise {
        let! content = Arcitisa.Components.ClipboardCodec.read ()

        updateDataMap
            dataMap
            setDataMap
            (fun nextDataMap ->
                match content.Cells with
                | Some cells -> nextDataMap.PasteStructuredCells(coordinate, selection, cells)
                | None -> nextDataMap.PasteTabText(coordinate, selection, content.PlainText)
            )
    }
