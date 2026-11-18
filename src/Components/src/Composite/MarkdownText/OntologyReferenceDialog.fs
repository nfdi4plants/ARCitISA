namespace Swate.Components.Composite.MarkdownText

open Fable.Core
open Feliz

open Swate.Components
open Swate.Components.Primitive.BaseModal
open Swate.Components.Composite.TermSearch
open Swate.Components.Composite.TermSearch.Types
open Swate.Components.Composite.MarkdownText.Editing
open Swate.Components.Composite.MarkdownText.Editing.Types

module private OntologyReferenceDialogTypes =

    type Resolution =
        | NotStarted
        | Resolving
        | Resolved of Result<string, string>

open OntologyReferenceDialogTypes

[<Erase; Mangle(false)>]
type OntologyReferenceDialog =

    /// A modal with a term search that writes the chosen term as a reference link on the surface.
    /// lookupIri finds the IRI of a CURIE when a search hit has none (default: the TIB terminology service).
    /// The search uses the app's TermSearchConfigProvider sources plus any termSearchQueries given here.
    [<ReactComponent>]
    static member OntologyReferenceDialog
        (
            isOpen: bool,
            setIsOpen: bool -> unit,
            surface: MarkdownSurface,
            ?lookupIri: string -> JS.Promise<string option>,
            ?termSearchQueries: ResizeArray<string * SearchCall>,
            ?disableDefaultSearch: bool
        ) =
        let lookupIri =
            defaultArg lookupIri (fun (id: string) -> Api.TIBApi.TIBApi.tryGetIRIFromOboId id)

        let term, setTerm = React.useState (None: Term option)
        let chosen, setChosen = React.useState (None: Term option)
        let resolution, setResolution = React.useState NotStarted
        let insertError, setInsertError = React.useState (None: string option)
        // A lookup result counts only while no newer choice was made.
        let requestId = React.useRef 0
        // The term of the running or finished lookup, read synchronously by onTermChange.
        let chosenRef = React.useRef (None: Term option)

        let isFreeText (t: Term) = t.id.IsNone && t.href.IsNone

        let clearChoice () =
            requestId.current <- requestId.current + 1
            chosenRef.current <- None
            setChosen None
            setResolution NotStarted
            setInsertError None

        let choose (t: Term) =
            if isFreeText t then
                clearChoice ()
            else
                requestId.current <- requestId.current + 1
                let current = requestId.current
                chosenRef.current <- Some t
                setTerm (Some t)
                setChosen (Some t)
                setInsertError None
                setResolution Resolving

                OntologyReferences.resolveIri lookupIri t
                |> Promise.iter (fun result ->
                    if requestId.current = current then
                        let checked' =
                            result
                            |> Result.bind (fun iri ->
                                OntologyReferences.check (OntologyReferences.referenceOf t iri)
                                |> Result.map (fun () -> iri)
                            )

                        setResolution (Resolved checked')
                )

        let onTermChange (next: Term option) =
            setTerm next

            match next with
            | Some t when not (isFreeText t) ->
                // With the same id and href the IRI stays valid, so only the shown term changes, for example after
                // an F2 edit of the name.
                match chosenRef.current with
                | Some c when c.id = t.id && c.href = t.href ->
                    chosenRef.current <- Some t
                    setChosen (Some t)
                | _ -> choose t
            | _ -> clearChoice ()

        let close () =
            requestId.current <- requestId.current + 1
            chosenRef.current <- None
            setTerm None
            setChosen None
            setResolution NotStarted
            setInsertError None
            setIsOpen false
            surface.focus ()

        let onOpenChange (open': bool) =
            if open' then setIsOpen true else close ()

        let insert (t: Term) (iri: string) =
            match OntologyReferences.insert (OntologyReferences.referenceOf t iri) (surface.getState ()) with
            | Ok edit ->
                if surface.apply edit then
                    close ()
                else
                    setInsertError (Some "The editor did not accept the change.")
            | Error message -> setInsertError (Some message)

        let insertPlainText (t: Term) =
            let name = t.name |> Option.defaultValue ""
            let curie = t.id |> Option.defaultValue ""

            if System.String.IsNullOrWhiteSpace name && System.String.IsNullOrWhiteSpace curie then
                setInsertError (Some "This term has neither a name nor a CURIE to insert as text.")
            elif surface.apply (OntologyReferences.insertPlainText name curie (surface.getState ())) then
                close ()
            else
                setInsertError (Some "The editor did not accept the change.")

        let details (t: Term) =
            // The dl takes the dt and dd pairs directly, so each element carries its own key.
            let row (label: string) (value: ReactElement) = [
                Html.dt [
                    prop.key (label + "-label")
                    prop.className "swt:font-semibold"
                    prop.text label
                ]
                Html.dd [
                    prop.key (label + "-value")
                    prop.className "swt:min-w-0"
                    prop.children [ value ]
                ]
            ]

            let text (value: string option) =
                Html.text (Option.defaultValue "" value)

            Html.dl [
                prop.className "swt:grid swt:grid-cols-[auto_1fr] swt:gap-x-4 swt:gap-y-1 swt:text-sm"
                prop.children [
                    yield! row "Name" (text t.name)
                    yield! row "CURIE" (text t.id)
                    yield! row "Ontology" (text t.source)
                    let url =
                        match resolution with
                        | Resolving -> Html.text "Looking up the IRI..."
                        | Resolved(Ok iri) ->
                            Html.span [
                                prop.className "swt:font-mono swt:break-all"
                                prop.text iri
                            ]
                        | Resolved(Error reason) ->
                            Html.div [
                                prop.className "swt:alert swt:alert-warning"
                                prop.role.status
                                prop.text reason
                            ]
                        | NotStarted -> Html.none

                    yield! row "URL" url
                ]
            ]

        let body =
            Html.div [
                prop.className "swt:flex swt:flex-col swt:gap-3"
                prop.children [
                    TermSearch.TermSearch(
                        term,
                        onTermChange,
                        onTermSelect = choose,
                        autoFocus = true,
                        ?termSearchQueries = termSearchQueries,
                        ?disableDefaultSearch = disableDefaultSearch
                    )
                    match chosen with
                    | Some t -> details t
                    | None -> Html.none
                    match insertError with
                    | Some message ->
                        Html.div [
                            prop.className "swt:alert swt:alert-error"
                            prop.role.alert
                            prop.text message
                        ]
                    | None -> Html.none
                ]
            ]

        let footer =
            React.Fragment [
                Html.button [
                    prop.key "cancel"
                    prop.className "swt:btn"
                    prop.text "Cancel"
                    prop.onClick (fun _ -> close ())
                ]
                match resolution, chosen with
                | Resolved(Error _), Some t ->
                    Html.button [
                        prop.key "plain"
                        prop.className "swt:btn"
                        prop.text "Insert as plain text"
                        prop.onClick (fun _ -> insertPlainText t)
                    ]
                | _ -> Html.none
                match resolution, chosen with
                | Resolved(Ok iri), Some t ->
                    Html.button [
                        prop.key "insert"
                        prop.className "swt:btn swt:btn-primary swt:ml-auto"
                        prop.text "Insert"
                        prop.onClick (fun _ -> insert t iri)
                    ]
                | _ ->
                    Html.button [
                        prop.key "insert"
                        prop.className "swt:btn swt:btn-primary swt:ml-auto"
                        prop.disabled true
                        prop.children [
                            if resolution = Resolving then
                                Html.span [
                                    prop.className "swt:loading swt:loading-spinner swt:loading-sm"
                                ]
                            Html.text "Insert"
                        ]
                    ]
            ]

        BaseModal.Modal(
            isOpen = isOpen,
            setIsOpen = onOpenChange,
            header = Html.text "Ontology reference",
            children = body,
            footer = footer,
            className = "swt:bg-base-100 swt:text-base-content"
        )
