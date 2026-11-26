namespace Arcitisa.Components.Page.Metadata.FormComponents.PublicationsInput

open System
open Fable.Core
open Feliz
open Arcitisa.Components
open Arcitisa.Components.Primitive.Dialog
open Arcitisa.Components.Page.Metadata.FormComponents.PublicationsInput.Types

[<Erase; Mangle(false)>]
type PublicationSearchDialog =

    static member private Fields(result: SearchResult) =
        [
            "Title", result.title
            "Authors", result.authors
            "DOI", result.doi
            "PubMed ID", result.pubMedId
            "PMC ID", result.pmcId
            "Journal", result.journal
            "Year", result.year
        ]
        |> List.choose (fun (label, value) -> value |> Option.map (fun text -> label, text))

    [<ReactComponent>]
    static member private Details(result: SearchResult) =
        Html.dl [
            prop.className "swt:grid swt:grid-cols-[auto_1fr] swt:gap-x-3 swt:gap-y-1 swt:text-sm swt:break-words"
            prop.children [
                for label, text in PublicationSearchDialog.Fields result do
                    Html.dt [ prop.className "swt:font-semibold"; prop.text label ]
                    Html.dd [ prop.text text ]
            ]
        ]

    /// A button may only contain phrasing content, so result buttons use spans in place of a description list.
    [<ReactComponent>]
    static member private Summary(result: SearchResult) =
        Html.span [
            prop.className "swt:grid swt:grid-cols-[auto_1fr] swt:gap-x-3 swt:gap-y-1 swt:text-sm swt:break-words"
            prop.children [
                for label, text in PublicationSearchDialog.Fields result do
                    Html.span [ prop.className "swt:font-semibold"; prop.text label ]
                    Html.span [ prop.text text ]
            ]
        ]

    [<ReactComponent>]
    static member PublicationSearchDialog
        (
            initialDoi: string,
            initialPubMedId: string,
            initialIsDoi: bool,
            apply: SearchResult -> unit,
            close: unit -> unit
        ) =
        let isDoi, setIsDoi = React.useState initialIsDoi
        let doiQuery, setDoiQuery = React.useState initialDoi
        let europePmcQuery, setEuropePmcQuery = React.useState initialPubMedId
        let results, setResults = React.useState<SearchResult[]> [||]
        let selected, setSelected = React.useState<SearchResult option> None
        let nextCursor, setNextCursor = React.useState<string option> None
        let error, setError = React.useState<string option> None
        let loading, setLoading = React.useState false
        let searched, setSearched = React.useState false
        let requestVersion = React.useRef 0
        let query = if isDoi then doiQuery else europePmcQuery

        let invalidate () =
            requestVersion.current <- requestVersion.current + 1

        // Requests can finish after dismissal or after the containing entry is removed.
        React.useEffectOnce (fun () -> FsReact.createDisposable invalidate)

        let dismiss () =
            invalidate ()
            close ()

        let reset () =
            invalidate ()
            setResults [||]
            setSelected None
            setNextCursor None
            setError None
            setLoading false
            setSearched false

        let search (cursor: string option) =
            let validation =
                if isDoi then
                    Lookup.normalizeDoi query
                elif String.IsNullOrWhiteSpace query then
                    Error "Enter a title, author, PubMed ID, or PMC ID."
                else
                    Ok(query.Trim())

            match validation with
            | Error message -> setError (Some message)
            | Ok normalizedQuery ->
                invalidate ()
                let version = requestVersion.current
                setLoading true
                setError None

                // Load more keeps the current selection.
                if cursor.IsNone then
                    setSelected None
                    setResults [||]
                    setNextCursor None
                    setSearched false

                promise {
                    try
                        let! page =
                            if isDoi then
                                Lookup.searchDoi normalizedQuery
                            else
                                Lookup.searchEuropePmc normalizedQuery cursor

                        if requestVersion.current = version then
                            let found =
                                if cursor.IsSome then
                                    Array.append results page.results |> Array.distinctBy _.key
                                else
                                    page.results

                            setResults found

                            if found.Length = 1 && page.nextCursor.IsNone then
                                setSelected (Some found.[0])

                            setNextCursor page.nextCursor
                            setSearched true
                            setLoading false
                    with ex ->
                        if requestVersion.current = version then
                            setError (Some $"Publication search failed: {ex.Message}")
                            setLoading false
                }
                |> ignore

        Dialog.Dialog(
            true,
            (fun isOpen ->
                if not isOpen then
                    dismiss ()
            ),
            Html.text "Search publication",
            Html.div [
                prop.className "swt:space-y-4"
                prop.children [
                    Html.div [
                        prop.className "swt:flex swt:gap-2"
                        prop.children [
                            for label, doiMode in [ "DOI", true; "Europe PMC", false ] do
                                Html.button [
                                    prop.type'.button
                                    prop.className (
                                        if isDoi = doiMode then
                                            "swt:btn swt:btn-primary swt:btn-sm"
                                        else
                                            "swt:btn swt:btn-ghost swt:btn-sm"
                                    )
                                    prop.ariaPressed ((isDoi = doiMode))
                                    prop.text label
                                    prop.onClick (fun _ ->
                                        if isDoi <> doiMode then
                                            reset ()
                                            setIsDoi doiMode
                                    )
                                ]
                        ]
                    ]
                    Html.form [
                        prop.onSubmit (fun event ->
                            event.preventDefault ()

                            if not loading then
                                search None
                        )
                        prop.className "swt:flex swt:gap-2"
                        prop.children [
                            Html.input [
                                prop.type'.text
                                prop.ariaLabel (if isDoi then "DOI" else "Europe PMC query")
                                prop.placeholder (
                                    if isDoi then
                                        "10.… or https://doi.org/…"
                                    else
                                        "Title, author, PubMed ID, or PMC ID"
                                )
                                prop.className "swt:input swt:input-bordered swt:w-full"
                                prop.value query
                                prop.onChange (fun (value: string) ->
                                    reset ()
                                    if isDoi then setDoiQuery value else setEuropePmcQuery value
                                )
                            ]
                            Html.button [
                                prop.type'.submit
                                prop.className "swt:btn swt:btn-primary"
                                prop.disabled loading
                                prop.text "Search"
                            ]
                        ]
                    ]
                    if error.IsSome then
                        Html.p [
                            prop.role "alert"
                            prop.className "swt:text-error"
                            prop.text error.Value
                        ]
                    if loading then
                        Html.p [ prop.role "status"; prop.text "Searching publications…" ]
                    if searched && Array.isEmpty results then
                        Html.p [ prop.role "status"; prop.text "No publications found." ]
                    Html.ul [
                        prop.className "swt:space-y-2"
                        prop.children [
                            for result in
                                (if results.Length = 1 && selected.IsSome then
                                     [||]
                                 else
                                     results) do
                                let title = defaultArg result.title "Untitled publication"

                                Html.li [
                                    prop.key result.key
                                    prop.children [
                                        Html.button [
                                            prop.type'.button
                                            prop.ariaLabel $"Preview {title}"
                                            prop.ariaPressed (
                                                selected |> Option.exists (fun current -> current.key = result.key)
                                            )
                                            prop.className
                                                "swt:w-full swt:text-left swt:border swt:border-base-300 swt:rounded swt:p-3 swt:hover:bg-base-200"
                                            prop.onClick (fun _ -> setSelected (Some result))
                                            prop.children [ PublicationSearchDialog.Summary result ]
                                        ]
                                    ]
                                ]
                        ]
                    ]
                    if nextCursor.IsSome then
                        Html.button [
                            prop.type'.button
                            prop.className "swt:btn swt:btn-sm"
                            prop.text "Load more"
                            prop.disabled loading
                            prop.onClick (fun _ -> search nextCursor)
                        ]
                    match selected with
                    | Some result ->
                        Html.section [
                            prop.className "swt:space-y-2 swt:border-t swt:border-base-300 swt:pt-3"
                            prop.children [
                                Html.h3 [
                                    prop.className "swt:font-semibold"
                                    prop.text "Autofill preview"
                                ]
                                PublicationSearchDialog.Details result
                                Html.p [
                                    prop.className "swt:text-sm swt:opacity-70"
                                    prop.text
                                        "Apply replaces returned title, authors, DOI, and PubMed ID. Missing fields, status, and comments are kept."
                                ]
                            ]
                        ]
                    | None -> ()
                ]
            ],
            footer =
                Html.div [
                    prop.className "swt:flex swt:justify-end swt:gap-2 swt:w-full"
                    prop.children [
                        Html.button [
                            prop.type'.button
                            prop.className "swt:btn swt:btn-ghost"
                            prop.text "Cancel"
                            prop.onClick (fun _ -> dismiss ())
                        ]
                        Html.button [
                            prop.type'.button
                            prop.className "swt:btn swt:btn-primary"
                            prop.text "Apply publication"
                            prop.disabled (selected.IsNone || loading)
                            prop.onClick (fun _ ->
                                selected
                                |> Option.iter (fun result ->
                                    invalidate ()
                                    apply result
                                    close ()
                                )
                            )
                        ]
                    ]
                ]
        )
