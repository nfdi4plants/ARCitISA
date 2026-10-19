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
type RunMetadata =

    [<ReactComponent(true)>]
    static member RunMetadata
        // 👀 If you rename these variables, ensure that the names are forwarded for lazy loading in `src\Components\src\Metadata\ArcFileMetadata.fs` as well!
        (run: ArcRun, setRun: ArcRun -> unit, ?onImportPersons: unit -> JS.Promise<Person[]>) =
        LayoutComponents.Section [
            LayoutComponents.BoxedField(
                "Run Metadata",
                content = [
                    TextInput.TextInput(run.Identifier, (fun _ -> ()), label = "Identifier", disabled = true)
                    TextInput.TextInput(
                        defaultArg run.Title "",
                        (fun value ->
                            run.Title <- if String.IsNullOrWhiteSpace value then None else Some value
                            setRun run
                        ),
                        label = "Title"
                    )
                    TextInput.TextInput(
                        defaultArg run.Description "",
                        (fun value ->
                            run.Description <- if String.IsNullOrWhiteSpace value then None else Some value
                            setRun run
                        ),
                        label = "Description",
                        isArea = true
                    )
                    OntologyAnnotationInput.OntologyAnnotationInput(
                        run.MeasurementType,
                        (fun annotation ->
                            run.MeasurementType <- annotation
                            setRun run
                        ),
                        label = "Measurement Type"
                    )
                    OntologyAnnotationInput.OntologyAnnotationInput(
                        run.TechnologyType,
                        (fun annotation ->
                            run.TechnologyType <- annotation
                            setRun run
                        ),
                        label = "Technology Type"
                    )
                    OntologyAnnotationInput.OntologyAnnotationInput(
                        run.TechnologyPlatform,
                        (fun annotation ->
                            run.TechnologyPlatform <- annotation
                            setRun run
                        ),
                        label = "Technology Platform"
                    )
                    TextInput.CollectionOfStrings(
                        run.WorkflowIdentifiers,
                        (fun identifiers ->
                            run.WorkflowIdentifiers <- identifiers
                            setRun run
                        ),
                        label = "Workflow Identifiers"
                    )
                    PersonsInput.PersonsInput(
                        run.Performers,
                        (fun persons ->
                            run.Performers <- persons
                            setRun run
                        ),
                        label = "Performers",
                        ?onImportPersons = onImportPersons
                    )
                    CommentsInput.CommentsInput(
                        run.Comments,
                        (fun comments ->
                            run.Comments <- comments
                            setRun run
                        ),
                        label = "Comments"
                    )
                ]
            )
        ]
