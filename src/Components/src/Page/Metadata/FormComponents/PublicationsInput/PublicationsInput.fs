namespace Arcitisa.Components.Page.Metadata.FormComponents.PublicationsInput

open Fable.Core
open Browser.Types
open Feliz
open ARCtrl

open Arcitisa.Components
open Arcitisa.Components.Primitive.LayoutComponents
open Arcitisa.Components.Page.Metadata.FormComponents

module private PublicationsInputHelper =

    let toOptionalString (value: string) = if value = "" then None else Some value

    let countFilledFieldsString (publication: Publication) =
        let fields = [
            publication.PubMedID
            publication.DOI
            publication.Title
            publication.Authors
            publication.Status |> Option.map (fun _ -> "")
        ]

        let total = fields.Length
        let filled = fields |> List.choose id |> List.length
        $"{filled}/{total}"

[<Erase; Mangle(false)>]
type PublicationsInput =

    [<ReactComponent>]
    static member private PublicationInput
        (publication: Publication, setter: Publication -> unit, ?rmv: MouseEvent -> unit)
        =
        let searchRequest, setSearchRequest = React.useState<(bool * string) option> None
        let fieldRevision, setFieldRevision = React.useState 0
        let title = Option.defaultValue "<title>" publication.Title
        let doi = Option.defaultValue "<doi>" publication.DOI

        let createFieldTextInput (field: string option, label: string, publicationSetter: string option -> unit) =
            TextInput.TextInput(
                field |> Option.defaultValue "",
                (fun value ->
                    let nextValue = PublicationsInputHelper.toOptionalString value
                    publicationSetter nextValue
                    setter publication
                ),
                label
            )

        let createIdentifierInput
            (field: string option, label: string, isDoi: bool, publicationSetter: string option -> unit)
            =
            TextInput.TextInput(
                defaultArg field "",
                (fun value ->
                    publicationSetter (PublicationsInputHelper.toOptionalString value)
                    setter publication
                ),
                label,
                trailingAction =
                    (fun value ->
                        Html.button [
                            prop.type'.button
                            prop.className "swt:btn swt:btn-square swt:btn-ghost swt:shrink-0"
                            prop.ariaLabel $"Search by {label}"
                            prop.title $"Search by {label}"
                            prop.onClick (fun _ -> setSearchRequest (Some(isDoi, value)))
                            prop.children [
                                Html.span [
                                    prop.ariaHidden true
                                    prop.className "swt:iconify swt:fluent--search-20-regular swt:size-5"
                                ]
                            ]
                        ]
                    )
            )

        LayoutComponents.Collapse [
            LayoutComponents.CollapseTitle(title, doi, PublicationsInputHelper.countFilledFieldsString publication)
        ] [
            match searchRequest with
            | Some(isDoi, query) ->
                PublicationSearchDialog.PublicationSearchDialog(
                    (if isDoi then query else defaultArg publication.DOI ""),
                    (if isDoi then defaultArg publication.PubMedID "" else query),
                    isDoi,
                    (fun result ->
                        setter (Lookup.mergePublication publication result)
                        // Discard pending manual-input debounce callbacks when replacing metadata.
                        setFieldRevision (fieldRevision + 1)
                    ),
                    (fun () -> setSearchRequest None)
                )
            | None -> ()
            Html.div [
                prop.key (string fieldRevision)
                // Collapse spaces only its direct children, so this wrapper spaces the fields itself.
                prop.className "swt:space-y-4"
                prop.children [
                    createFieldTextInput (publication.Title, "Title", fun value -> publication.Title <- value)
                    Helpers.cardFormGroup [
                        createIdentifierInput (
                            publication.PubMedID,
                            "PubMed ID",
                            false,
                            fun value -> publication.PubMedID <- value
                        )
                        createIdentifierInput (publication.DOI, "DOI", true, fun value -> publication.DOI <- value)
                    ]
                    createFieldTextInput (publication.Authors, "Authors", fun value -> publication.Authors <- value)
                    OntologyAnnotationInput.OntologyAnnotationInput(
                        publication.Status,
                        (fun value ->
                            publication.Status <- value
                            setter publication
                        ),
                        "Status",
                        parent = TermCollection.PublicationStatus
                    )
                    CommentsInput.CommentsInput(
                        publication.Comments,
                        (fun comments ->
                            publication.Comments <- ResizeArray comments
                            setter publication
                        ),
                        "Comments"
                    )
                ]
            ]
            if rmv.IsSome then
                Helpers.deleteButton rmv.Value
        ]

    [<ReactComponent>]
    static member PublicationsInput
        (publications: ResizeArray<Publication>, setter: ResizeArray<Publication> -> unit, label: string)
        =
        InputSequence.InputSequence(
            publications,
            Publication,
            setter,
            (fun (value, setValue, remove) -> PublicationsInput.PublicationInput(value, setValue, rmv = remove)),
            label = label
        )
