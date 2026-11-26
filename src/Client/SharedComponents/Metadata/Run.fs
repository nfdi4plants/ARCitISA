module Components.Metadata.Run

open Arcitisa.Components.Shared
open Feliz
open ARCtrl
open Arcitisa.Components.Shared
open Components
open Components.Forms

[<ReactComponent>]
let Main (run: ArcRun, setArcRun: ArcRun -> unit, model: Model.Model) =
    Generic.Section [
        Generic.BoxedField(
            "Run Metadata",
            content = [
                FormComponents.TextInput(
                    run.Identifier,
                    (fun v ->
                        let nextRun = IdentifierSetters.setRunIdentifier v run
                        setArcRun nextRun
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
                    (Option.defaultValue "" run.Title),
                    (fun v ->
                        run.Title <- Option.whereNot System.String.IsNullOrWhiteSpace v
                        setArcRun <| run
                    ),
                    "Title",
                    classes = "swt:w-full"
                )
                FormComponents.TextInput(
                    (Option.defaultValue "" run.Description),
                    (fun v ->
                        run.Description <- Option.whereNot System.String.IsNullOrWhiteSpace v
                        setArcRun <| run
                    ),
                    "Description",
                    classes = "swt:w-full",
                    isarea = true
                )
                FormComponents.OntologyAnnotationInput(
                    run.MeasurementType,
                    (fun oa ->
                        run.MeasurementType <- oa
                        setArcRun <| run
                    ),
                    "Measurement Type"
                )
                FormComponents.OntologyAnnotationInput(
                    run.TechnologyType,
                    (fun oa ->
                        run.TechnologyType <- oa
                        setArcRun <| run
                    ),
                    "Technology Type"
                )
                FormComponents.OntologyAnnotationInput(
                    run.TechnologyPlatform,
                    (fun oa ->
                        run.TechnologyPlatform <- oa
                        setArcRun <| run
                    ),
                    "Technology Platform"
                )
                FormComponents.CollectionOfStrings(
                    run.WorkflowIdentifiers,
                    (fun ids ->
                        run.WorkflowIdentifiers <- ResizeArray ids
                        setArcRun run
                    ),
                    "Workflow Identifiers"
                )
                FormComponents.PersonsInput(
                    run.Performers,
                    (fun persons ->
                        run.Performers <- persons
                        setArcRun run
                    ),
                    model.PersistentStorageState.IsARCitect,
                    "Performers"
                )
                FormComponents.CommentsInput(
                    run.Comments,
                    (fun comments ->
                        run.Comments <- ResizeArray comments
                        setArcRun run
                    ),
                    "Comments"
                )
            ]
        )
    ]
