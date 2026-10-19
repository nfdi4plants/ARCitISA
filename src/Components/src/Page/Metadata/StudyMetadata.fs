namespace Swate.Components.Page.Metadata

open System
open Fable.Core
open Feliz
open ARCtrl
open Swate.Components
open Swate.Components.Primitive.LayoutComponents
open Swate.Components.Page.Metadata.FormComponents
open Swate.Components.Page.Metadata.FormComponents.PersonsInput
open Swate.Components.Page.Metadata.FormComponents.PublicationsInput

[<Erase; Mangle(false)>]
type StudyMetadata =

    [<ReactComponent(true)>]
    static member StudyMetadata
        // 👀 If you rename these variables, ensure that the names are forwarded for lazy loading in `src\Components\src\Metadata\ArcFileMetadata.fs` as well!
        (study: ArcStudy, setStudy: ArcStudy -> unit, ?onImportPersons: unit -> JS.Promise<Person[]>) =
        LayoutComponents.Section [
            LayoutComponents.BoxedField(
                "Study Metadata",
                content = [
                    TextInput.TextInput(study.Identifier, (fun _ -> ()), label = "Identifier", disabled = true)
                    TextInput.TextInput(
                        defaultArg study.Title "",
                        (fun value ->
                            study.Title <- if String.IsNullOrWhiteSpace value then None else Some value
                            setStudy study
                        ),
                        label = "Title"
                    )
                    TextInput.TextInput(
                        defaultArg study.Description "",
                        (fun value ->
                            study.Description <- if String.IsNullOrWhiteSpace value then None else Some value
                            setStudy study
                        ),
                        label = "Description",
                        isArea = true
                    )
                    PersonsInput.PersonsInput(
                        study.Contacts,
                        (fun persons ->
                            study.Contacts <- persons
                            setStudy study
                        ),
                        label = "Contacts",
                        ?onImportPersons = onImportPersons
                    )
                    PublicationsInput.PublicationsInput(
                        study.Publications,
                        (fun publications ->
                            study.Publications <- publications
                            setStudy study
                        ),
                        label = "Publications"
                    )
                    DateTimeInput.DateTimeInput(
                        defaultArg study.SubmissionDate "",
                        (fun value ->
                            study.SubmissionDate <- if String.IsNullOrWhiteSpace value then None else Some value
                            setStudy study
                        ),
                        label = "Submission Date"
                    )
                    DateTimeInput.DateTimeInput(
                        defaultArg study.PublicReleaseDate "",
                        (fun value ->
                            study.PublicReleaseDate <- if String.IsNullOrWhiteSpace value then None else Some value
                            setStudy study
                        ),
                        label = "Public Release Date"
                    )
                    OntologyAnnotationInput.OntologyAnnotationsInput(
                        study.StudyDesignDescriptors,
                        (fun annotations ->
                            study.StudyDesignDescriptors <- annotations
                            setStudy study
                        ),
                        label = "Study Design Descriptors"
                    )
                    CommentsInput.CommentsInput(
                        study.Comments,
                        (fun comments ->
                            study.Comments <- comments
                            setStudy study
                        ),
                        label = "Comments"
                    )
                ]
            )
        ]
