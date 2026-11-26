namespace Arcitisa.Components.Composite.NoteSearch.FilterComponent

open Feliz
open Arcitisa.Components
open Arcitisa.Components.Primitive
open Arcitisa.Components.Primitive.Select
open Arcitisa.Components.Primitive.Select.Types
open Arcitisa.Components.Composite.Notes.Types


module Main =
    let noteSuggestionsList (searchTerm: string, selectedIndices: Set<int>, notes: Note list) =
        let notesFilteredAfterTitle =
            if Set.contains 0 selectedIndices || Set.isEmpty selectedIndices then
                Arcitisa.Components.Composite.NoteSearch.FilterLogic.FuzzySearch.search (searchTerm, "Title", notes)
            else
                []

        let notesFilteredAfterRest =
            Arcitisa.Components.Composite.NoteSearch.FilterLogic.ExactMatchSearch.search (
                searchTerm,
                selectedIndices,
                notes
            )

        notesFilteredAfterTitle @ notesFilteredAfterRest

    [<ReactComponent>]
    let FilterDropdown (filterOptions: string list, selectedIndices: Set<int>, setSelectedIndices: Set<int> -> unit) =

        let filterFields: SelectItem<string>[] =
            React.useMemo (
                (fun () ->
                    filterOptions
                    |> List.map (fun opt -> {| label = opt; item = opt |})
                    |> List.toArray
                ),
                [| filterOptions |]
            )

        let TriggerRenderFn =
            fun _ ->
                Html.button [
                    prop.title "Filter Notes"
                    prop.type'.button
                    prop.tabIndex -1
                    prop.className "swt:btn swt:btn-square swt:btn-neutral swt:shadow-none swt:pointer-events-none"
                    prop.children [
                        Html.div selectedIndices.Count
                        Icons.Filter(className = "swt:size-4")
                    ]
                ]

        MultiSelect.MultiSelect(
            filterFields,
            selectedIndices,
            setSelectedIndices,
            triggerRenderFn = TriggerRenderFn,
            dropdownPlacement = FloatingUI.Placement.BottomStart,
            middleware = [|
                FloatingUI.Middleware.flip ()
                FloatingUI.Middleware.offset (10)
            |]
        )
