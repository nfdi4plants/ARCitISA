namespace Arcitisa.Components.Page.Metadata.FormComponents

open Fable.Core
open System
open Browser.Types
open Feliz
open ARCtrl

open Arcitisa.Components
open Arcitisa.Components.Primitive.LayoutComponents

module private OntologySourceReferencesInputHelper =

    let toOptionalString (value: string) =
        if String.IsNullOrWhiteSpace value then None else Some value

    let countFilledFieldsString (input: OntologySourceReference) =
        let fields = [
            input.Name
            input.File
            input.Version
            input.Description
            if input.Comments.Count > 0 then Some "comments" else None
        ]

        let total = fields.Length
        let filled = fields |> List.choose id |> List.length
        $"{filled}/{total}"

[<Erase; Mangle(false)>]
type OntologySourceReferencesInput =

    [<ReactComponent>]
    static member private OntologySourceReferenceInput
        (input: OntologySourceReference, setter: OntologySourceReference -> unit, ?deleteButton: MouseEvent -> unit)
        =
        let name = Option.defaultValue "<name>" input.Name
        let version = Option.defaultValue "<version>" input.Version

        let createFieldTextInput (field: string option, label: string, setField: string option -> unit) =
            TextInput.TextInput(
                field |> Option.defaultValue "",
                (fun value ->
                    setField (OntologySourceReferencesInputHelper.toOptionalString value)
                    setter input
                ),
                label
            )

        LayoutComponents.Collapse [
            LayoutComponents.CollapseTitle(
                name,
                version,
                OntologySourceReferencesInputHelper.countFilledFieldsString input
            )
        ] [
            createFieldTextInput (input.Name, "Name", fun value -> input.Name <- value)
            Helpers.cardFormGroup [
                createFieldTextInput (input.Version, "Version", fun value -> input.Version <- value)
                createFieldTextInput (input.File, "File", fun value -> input.File <- value)
            ]
            TextInput.TextInput(
                Option.defaultValue "" input.Description,
                (fun value ->
                    input.Description <- OntologySourceReferencesInputHelper.toOptionalString value
                    setter input
                ),
                "Description",
                isArea = true
            )
            CommentsInput.CommentsInput(
                input.Comments,
                (fun comments ->
                    input.Comments <- comments
                    setter input
                ),
                "Comments"
            )
            if deleteButton.IsSome then
                Helpers.deleteButton deleteButton.Value
        ]

    [<ReactComponent>]
    static member OntologySourceReferencesInput
        (
            input: ResizeArray<OntologySourceReference>,
            setter: ResizeArray<OntologySourceReference> -> unit,
            label: string
        ) =
        InputSequence.InputSequence(
            input,
            OntologySourceReference,
            setter,
            (fun (value, setValue, remove) ->
                OntologySourceReferencesInput.OntologySourceReferenceInput(value, setValue, deleteButton = remove)
            ),
            label = label
        )
