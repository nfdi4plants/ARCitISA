module Arcitisa.Components.ClipboardContract.Types

open global.ARCtrl
open Arcitisa.Components

type ClipboardContent = {
    Cells: CompositeCell[][] option
    PlainText: string
}

type MappedCell<'T> = { Source: 'T; Target: CellCoordinate }
