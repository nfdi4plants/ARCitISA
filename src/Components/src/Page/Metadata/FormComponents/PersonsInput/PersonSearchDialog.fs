namespace Arcitisa.Components.Page.Metadata.FormComponents.PersonsInput

open System
open Fable.Core
open Feliz
open Arcitisa.Components
open Arcitisa.Components.Primitive.Dialog
open Arcitisa.Components.Page.Metadata.FormComponents.PersonsInput.Types

[<Erase; Mangle(false)>]
type PersonSearchDialog =

    static member private Fields(result: PersonResult) =
        // Apply writes only the given and family names, so the credit name gets its own row.
        let appliedName =
            [ result.firstName; result.lastName ]
            |> List.choose id
            |> String.concat " "
            |> Some
            |> Option.filter (fun name -> name <> "")

        [
            "Name", appliedName
            "Credit name", result.displayName |> Option.filter (fun credit -> Some credit <> appliedName)
            "ORCID", Some result.orcid
            "Affiliation", result.affiliation
            "Email", result.email
        ]
        |> List.choose (fun (label, value) -> value |> Option.map (fun text -> label, text))

    [<ReactComponent>]
    static member private Details(result: PersonResult) =
        Html.dl [
            prop.className "swt:grid swt:grid-cols-[auto_1fr] swt:gap-x-3 swt:gap-y-1 swt:text-sm swt:break-words"
            prop.children [
                for label, text in PersonSearchDialog.Fields result do
                    Html.dt [ prop.className "swt:font-semibold"; prop.text label ]
                    Html.dd [ prop.text text ]
            ]
        ]

    /// A button may only contain phrasing content, so result buttons use spans in place of a description list.
    [<ReactComponent>]
    static member private Summary(result: PersonResult) =
        Html.span [
            prop.className "swt:grid swt:grid-cols-[auto_1fr] swt:gap-x-3 swt:gap-y-1 swt:text-sm swt:break-words"
            prop.children [
                for label, text in PersonSearchDialog.Fields result do
                    Html.span [ prop.className "swt:font-semibold"; prop.text label ]
                    Html.span [ prop.text text ]
            ]
        ]

    [<ReactComponent>]
    static member PersonSearchDialog
        (
            initialOrcid: string,
            initialName: string,
            initialMode: SearchMode,
            apply: PersonResult -> unit,
            close: unit -> unit
        ) =
        let mode, setMode = React.useState initialMode
        let orcidQuery, setOrcidQuery = React.useState initialOrcid
        let nameQuery, setNameQuery = React.useState initialName
        let results, setResults = React.useState<PersonResult[]> [||]
        let selected, setSelected = React.useState<PersonResult option> None
        let nextStart, setNextStart = React.useState<int option> None
        let error, setError = React.useState<string option> None
        let blocked, setBlocked = React.useState false
        let loading, setLoading = React.useState false
        let searched, setSearched = React.useState false
        let requestVersion = React.useRef 0
        let query = if mode = SearchMode.Orcid then orcidQuery else nameQuery

        let invalidate () =
            requestVersion.current <- requestVersion.current + 1

        React.useEffectOnce (fun () -> FsReact.createDisposable invalidate)

        let dismiss () =
            invalidate ()
            close ()

        let reset () =
            invalidate ()
            setResults [||]
            setSelected None
            setNextStart None
            setError None
            setBlocked false
            setLoading false
            setSearched false

        let search (start: int option) =
            let validation =
                if mode = SearchMode.Orcid then
                    Lookup.normalizeOrcid query
                elif String.IsNullOrWhiteSpace query then
                    Error "Enter a person's name."
                else
                    Ok(query.Trim())

            match validation with
            | Error message -> setError (Some message)
            | Ok normalized ->
                invalidate ()
                let version = requestVersion.current
                setLoading true
                setError None

                // Load more keeps the current selection.
                if start.IsNone then
                    setSelected None
                    setResults [||]
                    setNextStart None
                    setSearched false

                promise {
                    try
                        let! page =
                            if mode = SearchMode.Orcid then
                                Lookup.getRecord normalized
                            else
                                Lookup.searchByName normalized (defaultArg start 0)

                        if requestVersion.current = version then
                            let found =
                                if start.IsSome then
                                    Array.append results page.results |> Array.distinctBy _.orcid
                                else
                                    page.results

                            setResults found

                            if found.Length = 1 && page.nextStart.IsNone then
                                setSelected (Some found.[0])

                            setNextStart page.nextStart
                            setSearched true
                            setLoading false
                    with
                    | LookupBlocked message when requestVersion.current = version ->
                        setError (Some message)
                        setBlocked true
                        setLoading false
                    | ex when requestVersion.current = version ->
                        setError (Some $"Person search failed: {ex.Message}")
                        setLoading false
                    | _ -> ()
                }
                |> ignore

        Dialog.Dialog(
            true,
            (fun isOpen ->
                if not isOpen then
                    dismiss ()
            ),
            Html.text "Search person",
            Html.div [
                prop.className "swt:space-y-4"
                prop.children [
                    Html.div [
                        prop.className "swt:flex swt:gap-2"
                        prop.children [
                            for label, searchMode in [ "ORCID", SearchMode.Orcid; "Name", SearchMode.Name ] do
                                Html.button [
                                    prop.type'.button
                                    prop.text label
                                    prop.ariaPressed ((mode = searchMode))
                                    prop.className (
                                        if mode = searchMode then
                                            "swt:btn swt:btn-primary swt:btn-sm"
                                        else
                                            "swt:btn swt:btn-ghost swt:btn-sm"
                                    )
                                    prop.onClick (fun _ ->
                                        if mode <> searchMode then
                                            reset ()
                                            setMode searchMode
                                    )
                                ]
                        ]
                    ]
                    Html.form [
                        prop.className "swt:flex swt:gap-2"
                        prop.onSubmit (fun event ->
                            event.preventDefault ()

                            if not loading && not blocked then
                                search None
                        )
                        prop.children [
                            Html.input [
                                prop.type'.text
                                prop.ariaLabel (
                                    if mode = SearchMode.Orcid then
                                        "ORCID iD or URL"
                                    else
                                        "Person name"
                                )
                                prop.placeholder (
                                    if mode = SearchMode.Orcid then
                                        "0000-0000-0000-0000 or https://orcid.org/…"
                                    else
                                        "First and last name"
                                )
                                prop.className "swt:input swt:input-bordered swt:w-full swt:min-w-0"
                                prop.value query
                                prop.onChange (fun (value: string) ->
                                    reset ()

                                    if mode = SearchMode.Orcid then
                                        setOrcidQuery value
                                    else
                                        setNameQuery value
                                )
                            ]
                            Html.button [
                                prop.type'.submit
                                prop.className "swt:btn swt:btn-primary"
                                prop.disabled (loading || blocked)
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
                        Html.p [ prop.role "status"; prop.text "Searching people…" ]
                    if searched && results.Length = 0 then
                        Html.p [ prop.role "status"; prop.text "No people found." ]
                    if results.Length <> 1 || selected.IsNone then
                        Html.ul [
                            prop.className "swt:space-y-2"
                            prop.children [
                                for result in results do
                                    Html.li [
                                        prop.key result.orcid
                                        prop.children [
                                            Html.button [
                                                prop.type'.button
                                                prop.ariaLabel $"Preview {Lookup.fullName result}"
                                                prop.ariaPressed (
                                                    selected
                                                    |> Option.exists (fun current -> current.orcid = result.orcid)
                                                )
                                                prop.className
                                                    "swt:w-full swt:text-left swt:border swt:border-base-300 swt:rounded swt:p-3 swt:hover:bg-base-200"
                                                prop.onClick (fun _ -> setSelected (Some result))
                                                prop.children [ PersonSearchDialog.Summary result ]
                                            ]
                                        ]
                                    ]
                            ]
                        ]
                    if nextStart.IsSome then
                        Html.button [
                            prop.type'.button
                            prop.className "swt:btn swt:btn-sm"
                            prop.disabled (loading || blocked)
                            prop.text "Load more"
                            prop.onClick (fun _ -> search nextStart)
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
                                PersonSearchDialog.Details result
                                Html.p [
                                    prop.className "swt:text-sm swt:opacity-70"
                                    prop.text
                                        "Apply replaces returned names, ORCID, email, and affiliation. Missing fields, roles, comments, and other contact details are kept."
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
                            prop.text "Apply person"
                            prop.disabled (selected.IsNone || loading || blocked)
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
