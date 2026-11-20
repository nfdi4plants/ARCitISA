/// A small example ARC for stories and tests.
///
/// Study Plants: four plants (two organisms) give four leaf samples (two treatments).
/// Assay Extraction: the leaves give extracts. Two temperatures make two signatures and two groups per side.
/// Assay Measurement: the extracts give raw data files.
/// Assay Empty: has no tables.
/// Run Analysis: the raw data files give result files.
///
/// The work in progress ARC (wipIndex) has an assay Draft with unfinished rows: one extraction table whose rows
/// lack an output or an input, and a planned table that holds only a protocol.
module internal Swate.Components.Page.ProvenanceGraphSample

open ARCtrl
open Swate.Components.Shared.ProvenanceGraph.Types
open Swate.Components.Shared.ProvenanceGraph.BuildIndex

module private Fixtures =

    let oa name = OntologyAnnotation.create (name = name)

    let text value = CompositeCell.createFreeText value

    let term value =
        CompositeCell.createTermFromString (name = value)

    let unitized value unitName =
        CompositeCell.createUnitizedFromString (value, name = unitName)

    let table (name: string) (headers: CompositeHeader list) (rows: CompositeCell list list) =
        let rows =
            rows |> List.map (fun row -> ResizeArray<CompositeCell>(row)) |> ResizeArray

        ArcTable.createFromRows (name, ResizeArray<CompositeHeader>(headers), rows)

    let temperature =
        OntologyAnnotation.create (name = "temperature", tsr = "PATO", tan = "PATO:0000146")

    let sampling () =
        let row plant organism leaf treatment = [
            text plant
            term organism
            text "sampling.md"
            text leaf
            term treatment
        ]

        table "Sampling" [
            CompositeHeader.Input IOType.Source
            CompositeHeader.Characteristic(oa "organism")
            CompositeHeader.ProtocolREF
            CompositeHeader.Output IOType.Sample
            CompositeHeader.Factor(oa "treatment")
        ] [
            row "plant-1" "Arabidopsis thaliana" "leaf-1" "control"
            row "plant-2" "Arabidopsis thaliana" "leaf-2" "drought"
            row "plant-3" "Nicotiana tabacum" "leaf-3" "control"
            row "plant-4" "Nicotiana tabacum" "leaf-4" "drought"
        ]

    let extraction () =
        let row leaf degrees extract = [
            text leaf
            text "extraction.md"
            unitized degrees "degree Celsius"
            text extract
        ]

        table "Extraction" [
            CompositeHeader.Input IOType.Sample
            CompositeHeader.ProtocolREF
            CompositeHeader.Parameter temperature
            CompositeHeader.Output IOType.Sample
        ] [
            row "leaf-1" "4" "extract-1"
            row "leaf-2" "4" "extract-2"
            row "leaf-3" "20" "extract-3"
            row "leaf-4" "20" "extract-4"
        ]

    let measurement () =
        let row extract rawFile = [
            text extract
            text "measurement.md"
            term "Orbitrap"
            text rawFile
        ]

        table "Mass spectrometry" [
            CompositeHeader.Input IOType.Sample
            CompositeHeader.ProtocolREF
            CompositeHeader.Parameter(oa "instrument model")
            CompositeHeader.Output IOType.Data
        ] [
            row "extract-1" "./data/m1.raw"
            row "extract-2" "./data/m2.raw"
            row "extract-3" "./data/m3.raw"
            row "extract-4" "./data/m4.raw"
        ]

    let processing () =
        let row rawFile resultFile = [ text rawFile; text "processing.md"; text resultFile ]

        table "Processing" [
            CompositeHeader.Input IOType.Data
            CompositeHeader.ProtocolREF
            CompositeHeader.Output IOType.Data
        ] [
            row "./data/m1.raw" "./results/m1.csv"
            row "./data/m2.raw" "./results/m2.csv"
            row "./data/m3.raw" "./results/m3.csv"
            row "./data/m4.raw" "./results/m4.csv"
        ]

    let investigation () =
        ArcInvestigation.create (
            identifier = "Plants-example",
            studies =
                ResizeArray [
                    ArcStudy.create (identifier = "Plants", tables = ResizeArray [ sampling () ])
                ],
            assays =
                ResizeArray [
                    ArcAssay.create (identifier = "Extraction", tables = ResizeArray [ extraction () ])
                    ArcAssay.create (identifier = "Measurement", tables = ResizeArray [ measurement () ])
                    ArcAssay.create (identifier = "Empty")
                ],
            runs =
                ResizeArray [
                    ArcRun.create (identifier = "Analysis", tables = ResizeArray [ processing () ])
                ]
        )

    /// Rows that are not finished: two without an output, one without an input, and a table of two rows
    /// that holds only a protocol.
    let draftExtraction () =
        let row leaf extract = [ text leaf; text "extraction.md"; text extract ]

        table "Extraction" [
            CompositeHeader.Input IOType.Sample
            CompositeHeader.ProtocolREF
            CompositeHeader.Output IOType.Sample
        ] [
            row "leaf-1" "extract-1"
            row "leaf-2" ""
            row "leaf-3" ""
            row "" "extract-4"
        ]

    let draftPlanned () =
        table "Planned" [ CompositeHeader.ProtocolREF ] [ [ text "measurement.md" ]; [ text "measurement.md" ] ]

    let workInProgress () =
        ArcInvestigation.create (
            identifier = "Draft-example",
            assays =
                ResizeArray [
                    ArcAssay.create (identifier = "Draft", tables = ResizeArray [ draftExtraction (); draftPlanned () ])
                ]
        )

/// The plain data of the example ARC.
let data: ProvenanceGraphData =
    ProvenanceGraphData.ofInvestigation (Fixtures.investigation ())

/// The index of the example ARC.
let index: ProvenanceIndex = ProvenanceIndex.ofData data

/// An ARC without studies, assays and runs.
let emptyIndex: ProvenanceIndex =
    ProvenanceIndex.ofData (ProvenanceGraphData.ofInvestigation (ArcInvestigation.init "Empty-example"))

/// Three datasets whose short labels exercise the overview's minimum chip width.
let shortLabelsIndex: ProvenanceIndex =
    ArcInvestigation.create (
        identifier = "Short-example",
        assays =
            ResizeArray [
                ArcAssay.create (identifier = "S1")
                ArcAssay.create (identifier = "S2")
                ArcAssay.create (identifier = "S3")
            ]
    )
    |> ProvenanceGraphData.ofInvestigation
    |> ProvenanceIndex.ofData

/// An ARC with unfinished rows, as a draft has them. Unfinished rows show as nodes without a connection.
let wipIndex: ProvenanceIndex =
    ProvenanceIndex.ofData (ProvenanceGraphData.ofInvestigation (Fixtures.workInProgress ()))
