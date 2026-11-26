namespace Arcitisa.Components.Primitive.Select

open Fable.Core
open Feliz
open Types
open Arcitisa.Components
open Arcitisa.Components.Primitive.Select.Context

module private SingleSelectHelper =

    let singleSelectBehavior: SelectBehavior<int option> = {
        isSelected = fun selected index -> selected = Some index

        select = fun _ index -> Some index

        deselect = fun selected index -> if selected = Some index then None else selected

        selectedIndices =
            function
            | Some index -> Set.singleton index
            | None -> Set.empty
    }

[<Erase; Mangle(false)>]
type SingleSelect =

    [<ReactComponent(true)>]
    static member SingleSelect<'a>
        (
            options: SelectItem<'a>[],
            selectedIndex: int option,
            setSelectedIndex: int option -> unit,
            ?onSelect: int option -> unit,
            ?triggerRenderFn: {| isOpen: bool |} -> ReactElement,
            ?optionRenderFn: SelectItemRender<'a> -> ReactElement,
            ?dropdownPlacement: FloatingUI.Placement,
            ?middleware: FloatingUI.IMiddleware[]
        ) =
        GenericSelect.GenericSelect<'a, int option>(
            options,
            selectedIndex,
            setSelectedIndex,
            SingleSelectHelper.singleSelectBehavior,
            ?onSelect = onSelect,
            ?triggerRenderFn = triggerRenderFn,
            ?optionRenderFn = optionRenderFn,
            ?dropdownPlacement = dropdownPlacement,
            ?middleware = middleware
        )
