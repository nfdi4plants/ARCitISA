namespace Swate.Components.Page.Metadata

open System
open Fable.Core
open Feliz
open ARCtrl
open Swate.Components
open Swate.Components.Shared

type private LazyComponents =

    [<ReactLazyComponent>]
    static member LazyInvestigationMetadata
        (
            investigation: ArcInvestigation,
            setInvestigation: ArcInvestigation -> unit,
            ?onImportPersons: unit -> JS.Promise<Person[]>
        ) =
        InvestigationMetadata.InvestigationMetadata(
            investigation = investigation,
            setInvestigation = setInvestigation,
            ?onImportPersons = onImportPersons
        )

    [<ReactLazyComponent>]
    static member LazyStudyMetadata
        (study: ArcStudy, setStudy: ArcStudy -> unit, ?onImportPersons: unit -> JS.Promise<Person[]>)
        =
        StudyMetadata.StudyMetadata(study = study, setStudy = setStudy, ?onImportPersons = onImportPersons)

    [<ReactLazyComponent>]
    static member LazyAssayMetadata
        (assay: ArcAssay, setAssay: ArcAssay -> unit, ?onImportPersons: unit -> JS.Promise<Person[]>)
        =
        AssayMetadata.AssayMetadata(assay = assay, setAssay = setAssay, ?onImportPersons = onImportPersons)

    [<ReactLazyComponent>]
    static member LazyRunMetadata(run: ArcRun, setRun: ArcRun -> unit, ?onImportPersons: unit -> JS.Promise<Person[]>) =
        RunMetadata.RunMetadata(run = run, setRun = setRun, ?onImportPersons = onImportPersons)

    [<ReactLazyComponent>]
    static member LazyWorkflowMetadata
        (workflow: ArcWorkflow, setWorkflow: ArcWorkflow -> unit, ?onImportPersons: unit -> JS.Promise<Person[]>)
        =
        WorkflowMetadata.WorkflowMetadata(
            workflow = workflow,
            setWorkflow = setWorkflow,
            ?onImportPersons = onImportPersons
        )

    [<ReactLazyComponent>]
    static member LazyDataMapMetadata(datamap: Datamap) =
        DataMapMetadata.DataMapMetadata(datamap = datamap)

    [<ReactLazyComponent>]
    static member LazyTemplateMetadata
        (template: ARCtrl.Template, setTemplate: ARCtrl.Template -> unit, ?onImportPersons: unit -> JS.Promise<Person[]>) =
        TemplateMetadata.TemplateMetadata(
            template = template,
            setTemplate = setTemplate,
            ?onImportPersons = onImportPersons
        )

[<Erase; Mangle(false)>]
type ArcFileMetadata =

    [<ReactComponent>]
    static member private LazyFallback(text: string) =
        Html.div [
            prop.className "swt:flex swt:items-center swt:justify-center swt:p-3 swt:text-sm swt:opacity-70"
            prop.text text
        ]

    [<ReactComponent(true)>]
    static member ArcFileMetadata
        // 👀 If you rename these variables, ensure that the names are forwarded for lazy loading in `src\Components\src\ARCFileEditor\ArcFileEditor.fs` as well!
        (arcFile: ArcFiles, setArcFile: ArcFiles -> unit, ?onImportPersons: unit -> JS.Promise<Person[]>) =

        match arcFile with
        | ArcFiles.Investigation investigation ->
            React.Suspense(
                [
                    LazyComponents.LazyInvestigationMetadata(
                        investigation,
                        (fun updated -> setArcFile (ArcFiles.Investigation updated)),
                        ?onImportPersons = onImportPersons
                    )
                ],
                fallback = ArcFileMetadata.LazyFallback("Loading investigation metadata...")
            )
        | ArcFiles.Study(study, assays) ->
            React.Suspense(
                [
                    LazyComponents.LazyStudyMetadata(
                        study,
                        (fun updated -> setArcFile (ArcFiles.Study(updated, assays))),
                        ?onImportPersons = onImportPersons
                    )
                ],
                fallback = ArcFileMetadata.LazyFallback("Loading study metadata...")
            )
        | ArcFiles.Assay assay ->
            React.Suspense(
                [
                    LazyComponents.LazyAssayMetadata(
                        assay,
                        (fun updated -> setArcFile (ArcFiles.Assay updated)),
                        ?onImportPersons = onImportPersons
                    )
                ],
                fallback = ArcFileMetadata.LazyFallback("Loading assay metadata...")
            )
        | ArcFiles.Run run ->
            React.Suspense(
                [
                    LazyComponents.LazyRunMetadata(
                        run,
                        (fun updated -> setArcFile (ArcFiles.Run updated)),
                        ?onImportPersons = onImportPersons
                    )
                ],
                fallback = ArcFileMetadata.LazyFallback("Loading run metadata...")
            )
        | ArcFiles.Workflow workflow ->
            React.Suspense(
                [
                    LazyComponents.LazyWorkflowMetadata(
                        workflow,
                        (fun updated -> setArcFile (ArcFiles.Workflow updated)),
                        ?onImportPersons = onImportPersons
                    )
                ],
                fallback = ArcFileMetadata.LazyFallback("Loading workflow metadata...")
            )

        | ArcFiles.DataMap(_, datamap) ->
            React.Suspense(
                [ LazyComponents.LazyDataMapMetadata(datamap) ],
                fallback = ArcFileMetadata.LazyFallback("Loading datamap metadata...")
            )
        | ArcFiles.Template template ->
            React.Suspense(
                [
                    LazyComponents.LazyTemplateMetadata(
                        template,
                        (fun updated -> setArcFile (ArcFiles.Template updated)),
                        ?onImportPersons = onImportPersons
                    )
                ],
                fallback = ArcFileMetadata.LazyFallback("Loading template metadata...")
            )
