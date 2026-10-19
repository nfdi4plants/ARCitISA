namespace Swate.Components.Page.Metadata

open System
open Fable.Core
open Feliz
open ARCtrl
open Swate.Components
open Swate.Components.Primitive.LayoutComponents
open Swate.Components.Page.Metadata.FormComponents
open Swate.Components.Page.Metadata.FormComponents.PersonsInput

[<Erase; Mangle(false)>]
type WorkflowMetadata =

    [<ReactComponent(true)>]
    static member WorkflowMetadata
        // 👀 If you rename these variables, ensure that the names are forwarded for lazy loading in `src\Components\src\Metadata\ArcFileMetadata.fs` as well!
        (workflow: ArcWorkflow, setWorkflow: ArcWorkflow -> unit, ?onImportPersons: unit -> JS.Promise<Person[]>) =
        LayoutComponents.Section [
            LayoutComponents.BoxedField(
                "Workflow Metadata",
                content = [
                    TextInput.TextInput(workflow.Identifier, (fun _ -> ()), label = "Identifier", disabled = true)
                    TextInput.TextInput(
                        defaultArg workflow.Title "",
                        (fun value ->
                            workflow.Title <- if String.IsNullOrWhiteSpace value then None else Some value
                            setWorkflow workflow
                        ),
                        label = "Title"
                    )
                    TextInput.TextInput(
                        defaultArg workflow.Description "",
                        (fun value ->
                            workflow.Description <- if String.IsNullOrWhiteSpace value then None else Some value
                            setWorkflow workflow
                        ),
                        label = "Description",
                        isArea = true
                    )
                    TextInput.TextInput(
                        defaultArg workflow.Version "",
                        (fun value ->
                            workflow.Version <- if String.IsNullOrWhiteSpace value then None else Some value
                            setWorkflow workflow
                        ),
                        label = "Version"
                    )
                    OntologyAnnotationInput.OntologyAnnotationInput(
                        workflow.WorkflowType,
                        (fun annotation ->
                            workflow.WorkflowType <- annotation
                            setWorkflow workflow
                        ),
                        label = "Workflow Type"
                    )
                    TextInput.TextInput(
                        defaultArg workflow.URI "",
                        (fun value ->
                            workflow.URI <- if String.IsNullOrWhiteSpace value then None else Some value
                            setWorkflow workflow
                        ),
                        label = "URI"
                    )
                    PersonsInput.PersonsInput(
                        workflow.Contacts,
                        (fun persons ->
                            workflow.Contacts <- persons
                            setWorkflow workflow
                        ),
                        label = "Contacts",
                        ?onImportPersons = onImportPersons
                    )
                    CommentsInput.CommentsInput(
                        workflow.Comments,
                        (fun comments ->
                            workflow.Comments <- comments
                            setWorkflow workflow
                        ),
                        label = "Comments"
                    )
                ]
            )
        ]
