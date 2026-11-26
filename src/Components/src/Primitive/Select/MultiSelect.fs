namespace Arcitisa.Components.Primitive.Select

open Fable.Core
open Feliz
open Types
open Arcitisa.Components
open Arcitisa.Components.Primitive.Select.Context

module private MultiSelectHelper =

    let multiSelectBehavior: SelectBehavior<Set<int>> = {
        isSelected = fun selected index -> selected.Contains index

        select = fun selected index -> selected.Add index

        deselect = fun selected index -> selected.Remove index

        selectedIndices = id
    }

[<Erase; Mangle(false)>]
type MultiSelect =

    [<ReactComponent>]
    static member private SelectAll(setSelectIndices: Set<int> -> unit, key: string) =
        let selectContext = useSelectCtx ()
        let listItem = FloatingUI.useListItem ()
        let allIndices: Set<int> = Set(List.init selectContext.optionCount id)

        let isActive = selectContext.activeIndex = Some listItem.index
        let isSelected = selectContext.selectedIndices = allIndices

        let checkboxRef = React.useInputRef ()

        React.useEffect (
            (fun () ->
                if selectContext.selectedIndices.IsEmpty then
                    checkboxRef.current |> Option.iter (fun x -> x.indeterminate <- false)
                elif not isSelected && selectContext.selectedIndices.IsSubsetOf allIndices then
                    checkboxRef.current |> Option.iter (fun x -> x.indeterminate <- true)
                else
                    checkboxRef.current |> Option.iter (fun x -> x.indeterminate <- false)
            ),
            [| box selectContext.selectedIndices |]
        )

        let toggleSelect =
            fun (_) ->
                if isSelected then
                    setSelectIndices (Set.empty)
                else
                    setSelectIndices (allIndices)

        GenericSelect.OuterBaseOptionRender(
            isActive,
            isSelected,
            key,
            listItem,
            selectContext,
            toggleSelect,
            GenericSelect.InnerBaseOptionRender("Select all", isSelected, ref = checkboxRef)
        )

    [<ReactComponent(true)>]
    static member MultiSelect<'a>
        (
            options: SelectItem<'a>[],
            selectedIndices: Set<int>,
            setSelectedIndices: Set<int> -> unit,
            ?onSelect: int option -> unit,
            ?triggerRenderFn: {| isOpen: bool |} -> ReactElement,
            ?optionRenderFn: SelectItemRender<'a> -> ReactElement,
            ?dropdownPlacement: FloatingUI.Placement,
            ?middleware: FloatingUI.IMiddleware[],
            ?showSelectAll: bool
        ) =
        let leadingItem =
            match showSelectAll with
            | None
            | Some true -> Some(MultiSelect.SelectAll(setSelectedIndices, "multi-select-select-all"))
            | Some false -> None

        GenericSelect.GenericSelect<'a, Set<int>>(
            options,
            selectedIndices,
            setSelectedIndices,
            MultiSelectHelper.multiSelectBehavior,
            ?onSelect = onSelect,
            ?triggerRenderFn = triggerRenderFn,
            ?optionRenderFn = optionRenderFn,
            ?dropdownPlacement = dropdownPlacement,
            ?middleware = middleware,
            ?leadingListItem = leadingItem
        )
