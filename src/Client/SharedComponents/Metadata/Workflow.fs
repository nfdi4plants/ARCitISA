module Components.Metadata.Workflow

open Swate.Components.Shared
open Feliz
open ARCtrl
open Swate.Components.Shared
open Components
open Components.Forms

[<ReactComponent>]
let Main (workflow: ArcWorkflow, setArcWorkflow: ArcWorkflow -> unit, model: Model.Model) =
    /// default SemVer Regex: https://semver.org/#is-there-a-suggested-regular-expression-regex-to-check-a-semver-string
    /// CHANGED: Added optional 'v' at start to allow versions like 'v1.0.0'
    let versionPattern =
        @"^(v)?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$"

    // Function to check if a string contains only valid characters
    let checkVersionStr (identifier: string) =
        match identifier with
        | Regex versionPattern _ -> true
        | _ -> false

    Generic.Section [
        Generic.BoxedField(
            "Workflow Metadata",
            content = [
                FormComponents.TextInput(
                    workflow.Identifier,
                    (fun v ->
                        let nextWorkflow = IdentifierSetters.setWorkflowIdentifier v workflow
                        setArcWorkflow nextWorkflow
                    ),
                    "Identifier",
                    validator =
                        (fun s ->
                            try
                                ARCtrl.Helper.Identifier.checkValidCharacters s
                                Ok()
                            with ex ->
                                Error ex.Message
                        ),
                    disabled = Generic.isDisabledInARCitect model.PersistentStorageState.Host,
                    classes = "swt:w-full"
                )
                FormComponents.TextInput(
                    defaultArg workflow.Title "",
                    (fun v ->
                        workflow.Title <- Option.whereNot System.String.IsNullOrWhiteSpace v
                        setArcWorkflow workflow
                    ),
                    "Title",
                    classes = "swt:w-full"
                )
                FormComponents.TextInput(
                    defaultArg workflow.Description "",
                    (fun v ->
                        workflow.Description <- Option.whereNot System.String.IsNullOrWhiteSpace v
                        setArcWorkflow workflow
                    ),
                    "Description",
                    classes = "swt:w-full",
                    isarea = true
                )
                FormComponents.TextInput(
                    defaultArg workflow.Version "",
                    (fun v ->
                        workflow.Version <- Option.whereNot System.String.IsNullOrWhiteSpace v
                        setArcWorkflow workflow
                    ),
                    "Version",
                    validator =
                        (fun s ->
                            match checkVersionStr s with
                            | true -> Ok()
                            | false -> Error "Version must follow SemVer format (e.g., 1.0.0 or v1.0.0)"
                        ),
                    classes = "swt:w-full"
                )
                FormComponents.OntologyAnnotationInput(
                    workflow.WorkflowType,
                    (fun oa ->
                        workflow.WorkflowType <- oa
                        setArcWorkflow <| workflow
                    ),
                    "Workflow Type"
                )
                FormComponents.TextInput(
                    defaultArg workflow.URI "",
                    (fun v ->
                        workflow.URI <- Option.whereNot System.String.IsNullOrWhiteSpace v
                        setArcWorkflow workflow
                    ),
                    "URI",
                    classes = "swt:w-full"
                )
                // FormComponents.CollectionOfStrings(
                //     workflow.SubWorkflowIdentifiers,
                //     (fun identifiers ->
                //         workflow.SubWorkflowIdentifiers <- identifiers
                //         setArcWorkflow workflow
                //     ),
                //     "SubWorkflow Identifiers"
                // )
                FormComponents.OntologyAnnotationsInput(
                    workflow.Parameters,
                    (fun parameter ->
                        workflow.Parameters <- parameter
                        setArcWorkflow workflow
                    ),
                    "Parameters"
                )
                FormComponents.ComponentsInput(
                    workflow.Components,
                    (fun workflowComponent ->
                        workflow.Components <- workflowComponent
                        setArcWorkflow workflow
                    ),
                    "Components"
                )
                FormComponents.PersonsInput(
                    workflow.Contacts,
                    (fun persons ->
                        workflow.Contacts <- persons
                        setArcWorkflow workflow
                    ),
                    model.PersistentStorageState.IsARCitect,
                    "Contacts"
                )
                FormComponents.CommentsInput(
                    workflow.Comments,
                    (fun comments ->
                        workflow.Comments <- ResizeArray comments
                        setArcWorkflow workflow
                    ),
                    "Comments"
                )
            ]
        )
    ]
