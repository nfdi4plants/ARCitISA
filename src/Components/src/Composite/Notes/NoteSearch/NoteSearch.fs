namespace Arcitisa.Components.Composite

open Browser.Dom
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Arcitisa.Components.Composite.Notes.Types
open Arcitisa.Components.Primitive

module InputField =

    [<ReactComponent>]
    let SearchInput
        (
            setSearchTerm,
            setStartSearch,
            filterOptions: list<string>,
            selectedOptIndices: Set<int>,
            setSelectedOptIndices: Set<int> -> unit

        ) =
        Html.div [
            prop.className "swt:w-full swt:mt-4 swt:join"
            prop.children [
                Html.div [
                    prop.className "swt:relative swt:flex-1 swt:join-item"
                    prop.children [
                        Html.input [
                            prop.className "swt:input swt:border-current swt:w-full swt:pr-9 swt:join-item"
                            prop.placeholder "Search Notes..."
                            prop.onClick (fun _ -> setStartSearch true)
                            prop.onChange (fun (ev: Browser.Types.Event) ->
                                let value: string = ev.target?value
                                setSearchTerm value
                                setStartSearch true
                            )
                        ]
                        Html.div [
                            prop.className
                                "swt:absolute swt:right-3 swt:top-1/2 swt:-translate-y-1/2 swt:pointer-events-none"
                            prop.children [ Icons.MagnifyingClass() ]
                        ]
                    ]
                ]
                NoteSearch.FilterComponent.Main.FilterDropdown(filterOptions, selectedOptIndices, setSelectedOptIndices)
            ]
        ]

module helperFnctions =

    let createContentPreview (note: Note) =
        if note.Content.Length > 45 then
            note.Content.Substring(0, 45) + "..."
        else
            note.Content



[<Erase; Mangle(false)>]
type SearchComponent =

    [<ReactComponent>]
    static member SearchSuggestion(note: Note, onOpen: string -> unit) =
        Html.div [
            prop.className "swt:p-3"
            prop.children [
                Html.div [
                    prop.className "swt:flex swt:justify-between swt:items-center swt:gap-2"
                    prop.children [
                        Html.h2 [
                            prop.className "swt:text-lg swt:font-bold"
                            prop.text note.Title
                        ]
                        Html.button [
                            prop.className "swt:btn swt:btn-sm swt:btn-primary"
                            prop.text "Open"
                            prop.onClick (fun _ -> onOpen note.RelativePath)
                        ]
                    ]
                ]
                Html.p [
                    prop.className "swt:text-sm swt:text-gray-600"
                    prop.text (note.Date.ToString("yyyy-MM-dd"))
                ]
                Html.div [
                    prop.className "swt:flex swt:flex-wrap swt:gap-1 swt:mt-1"
                    prop.children [
                        for tag in (note.Tags |> Option.defaultValue (ResizeArray [])) do
                            Html.span [
                                prop.className
                                    "swt:text-sm swt:text-gray-500 swt:border swt:border-current swt:inline-block swt:px-2 swt:py-1 swt:rounded"
                                prop.text tag.NameText
                            ]
                    ]
                ]
                Html.p [
                    prop.className "swt:mt-2"
                    prop.text (helperFnctions.createContentPreview note)
                ]
            ]
        ]

    [<ReactComponent>]
    static member Main(notes: Note list, isLoading: bool, error: string option, onOpen: string -> unit) =
        let startSearch, setStartSearch = React.useState false
        let searchTerm, setSearchTerm = React.useState ""
        let selectedOptIndices, setSelectedOptIndices = React.useState (Set [ 0 ]) // the default is to search in the title, but it can be changed to content and tags as well. If no filter is selected, it will search in all fields.

        let filterOptions = [ "Title"; "Content"; "Tags" ]

        let searchResults =
            match notes with
            | [] -> []
            | _ when searchTerm.Trim() = "" -> notes
            | _ -> NoteSearch.FilterComponent.Main.noteSuggestionsList (searchTerm, selectedOptIndices, notes)


        Html.div [
            prop.className "swt:flex swt:flex-col swt:items-center swt:pt-8 swt:min-h-screen"
            prop.onClick (fun _ -> setStartSearch false)
            prop.children [
                Html.div [
                    prop.className "swt:w-full swt:max-w-md"
                    prop.onClick (fun e -> e.stopPropagation ())
                    prop.children [
                        InputField.SearchInput(
                            setSearchTerm,
                            setStartSearch,
                            filterOptions,
                            selectedOptIndices,
                            setSelectedOptIndices
                        )
                        if startSearch then
                            if isLoading then
                                Html.div [
                                    prop.className "swt:mt-2 swt:text-center"
                                    prop.text "Loading notes..."
                                ]
                            elif error.IsSome then
                                Html.div [
                                    prop.className "swt:mt-2 swt:text-center swt:text-error"
                                    prop.text error.Value
                                ]
                            elif not searchResults.IsEmpty then
                                Html.div [
                                    prop.className
                                        "swt:border-2 swt:border-current swt:rounded-md swt:mt-2 swt:bg-base-100 swt:shadow-md swt:divide-y swt:divide-current"
                                    prop.children [
                                        for note in searchResults do
                                            SearchComponent.SearchSuggestion(note, onOpen)

                                    ]
                                ]
                            elif searchResults.IsEmpty && searchTerm.Trim() <> "" then
                                Html.div [
                                    prop.className "swt:mt-2 swt:text-center"
                                    prop.text "No results found."
                                ]
                        else
                            Html.none
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member Entry() =
        SearchComponent.Main(
            Examples.noteSearchTests.notes,
            false,
            None,
            (fun relativePath -> window.alert $"Open note: {relativePath}")
        )
