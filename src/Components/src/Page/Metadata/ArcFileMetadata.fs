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
    static member LazyDataMapMetadata(datamap: DataMap) =
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

    [<ReactComponent>]
    static member private LazyInvestigationMetadata
        (
            investigation: ArcInvestigation,
            setInvestigation: ArcInvestigation -> unit,
            ?onImportPersons: unit -> JS.Promise<Person[]>
        ) =
        React.Suspense(
            [
                LazyComponents.LazyInvestigationMetadata(
                    investigation,
                    setInvestigation,
                    ?onImportPersons = onImportPersons
                )
            ],
            fallback = ArcFileMetadata.LazyFallback("Loading investigation metadata...")
        )

    [<ReactComponent>]
    static member private LazyStudyMetadata
        (study: ArcStudy, setStudy: ArcStudy -> unit, ?onImportPersons: unit -> JS.Promise<Person[]>)
        =
        React.Suspense(
            [
                LazyComponents.LazyStudyMetadata(study, setStudy, ?onImportPersons = onImportPersons)
            ],
            fallback = ArcFileMetadata.LazyFallback("Loading study metadata...")
        )

    [<ReactComponent>]
    static member private LazyAssayMetadata
        (assay: ArcAssay, setAssay: ArcAssay -> unit, ?onImportPersons: unit -> JS.Promise<Person[]>)
        =
        React.Suspense(
            [
                LazyComponents.LazyAssayMetadata(assay, setAssay, ?onImportPersons = onImportPersons)
            ],
            fallback = ArcFileMetadata.LazyFallback("Loading assay metadata...")
        )

    [<ReactComponent>]
    static member private LazyRunMetadata
        (run: ArcRun, setRun: ArcRun -> unit, ?onImportPersons: unit -> JS.Promise<Person[]>)
        =
        React.Suspense(
            [
                LazyComponents.LazyRunMetadata(run, setRun, ?onImportPersons = onImportPersons)
            ],
            fallback = ArcFileMetadata.LazyFallback("Loading run metadata...")
        )

    [<ReactComponent>]
    static member private LazyWorkflowMetadata
        (workflow: ArcWorkflow, setWorkflow: ArcWorkflow -> unit, ?onImportPersons: unit -> JS.Promise<Person[]>)
        =
        React.Suspense(
            [
                LazyComponents.LazyWorkflowMetadata(workflow, setWorkflow, ?onImportPersons = onImportPersons)
            ],
            fallback = ArcFileMetadata.LazyFallback("Loading workflow metadata...")
        )

    [<ReactComponent>]
    static member private LazyDataMapMetadata(datamap: DataMap) =
        React.Suspense(
            [ LazyComponents.LazyDataMapMetadata(datamap) ],
            fallback = ArcFileMetadata.LazyFallback("Loading datamap metadata...")
        )

    [<ReactComponent>]
    static member private LazyTemplateMetadata
        (template: ARCtrl.Template, setTemplate: ARCtrl.Template -> unit, ?onImportPersons: unit -> JS.Promise<Person[]>) =
        React.Suspense(
            [
                LazyComponents.LazyTemplateMetadata(template, setTemplate, ?onImportPersons = onImportPersons)
            ],
            fallback = ArcFileMetadata.LazyFallback("Loading template metadata...")
        )

    [<ReactComponent(true)>]
    static member ArcFileMetadata
        // 👀 If you rename these variables, ensure that the names are forwarded for lazy loading in `src\Components\src\ARCFileEditor\ArcFileEditor.fs` as well!
        (arcFile: ArcFiles, setArcFile: ArcFiles -> unit, ?onImportPersons: unit -> JS.Promise<Person[]>) =

        match arcFile with
        | ArcFiles.Investigation investigation ->
            ArcFileMetadata.LazyInvestigationMetadata(
                investigation,
                (fun updated -> setArcFile (ArcFiles.Investigation updated)),
                ?onImportPersons = onImportPersons
            )
        | ArcFiles.Study(study, assays) ->
            ArcFileMetadata.LazyStudyMetadata(
                study,
                (fun updated -> setArcFile (ArcFiles.Study(updated, assays))),
                ?onImportPersons = onImportPersons
            )
        | ArcFiles.Assay assay ->
            ArcFileMetadata.LazyAssayMetadata(
                assay,
                (fun updated -> setArcFile (ArcFiles.Assay updated)),
                ?onImportPersons = onImportPersons
            )
        | ArcFiles.Run run ->
            ArcFileMetadata.LazyRunMetadata(
                run,
                (fun updated -> setArcFile (ArcFiles.Run updated)),
                ?onImportPersons = onImportPersons
            )
        | ArcFiles.Workflow workflow ->
            ArcFileMetadata.LazyWorkflowMetadata(
                workflow,
                (fun updated -> setArcFile (ArcFiles.Workflow updated)),
                ?onImportPersons = onImportPersons
            )
        | ArcFiles.DataMap(_, datamap) -> ArcFileMetadata.LazyDataMapMetadata(datamap)
        | ArcFiles.Template template ->
            ArcFileMetadata.LazyTemplateMetadata(
                template,
                (fun updated -> setArcFile (ArcFiles.Template updated)),
                ?onImportPersons = onImportPersons
            )
