module Arcitisa.Components.Primitive.Select.Types

open Fable.Core
open Feliz

type SelectBehavior<'selection> = {
    isSelected: 'selection -> int -> bool
    select: 'selection -> int -> 'selection
    deselect: 'selection -> int -> 'selection
    selectedIndices: 'selection -> Set<int>
}

type SelectItem<'a> = {| item: 'a; label: string |}

type SelectItemRender<'a> = {|
    isActive: bool
    isSelected: bool
    item: SelectItem<'a>
|}
