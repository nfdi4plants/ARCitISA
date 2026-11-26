namespace Arcitisa.Components

open Fable.Core
open Feliz

type CellCoordinate = {| x: int; y: int |}

type CellCoordinateRange = {|
    yStart: int
    yEnd: int
    xStart: int
    xEnd: int
|}

module CellCoordinateRange =

    let count (range: CellCoordinateRange) : int =
        (range.yEnd - range.yStart + 1) * (range.xEnd - range.xStart + 1)

    let toArray (range: CellCoordinateRange) : ResizeArray<CellCoordinate> =
        let result = ResizeArray<CellCoordinate>()

        for y in range.yStart .. range.yEnd do
            for x in range.xStart .. range.xEnd do
                result.Add({| x = x; y = y |})

        result

    let contains (range: CellCoordinateRange option) (cellCoordinate: CellCoordinate) : bool =
        if range.IsSome then
            let coordinates = toArray range.Value
            coordinates.Contains(cellCoordinate)
        else
            false
