namespace Swate.Components.Page.Metadata

open System
open Fable.Core
open Feliz
open ARCtrl
open Swate.Components
open Swate.Components.Primitive.LayoutComponents
open Swate.Components.Page.Metadata
open Swate.Components.Page.Metadata.FormComponents
open Swate.Components.Page.Metadata.FormComponents.PersonsInput

[<Erase; Mangle(false)>]
type AssayMetadata =

    [<ReactComponent(true)>]
    static member AssayMetadata
        // 👀 If you rename these variables, ensure that the names are forwarded for lazy loading in `src\Components\src\Metadata\ArcFileMetadata.fs` as well!
        (assay: ArcAssay, setAssay: ArcAssay -> unit, ?onImportPersons: unit -> JS.Promise<Person[]>) =
        LayoutComponents.Section [
            LayoutComponents.BoxedField(
                "Assay Metadata",
                content = [
                    TextInput.TextInput(assay.Identifier, (fun _ -> ()), label = "Identifier", disabled = true)
                    TextInput.TextInput(
                        defaultArg assay.Title "",
                        (fun value ->
                            assay.Title <- if String.IsNullOrWhiteSpace value then None else Some value
                            setAssay assay
                        ),
                        label = "Title"
                    )
                    TextInput.TextInput(
                        defaultArg assay.Description "",
                        (fun value ->
                            assay.Description <- if String.IsNullOrWhiteSpace value then None else Some value
                            setAssay assay
                        ),
                        label = "Description",
                        isArea = true
                    )
                    OntologyAnnotationInput.OntologyAnnotationInput(
                        assay.MeasurementType,
                        (fun annotation ->
                            assay.MeasurementType <- annotation
                            setAssay assay
                        ),
                        label = "Measurement Type"
                    )
                    OntologyAnnotationInput.OntologyAnnotationInput(
                        assay.TechnologyType,
                        (fun annotation ->
                            assay.TechnologyType <- annotation
                            setAssay assay
                        ),
                        label = "Technology Type"
                    )
                    OntologyAnnotationInput.OntologyAnnotationInput(
                        assay.TechnologyPlatform,
                        (fun annotation ->
                            assay.TechnologyPlatform <- annotation
                            setAssay assay
                        ),
                        label = "Technology Platform"
                    )
                    PersonsInput.PersonsInput(
                        assay.Performers,
                        (fun persons ->
                            assay.Performers <- persons
                            setAssay assay
                        ),
                        label = "Performers",
                        ?onImportPersons = onImportPersons
                    )
                    CommentsInput.CommentsInput(
                        assay.Comments,
                        (fun comments ->
                            assay.Comments <- comments
                            setAssay assay
                        ),
                        label = "Comments"
                    )
                ]
            )
        ]
