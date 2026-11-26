namespace Arcitisa.Components.Composite.NoteSearch.FilterLogic


open Fable.Core
open Fable.Core.JsInterop
open Arcitisa.Components.Composite.Notes.Types
open Arcitisa.Components.Primitive
open System

//filters the note list based on the search term and the selected filter options.
[<ImportDefault("fuse.js")>]
type Fuse(list: obj[], options: obj) =
    [<Emit "$0.search($1)">]
    member _.search(pattern: string) : obj[] = jsNative

[<Erase>]
type FuzzySearch =
    static member fuseOptions(filterOption: string) =
        createObj [ "keys" ==> [| filterOption |] ] // Default to searching in the title if no filters are selected
    // | _ -> createObj [ "keys" ==> (filterOptions |> List.toArray) ]

    static member fuse(filterOption, notes) =
        Fuse(notes |> List.toArray |> Array.map (fun x -> x :> obj), FuzzySearch.fuseOptions (filterOption))

    static member search(searchPattern: string, filterOption: string, notes) : Note list =
        FuzzySearch.fuse(filterOption, notes).search (searchPattern)
        |> Array.map (fun (result: obj) -> unbox<Note> (result?item))
        |> Array.toList

type ExactMatchSearch =
    static member private containsIgnoreCase(needle: string, haystack: string) =
        if String.IsNullOrEmpty(needle) || String.IsNullOrEmpty(haystack) then
            false
        elif needle.Trim() = "" then
            false

        else
            haystack.ToLowerInvariant().Contains(needle.ToLowerInvariant())

    static member search(searchTerm: string, selectedIndices: Set<int>, notes: Note list) : Note list =
        notes
        |> List.filter (fun note ->

            // let inTitle = ExactMatchSearch.containsIgnoreCase (searchTerm.Trim()) note.Title
            let inContent =
                ExactMatchSearch.containsIgnoreCase (searchTerm.Trim(), note.Content)

            let inTags =
                match note.Tags with
                | Some tags ->
                    tags
                    |> Seq.exists (fun tag -> ExactMatchSearch.containsIgnoreCase (searchTerm.Trim(), tag.NameText))
                | None -> false

            if Set.isEmpty selectedIndices then
                // Default behavior: no selected filter means search in all fields.
                if (searchTerm.Trim()) = "" then
                    true
                else
                    // inTitle ||
                    inContent || inTags
            else
                // let matchesTitle = List.contains "Title" filterOptions && inTitle
                let matchesContent = Set.contains 1 selectedIndices && inContent
                let matchesTags = Set.contains 2 selectedIndices && inTags

                // matchesTitle ||
                matchesContent || matchesTags // If multiple filters are selected, a note matches if it satisfies at least one of the selected criteria.
        )
