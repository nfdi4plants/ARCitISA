module internal Swate.Components.Tests.JsonCompatibility

open ARCtrl
open ARCtrl.Json
open Swate.Components.Shared
open Vitest

let private datamap () =
    Datamap(
        ResizeArray [
            DataContext(name = "data/file.csv", label = "legacy label")
        ]
    )

let private files () =
    let assay = ArcAssay.init "legacy-assay"
    assay.Datamap <- Some(datamap ())
    let study = ArcStudy.init "legacy-study"
    study.Datamap <- Some(datamap ())
    let run = ArcRun.init "legacy-run"
    run.Datamap <- Some(datamap ())
    let workflow = ArcWorkflow.init "legacy-workflow"
    workflow.Datamap <- Some(datamap ())

    [
        ArcFiles.Assay assay
        ArcFiles.Study(study, [])
        ArcFiles.Run run
        ArcFiles.Workflow workflow
    ]

for format in
    [
        JsonExportFormat.ARCtrl
        JsonExportFormat.ARCtrlCompressed
    ] do
    for file in files () do
        Vitest.test (
            $"imports legacy {file.RelatedArcFilesDiscriminate} datamaps in {format} JSON",
            fun () ->
                let _, currentJson = Export.parseToJsonString (file, format)
                let legacyJson = currentJson.Replace("\"Datamap\":", "\"DataMap\":")

                let restored =
                    Generic.readFromJsonMap.[file.RelatedArcFilesDiscriminate, format] legacyJson

                let restoredMap =
                    restored.TryGetDataMap()
                    |> Option.defaultWith (fun () -> failwith "Missing datamap")

                Vitest.expect(restoredMap.DataContexts.Count).toBe 1
                Vitest.expect(restoredMap.DataContexts.[0].Label).toEqual (Some "legacy label")
                Vitest.expect(restoredMap.DataContexts.[0].NameText).toBe "data/file.csv"
        )

Vitest.test (
    "restores datamaps nested in a legacy investigation session payload",
    fun () ->
        let assay = ArcAssay.init "nested"
        assay.Datamap <- Some(datamap ())
        let investigation = ArcInvestigation("legacy", assays = ResizeArray [ assay ])

        let json =
            (ArcInvestigation.toJsonString 0 investigation).Replace("\"Datamap\":", "\"DataMap\":")

        let restored = JsonCompatibility.fromInvestigationJsonString json
        Vitest.expect(restored.Assays.[0].Datamap.Value.DataContexts.[0].Label).toEqual (Some "legacy label")
)

Vitest.test (
    "normalizes legacy keys without changing text or replacing a current datamap",
    fun () ->
        let json =
            """{"Identifier":"example","Description":"DataMap","DataMap":{"dataContexts":[]},"Datamap":{"dataContexts":[{"data":{"name":"current.csv"}}]}}"""

        let restored = JsonCompatibility.fromAssayJsonString json
        Vitest.expect(restored.Description).toEqual (Some "DataMap")
        Vitest.expect(restored.Datamap.Value.DataContexts.Count).toBe 1
        Vitest.expect(restored.Datamap.Value.DataContexts.[0].NameText).toBe "current.csv"
)

Vitest.test (
    "imports Investigation RO-Crate JSON exported by the previous codec",
    fun () ->
        let study = ArcStudy.init "legacy-study"

        let investigation =
            ArcInvestigation("legacy-investigation", title = "Legacy title", studies = ResizeArray [ study ])

        let legacyJson =
            investigation |> Investigation.ROCrate.encoder |> Encode.toJsonString 0

        let restored =
            Generic.readFromJsonMap.[ArcFilesDiscriminate.Investigation, JsonExportFormat.ROCrate] legacyJson

        match restored with
        | ArcFiles.Investigation restored ->
            Vitest.expect(restored.Identifier).toBe "legacy-investigation"
            Vitest.expect(restored.Title).toEqual (Some "Legacy title")
            Vitest.expect(restored.Studies.[0].Identifier).toBe "legacy-study"
        | _ -> failwith "Expected an investigation"
)

Vitest.test (
    "round-trips current Investigation RO-Crate exports without mutating the source",
    fun () ->
        let investigation =
            ArcInvestigation("current-investigation", title = "Current title")

        let before = ArcInvestigation.toJsonString 0 investigation

        let _, json =
            Export.parseToJsonString (ArcFiles.Investigation investigation, JsonExportFormat.ROCrate)

        let restored = JsonCompatibility.fromInvestigationROCrateJsonString json
        Vitest.expect(restored.Identifier).toBe investigation.Identifier
        Vitest.expect(restored.Title).toEqual investigation.Title
        Vitest.expect(ArcInvestigation.toJsonString 0 investigation).toBe before
)

let private labelledInvestigation () =
    let datamap parent =
        Datamap(
            ResizeArray [
                DataContext(
                    name = $"data/{parent}-first.csv",
                    label = $"{parent} first label",
                    description = $"{parent} description"
                )
                DataContext(name = $"data/{parent}-unlabelled.csv")
                DataContext(name = $"data/{parent}-last.csv", label = $"{parent} last label")
            ]
        )

    let cwl () =
        CWL.Workflow(CWL.CWLWorkflowDescription(ResizeArray(), ResizeArray(), ResizeArray()))

    ArcInvestigation(
        "labelled-investigation",
        title = "Labelled investigation",
        assays =
            ResizeArray [
                ArcAssay("labelled-assay", datamap = datamap "assay")
                ArcAssay.init "no-map-assay"
            ],
        studies =
            ResizeArray [
                ArcStudy("labelled-study", datamap = datamap "study")
                ArcStudy.init "no-map-study"
            ],
        runs =
            ResizeArray [
                ArcRun("labelled-run", datamap = datamap "run", cwlDescription = cwl ())
                ArcRun("no-map-run", cwlDescription = cwl ())
            ],
        workflows =
            ResizeArray [
                ArcWorkflow("labelled-workflow", datamap = datamap "workflow", cwlDescription = cwl ())
                ArcWorkflow("no-map-workflow", cwlDescription = cwl ())
            ]
    )

let private nestedDatamaps (investigation: ArcInvestigation) = [
    for assay in investigation.Assays do
        yield assay.Datamap
    for study in investigation.Studies do
        yield study.Datamap
    for run in investigation.Runs do
        yield run.Datamap
    for workflow in investigation.Workflows do
        yield workflow.Datamap
]

Vitest.test (
    "copies investigation datamap labels for assays, studies, runs and workflows independently of the source",
    fun () ->
        let source = labelledInvestigation ()
        let before = ArcInvestigation.toJsonString 0 source
        let copied = DataMapCopyWorkaround.copyInvestigation source

        Seq.iter2
            (fun source target ->
                match source, target with
                | Some(source: Datamap), Some(target: Datamap) ->
                    let values (datamap: Datamap) =
                        datamap.DataContexts
                        |> Seq.map (fun context -> context.Name, context.Label, context.Description)
                        |> Seq.toArray

                    Vitest.expect(values target).toEqual (values source)
                    target.DataContexts.[0].Label <- Some "changed copy label"
                | None, None -> ()
                | _ -> failwith "Copy changed datamap presence"
            )
            (nestedDatamaps source)
            (nestedDatamaps copied)

        Vitest.expect(ArcInvestigation.toJsonString 0 source).toBe before
)

Vitest.test (
    "preserves nested assay, study and run datamap labels in Investigation RO-Crate exports without mutating the source",
    fun () ->
        let source = labelledInvestigation ()
        let before = ArcInvestigation.toJsonString 0 source

        let _, json =
            Export.parseToJsonString (ArcFiles.Investigation source, JsonExportFormat.ROCrate)

        for parent in [ "assay"; "study"; "run" ] do
            Vitest.expect(json.Contains $"{parent} first label").toBe true
            Vitest.expect(json.Contains $"{parent} last label").toBe true
            Vitest.expect(json.Contains $"{parent} description").toBe true

        Vitest.expect(ArcInvestigation.toJsonString 0 source).toBe before
)
