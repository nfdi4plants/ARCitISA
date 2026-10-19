module ElectronCore.ArcNodesReaderTests

open ARCtrl
open Fable.Core
open Main
open Swate.Components.Shared.ProvenanceGrouping.ARCtrlConverter
open Swate.Electron.Shared.DTOs.ArcNodesDto
open Vitest

[<Emit("structuredClone($0)")>]
let private structuredClone<'T> (value: 'T) : 'T = jsNative

let private ioTable (name: string) (input: string) (output: string) =
    let table = ArcTable.init name
    table.AddColumn(CompositeHeader.Input IOType.Source, ResizeArray [ CompositeCell.createFreeText input ])

    table.AddColumn(
        CompositeHeader.Parameter(OntologyAnnotation("Temperature")),
        ResizeArray [ CompositeCell.createTermFromString "22" ]
    )

    table.AddColumn(CompositeHeader.Output IOType.Sample, ResizeArray [ CompositeCell.createFreeText output ])
    table

let private read arc =
    ArcNodesReader.read arc
    |> structuredClone
    |> Array.map ArcNodeTableDto.toArcNodeTable

Vitest.describe (
    "ArcNodesReader",
    fun () ->
        Vitest.test (
            "returns study, assay and run tables with their location",
            fun () ->
                let study = ArcStudy("study")
                study.AddTable(ioTable "study-table" "source-a" "sample-a")
                let assay = ArcAssay("assay")
                assay.AddTable(ioTable "assay-table" "sample-a" "extract-a")
                let run = ArcRun("run")
                run.AddTable(ioTable "run-table" "extract-a" "result-a")

                let arc =
                    ARC(
                        "arc",
                        studies = ResizeArray [ study ],
                        assays = ResizeArray [ assay ],
                        runs = ResizeArray [ run ]
                    )

                let locations = read arc |> Array.map _.Location

                Vitest
                    .expect(locations)
                    .toEqual (
                        [|
                            {
                                Scope = ArcTableScope.Study
                                ParentIdentifier = "study"
                                TableName = "study-table"
                            }
                            {
                                Scope = ArcTableScope.Assay
                                ParentIdentifier = "assay"
                                TableName = "assay-table"
                            }
                            {
                                Scope = ArcTableScope.Run
                                ParentIdentifier = "run"
                                TableName = "run-table"
                            }
                        |]
                    )
        )

        Vitest.test (
            "keeps only input and output columns with their cells after IPC cloning",
            fun () ->
                let assay = ArcAssay("assay")
                assay.AddTable(ioTable "assay-table" "sample-a" "extract-a")
                let arc = ARC("arc", assays = ResizeArray [ assay ])

                let table = (read arc |> Array.exactlyOne).Table

                Vitest
                    .expect(table.Headers |> Seq.toArray)
                    .toEqual (
                        [|
                            CompositeHeader.Input IOType.Source
                            CompositeHeader.Output IOType.Sample
                        |]
                    )

                Vitest.expect(table.GetInputColumn().Cells.[0]).toEqual (CompositeCell.createFreeText "sample-a")
                Vitest.expect(table.GetOutputColumn().Cells.[0]).toEqual (CompositeCell.createFreeText "extract-a")
        )

        Vitest.test (
            "skips tables without input or output column and leaves the ARC unchanged",
            fun () ->
                let assay = ArcAssay("assay")
                let protocolOnly = ArcTable.init "protocol-only"
                protocolOnly.AddColumn(CompositeHeader.ProtocolREF)
                assay.AddTable(protocolOnly)
                assay.AddTable(ArcTable.init "empty")
                let source = ioTable "with-nodes" "sample-a" "extract-a"
                assay.AddTable(source)
                let arc = ARC("arc", assays = ResizeArray [ assay ])

                let names = read arc |> Array.map _.Location.TableName

                Vitest.expect(names).toEqual ([| "with-nodes" |])
                Vitest.expect(source.ColumnCount).toBe 3
        )

        Vitest.test (
            "reads edits directly from the current in-memory ARC",
            fun () ->
                let assay = ArcAssay("assay")
                let arc = ARC("arc", assays = ResizeArray [ assay ])

                Vitest.expect(ArcNodesReader.read arc).toEqual ([||]: ArcNodeTableDto[])

                assay.AddTable(ioTable "added-without-saving" "sample-a" "extract-a")

                Vitest.expect(read arc |> Array.map _.Location.TableName).toEqual ([| "added-without-saving" |])
        )
)
