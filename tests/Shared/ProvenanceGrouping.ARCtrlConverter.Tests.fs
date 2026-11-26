module ProvenanceGroupingARCtrlConverterTests

#if FABLE_COMPILER
open Fable.Mocha
#else
open Expecto
#endif

open ARCtrl
open Arcitisa.Components.Shared.ProvenanceGrouping.Types
open Arcitisa.Components.Shared.ProvenanceGrouping.ARCtrlConverter

let private oa name = OntologyAnnotation.create (name = name)

let private text value = CompositeCell.createFreeText value

let private term value =
    CompositeCell.createTermFromString (name = value)

let private table (name: string) (headers: CompositeHeader list) (rows: CompositeCell list list) =
    let rows =
        rows |> List.map (fun row -> ResizeArray<CompositeCell>(row)) |> ResizeArray

    ArcTable.createFromRows (name, ResizeArray<CompositeHeader>(headers), rows)

let private loadedAssayTable () =
    table "assay-table" [
        CompositeHeader.Input IOType.Sample
        CompositeHeader.Characteristic(oa "Species")
        CompositeHeader.Parameter(oa "Temperature")
        CompositeHeader.Output IOType.Sample
        CompositeHeader.Factor(oa "Replicate")
    ] [
        [
            text "sample-a"
            term "Arabidopsis"
            term "22"
            text "extract-a"
            term "R1"
        ]
        [
            text "sample-b"
            term "Arabidopsis"
            term "23"
            text "extract-b"
            term "R2"
        ]
    ]

let private previousStudyTable () =
    table "study-table" [
        CompositeHeader.Input IOType.Source
        CompositeHeader.Characteristic(oa "Organism")
        CompositeHeader.Output IOType.Sample
    ] [
        [ text "source-a"; term "Plant"; text "sample-a" ]
        [ text "source-b"; term "Plant"; text "sample-b" ]
    ]

let private loadedAssayTableWithDuplicateTemperatureColumns () =
    table "assay-table" [
        CompositeHeader.Input IOType.Sample
        CompositeHeader.Parameter(oa "Temperature")
        CompositeHeader.Parameter(oa "Temperature")
        CompositeHeader.Output IOType.Sample
    ] [
        [ text "sample-a"; term "22"; term "22"; text "extract-a" ]
    ]

let private inputOnlyAssayTable () =
    table "input-only-table" [
        CompositeHeader.Input IOType.Sample
        CompositeHeader.Characteristic(oa "Species")
        CompositeHeader.Parameter(oa "Temperature")
    ] [
        [ text "sample-a"; term "Arabidopsis"; term "22" ]
        [ text "sample-b"; term "Arabidopsis"; term "23" ]
    ]

let private outputOnlyAssayTable () =
    table "output-only-table" [
        CompositeHeader.Parameter(oa "Temperature")
        CompositeHeader.Output IOType.Sample
        CompositeHeader.Factor(oa "Replicate")
    ] [
        [ term "22"; text "extract-a"; term "R1" ]
        [ term "23"; text "extract-b"; term "R2" ]
    ]

let private repeatedMetaboliteExtractionTable () =
    table "metabolite_extraction" [
        CompositeHeader.Input IOType.Sample
        CompositeHeader.Parameter(oa "Extraction buffer")
        CompositeHeader.Parameter(oa "Biosource amount")
        CompositeHeader.Output IOType.Sample
    ] [
        [
            text "CAM_01"
            term "water:methanol:chloroform"
            term "6.1"
            text "DB23"
        ]
        [
            text "CAM_02"
            term "water:methanol:chloroform"
            term "5.2"
            text "DB24"
        ]
    ]

let private gasChromatographyLoadedTable () =
    table "gas_chromatography" [
        CompositeHeader.Input IOType.Sample
        CompositeHeader.Output IOType.Data
    ] [
        [ text "DB23"; text "raw-a" ]
        [ text "DB24"; text "raw-b" ]
    ]

let private previousStudyTableWithDuplicateOrganismColumns () =
    table "study-table" [
        CompositeHeader.Input IOType.Source
        CompositeHeader.Characteristic(oa "Organism")
        CompositeHeader.Characteristic(oa "Organism")
        CompositeHeader.Output IOType.Sample
    ] [
        [
            text "source-a"
            term "Plant"
            term "Plant"
            text "sample-a"
        ]
    ]

let private arcFixture () =
    let study =
        ArcStudy.create (
            identifier = "study-1",
            tables = ResizeArray [ previousStudyTable () ],
            registeredAssayIdentifiers = ResizeArray [ "assay-1" ]
        )

    let assay =
        ArcAssay.create (identifier = "assay-1", tables = ResizeArray [ loadedAssayTable () ])

    ARC(identifier = "arc-1", studies = ResizeArray [ study ], assays = ResizeArray [ assay ])

let private arcFixtureWithDuplicateTemperatureColumns () =
    let assay =
        ArcAssay.create (
            identifier = "assay-1",
            tables = ResizeArray [ loadedAssayTableWithDuplicateTemperatureColumns () ]
        )

    ARC(identifier = "arc-1", studies = ResizeArray [], assays = ResizeArray [ assay ])

let private arcFixtureWithRepeatedPreviousProcessRows () =
    let assay =
        ArcAssay.create (
            identifier = "assay-1",
            tables =
                ResizeArray [
                    repeatedMetaboliteExtractionTable ()
                    gasChromatographyLoadedTable ()
                ]
        )

    ARC(identifier = "arc-1", studies = ResizeArray [], assays = ResizeArray [ assay ])

let private previousStudyTableWithOutputContext () =
    table "study-table" [
        CompositeHeader.Input IOType.Source
        CompositeHeader.Characteristic(oa "Organism")
        CompositeHeader.Output IOType.Sample
        CompositeHeader.Characteristic(oa "Tissue")
        CompositeHeader.Factor(oa "Batch")
    ] [
        [
            text "source-a"
            term "Plant"
            text "sample-a"
            term "Leaf"
            term "B1"
        ]
        [
            text "source-b"
            term "Plant"
            text "sample-b"
            term "Root"
            term "B2"
        ]
    ]

let private arcFixtureWithPreviousOutputContext () =
    let study =
        ArcStudy.create (
            identifier = "study-1",
            tables = ResizeArray [ previousStudyTableWithOutputContext () ],
            registeredAssayIdentifiers = ResizeArray [ "assay-1" ]
        )

    let assay =
        ArcAssay.create (identifier = "assay-1", tables = ResizeArray [ loadedAssayTable () ])

    ARC(identifier = "arc-1", studies = ResizeArray [ study ], assays = ResizeArray [ assay ])

let private arcFixtureWithDuplicatePreviousColumns () =
    let study =
        ArcStudy.create (
            identifier = "study-1",
            tables = ResizeArray [ previousStudyTableWithDuplicateOrganismColumns () ],
            registeredAssayIdentifiers = ResizeArray [ "assay-1" ]
        )

    let assay =
        ArcAssay.create (identifier = "assay-1", tables = ResizeArray [ loadedAssayTable () ])

    ARC(identifier = "arc-1", studies = ResizeArray [ study ], assays = ResizeArray [ assay ])

let private loadedTable: ArcTableLocation = {
    Scope = ArcTableScope.Assay
    ParentIdentifier = "assay-1"
    TableName = "assay-table"
}

let private gasChromatographyLoadedTableLocation: ArcTableLocation = {
    Scope = ArcTableScope.Assay
    ParentIdentifier = "assay-1"
    TableName = "gas_chromatography"
}

let private convertWithPreviousContext () =
    fromLoadedArc
        {
            LoadedTable = loadedTable
            IncludePreviousContext = true
        }
        (arcFixture ())

let private convertWithoutPreviousContext () =
    fromLoadedArc
        {
            LoadedTable = loadedTable
            IncludePreviousContext = false
        }
        (arcFixture ())

let private expectText expected value =
    match value with
    | ProvenanceValue.Text actual -> Expect.equal actual expected "Expected text value."
    | ProvenanceValue.Term term -> Expect.equal term.Name expected "Expected term value."
    | ProvenanceValue.Integer actual -> Expect.equal (string actual) expected "Expected integer value."
    | ProvenanceValue.Float actual -> Expect.equal (string actual) expected "Expected float value."

let private anchorOfOrigin (origin: ProvenancePropertyOrigin) : ProvenanceWritebackAnchor =
    match origin with
    | ProvenancePropertyOrigin.Real anchor
    | ProvenancePropertyOrigin.Virtual anchor -> anchor

let private assertRealOriginProcess expectedProcessName expectedProcessId (value: ProvenancePropertyValue) =
    match value.Origin with
    | ProvenancePropertyOrigin.Real anchor ->
        Expect.equal anchor.ProcessName expectedProcessName "Origin process name should match."
        Expect.equal anchor.ProcessId expectedProcessId "Origin process id should match."
        anchor
    | ProvenancePropertyOrigin.Virtual _ -> failwith "Expected converted ARCtrl values to have real origins."

let tests =
    testList "ProvenanceGrouping ARCtrl converter" [
        testCase "converts loaded input and output names into first-class sets"
        <| fun _ ->
            let result = convertWithPreviousContext ()

            let inputNames =
                result.Model.InputSets
                |> Map.toList
                |> List.map (fun (_, set) -> set.Name)
                |> List.sort

            let outputNames =
                result.Model.OutputSets
                |> Map.toList
                |> List.map (fun (_, set) -> set.Name)
                |> List.sort

            Expect.equal inputNames [ "sample-a"; "sample-b" ] "Loaded inputs should come from loaded input cells."
            Expect.equal outputNames [ "extract-a"; "extract-b" ] "Loaded outputs should come from loaded output cells."

        testCase "omits previous context when IncludePreviousContext is false"
        <| fun _ ->
            let result = convertWithoutPreviousContext ()

            let previousValues =
                result.Model.PropertyValues
                |> Map.toSeq
                |> Seq.map snd
                |> Seq.filter (fun value -> (anchorOfOrigin value.Origin).Source.Id <> result.Model.Source.Id)
                |> Seq.toList

            Expect.isEmpty
                previousValues
                "No property values from non-loaded tables should appear when previous context is excluded."

        testCase "converted property values have real origins with source refs"
        <| fun _ ->
            let result = convertWithPreviousContext ()
            let values = result.Model.PropertyValues |> Map.toList |> List.map snd

            Expect.isNonEmpty values "Converted models should contain property values."

            Expect.all
                values
                (fun value ->
                    match value.Origin with
                    | ProvenancePropertyOrigin.Real anchor ->
                        not (System.String.IsNullOrWhiteSpace anchor.Source.Id)
                        && not (System.String.IsNullOrWhiteSpace anchor.Source.Name)
                    | ProvenancePropertyOrigin.Virtual _ -> false
                )
                "Every converted property value should have a real source origin."

            Expect.isTrue
                (values
                 |> List.exists (fun value -> (anchorOfOrigin value.Origin).Source.Id <> result.Model.Source.Id))
                "Previous-context values should keep their original source identity."

        testCase "converts loaded row input-to-output connections"
        <| fun _ ->
            let result = convertWithPreviousContext ()

            let connectionPairs =
                result.Model.Connections
                |> Map.toList
                |> List.map (fun (_, connection) ->
                    result.Model.InputSets.[connection.InputSetId].Name,
                    result.Model.OutputSets.[connection.OutputSetId].Name,
                    connection.ProcessName,
                    connection.ProcessId
                )
                |> List.sort

            Expect.equal
                connectionPairs
                [
                    ("sample-a", "extract-a", Some "assay-table", Some "assay-table_0")
                    ("sample-b", "extract-b", Some "assay-table", Some "assay-table_1")
                ]
                "Each loaded row should produce a loaded input/output connection with a logical process name and row process id."

        testCase "attaches loaded property values to loaded sets"
        <| fun _ ->
            let result = convertWithPreviousContext ()

            let sampleA =
                result.Model.InputSets
                |> Map.toSeq
                |> Seq.map snd
                |> Seq.find (fun set -> set.Name = "sample-a")

            let extractA =
                result.Model.OutputSets
                |> Map.toSeq
                |> Seq.map snd
                |> Seq.find (fun set -> set.Name = "extract-a")

            let species =
                sampleA.PropertyValueIds
                |> List.map (fun id -> result.Model.PropertyValues.[id])
                |> List.find (fun value -> value.Header.Category.Name = "Species")

            let temperature =
                sampleA.PropertyValueIds
                |> List.append extractA.PropertyValueIds
                |> List.map (fun id -> result.Model.PropertyValues.[id])
                |> List.find (fun value -> value.Header.Category.Name = "Temperature")

            let replicate =
                sampleA.PropertyValueIds
                |> List.append extractA.PropertyValueIds
                |> List.map (fun id -> result.Model.PropertyValues.[id])
                |> List.find (fun value -> value.Header.Category.Name = "Replicate")

            expectText "Arabidopsis" species.Value
            expectText "22" temperature.Value
            expectText "R1" replicate.Value

            Expect.isTrue
                (sampleA.PropertyValueIds |> List.contains replicate.Id)
                "Factor values should also connect to the loaded input set, like parameter values do, when the row directly connects input and output."

        testCase "collapses previous table context into property values on loaded inputs"
        <| fun _ ->
            let result = convertWithPreviousContext ()

            let sampleA =
                result.Model.InputSets
                |> Map.toSeq
                |> Seq.map snd
                |> Seq.find (fun set -> set.Name = "sample-a")

            let organism =
                sampleA.PropertyValueIds
                |> List.map (fun id -> result.Model.PropertyValues.[id])
                |> List.find (fun value -> value.Header.Category.Name = "Organism")

            expectText "Plant" organism.Value
            let location = result.Index.PropertyValueLocations.[organism.Id]

            Expect.equal
                location.Table.Scope
                ArcTableScope.Study
                "Collapsed previous value should remember study scope."

            Expect.equal
                location.Table.ParentIdentifier
                "study-1"
                "Collapsed previous value should remember parent identifier."

            Expect.equal location.Table.TableName "study-table" "Collapsed previous value should remember source table."

            Expect.equal
                location.OutputNames
                [ "sample-a" ]
                "Collapsed previous value should remember where it pointed to."

        testCase "collapses connected previous output property values into loaded input context"
        <| fun _ ->
            let result =
                fromLoadedArc
                    {
                        LoadedTable = loadedTable
                        IncludePreviousContext = true
                    }
                    (arcFixtureWithPreviousOutputContext ())

            let sampleA =
                result.Model.InputSets
                |> Map.toSeq
                |> Seq.map snd
                |> Seq.find (fun set -> set.Name = "sample-a")

            let values =
                sampleA.PropertyValueIds
                |> List.map (fun id -> result.Model.PropertyValues.[id])

            let tissue =
                values |> List.find (fun value -> value.Header.Category.Name = "Tissue")

            let batch = values |> List.find (fun value -> value.Header.Category.Name = "Batch")

            expectText "Leaf" tissue.Value
            expectText "B1" batch.Value

            let tissueLocation = result.Index.PropertyValueLocations.[tissue.Id]

            Expect.equal
                tissueLocation.Table.Scope
                ArcTableScope.Study
                "Previous output characteristic should keep study scope."

            Expect.equal
                tissueLocation.OutputNames
                [ "sample-a" ]
                "Previous output characteristic should keep the connected previous output name."

            let batchLocation = result.Index.PropertyValueLocations.[batch.Id]

            Expect.equal
                batchLocation.OutputNames
                [ "sample-a" ]
                "Previous output factor should keep the connected previous output name."

        testCase
            "identical duplicate loaded values collapse to one model value and one representative writeback location"
        <| fun _ ->
            let result =
                fromLoadedArc
                    {
                        LoadedTable = loadedTable
                        IncludePreviousContext = false
                    }
                    (arcFixtureWithDuplicateTemperatureColumns ())

            let sampleA =
                result.Model.InputSets
                |> Map.toSeq
                |> Seq.map snd
                |> Seq.find (fun set -> set.Name = "sample-a")

            let temperatures =
                sampleA.PropertyValueIds
                |> List.map (fun id -> result.Model.PropertyValues.[id])
                |> List.filter (fun value -> value.Header.Category.Name = "Temperature")

            Expect.equal temperatures.Length 1 "Equal duplicate loaded values should collapse into one model value."

            let writebackLocation = result.Index.PropertyValueLocations.[temperatures.Head.Id]

            Expect.equal
                writebackLocation.Table.TableName
                "assay-table"
                "A representative writeback location should still be preserved."

        testCase "reuses identical loaded values across generated row process names"
        <| fun _ ->
            let assay =
                ArcAssay.create (identifier = "assay-1", tables = ResizeArray [ repeatedMetaboliteExtractionTable () ])

            let arc =
                ARC(identifier = "arc-1", studies = ResizeArray [], assays = ResizeArray [ assay ])

            let location: ArcTableLocation = {
                Scope = ArcTableScope.Assay
                ParentIdentifier = "assay-1"
                TableName = "metabolite_extraction"
            }

            let result =
                fromLoadedArc
                    {
                        LoadedTable = location
                        IncludePreviousContext = false
                    }
                    arc

            let inputByName name =
                result.Model.InputSets
                |> Map.toSeq
                |> Seq.map snd
                |> Seq.find (fun set -> set.Name = name)

            let valueIdsForHeader headerName set =
                set.PropertyValueIds
                |> List.filter (fun id -> result.Model.PropertyValues.[id].Header.Category.Name = headerName)

            let cam01 = inputByName "CAM_01"
            let cam02 = inputByName "CAM_02"
            let cam01BufferIds = valueIdsForHeader "Extraction buffer" cam01
            let cam02BufferIds = valueIdsForHeader "Extraction buffer" cam02
            let cam01AmountIds = valueIdsForHeader "Biosource amount" cam01
            let cam02AmountIds = valueIdsForHeader "Biosource amount" cam02

            Expect.equal cam01BufferIds.Length 1 "The first row should reference one extraction buffer value."
            Expect.equal cam02BufferIds.Length 1 "The second row should reference one extraction buffer value."

            Expect.equal
                cam01BufferIds.Head
                cam02BufferIds.Head
                "Equal loaded values from generated row process names should share one property value id."

            Expect.equal cam01AmountIds.Length 1 "The first row should reference one biosource amount value."
            Expect.equal cam02AmountIds.Length 1 "The second row should reference one biosource amount value."

            Expect.isFalse
                (cam01AmountIds.Head = cam02AmountIds.Head)
                "Different values for the same property header should remain distinct property values."

            let sharedBuffer = result.Model.PropertyValues.[cam01BufferIds.Head]

            let sharedBufferAnchor =
                assertRealOriginProcess (Some "metabolite_extraction") (Some "metabolite_extraction_0") sharedBuffer

            Expect.equal
                sharedBufferAnchor.ProcessName
                (Some "metabolite_extraction")
                "Generated row process suffixes should not leak into the normalized property source process."

            Expect.equal
                sharedBufferAnchor.ProcessId
                (Some "metabolite_extraction_0")
                "The representative ARCtrl row process identity should be preserved separately from the process name."

        testCase "reuses identical previous-context values across generated row process names"
        <| fun _ ->
            let result =
                fromLoadedArc
                    {
                        LoadedTable = gasChromatographyLoadedTableLocation
                        IncludePreviousContext = true
                    }
                    (arcFixtureWithRepeatedPreviousProcessRows ())

            let inputByName name =
                result.Model.InputSets
                |> Map.toSeq
                |> Seq.map snd
                |> Seq.find (fun set -> set.Name = name)

            let valueIdsForHeader headerName set =
                set.PropertyValueIds
                |> List.filter (fun id -> result.Model.PropertyValues.[id].Header.Category.Name = headerName)

            let db23 = inputByName "DB23"
            let db24 = inputByName "DB24"
            let db23BufferIds = valueIdsForHeader "Extraction buffer" db23
            let db24BufferIds = valueIdsForHeader "Extraction buffer" db24
            let db23AmountIds = valueIdsForHeader "Biosource amount" db23
            let db24AmountIds = valueIdsForHeader "Biosource amount" db24

            Expect.equal db23BufferIds.Length 1 "The first loaded input should inherit one extraction buffer value."
            Expect.equal db24BufferIds.Length 1 "The second loaded input should inherit one extraction buffer value."

            Expect.equal
                db23BufferIds.Head
                db24BufferIds.Head
                "Equal previous-context values should share one property value id across loaded inputs."

            Expect.equal db23AmountIds.Length 1 "The first loaded input should inherit one biosource amount value."
            Expect.equal db24AmountIds.Length 1 "The second loaded input should inherit one biosource amount value."

            Expect.isFalse
                (db23AmountIds.Head = db24AmountIds.Head)
                "Different previous-context values for the same property header should remain distinct."

            let sharedBuffer = result.Model.PropertyValues.[db23BufferIds.Head]

            let sharedBufferAnchor =
                assertRealOriginProcess (Some "metabolite_extraction") (Some "metabolite_extraction_0") sharedBuffer

            Expect.equal
                sharedBufferAnchor.OutputNames
                [ "DB23"; "DB24" ]
                "A shared previous-context value should remember every loaded input it was attached through."

        testCase "collapsed previous-context duplicates keep one representative writeback slot"
        <| fun _ ->
            let result =
                fromLoadedArc
                    {
                        LoadedTable = loadedTable
                        IncludePreviousContext = true
                    }
                    (arcFixtureWithDuplicatePreviousColumns ())

            let sampleA =
                result.Model.InputSets
                |> Map.toSeq
                |> Seq.map snd
                |> Seq.find (fun set -> set.Name = "sample-a")

            let organism =
                sampleA.PropertyValueIds
                |> List.map (fun id -> result.Model.PropertyValues.[id])
                |> List.find (fun value -> value.Header.Category.Name = "Organism")

            let location = result.Index.PropertyValueLocations.[organism.Id]

            Expect.equal
                location.Table.TableName
                "study-table"
                "Collapsed previous-context duplicates should keep a representative source location."

        testCase "keeps ARCtrl table locations in the sidecar index"
        <| fun _ ->
            let result = convertWithPreviousContext ()

            let sampleA =
                result.Model.InputSets
                |> Map.toSeq
                |> Seq.map snd
                |> Seq.find (fun set -> set.Name = "sample-a")

            let endpointLocation = result.Index.EndpointLocations.[sampleA.Id]

            Expect.equal endpointLocation.Table.Scope ArcTableScope.Assay "Loaded endpoint should remember assay scope."

            Expect.equal
                endpointLocation.Table.ParentIdentifier
                "assay-1"
                "Loaded endpoint should remember assay identifier."

            Expect.equal endpointLocation.Table.TableName "assay-table" "Loaded endpoint should remember table name."
            Expect.equal endpointLocation.Name "sample-a" "Endpoint location should keep the actual loaded input name."

        testCase "converts an input-only loaded table into loaded inputs without fake outputs or connections"
        <| fun _ ->
            let assay =
                ArcAssay.create (identifier = "assay-1", tables = ResizeArray [ inputOnlyAssayTable () ])

            let arc =
                ARC(identifier = "arc-1", studies = ResizeArray [], assays = ResizeArray [ assay ])

            let location: ArcTableLocation = {
                Scope = ArcTableScope.Assay
                ParentIdentifier = "assay-1"
                TableName = "input-only-table"
            }

            let result =
                fromLoadedArc
                    {
                        LoadedTable = location
                        IncludePreviousContext = false
                    }
                    arc

            let inputNames =
                result.Model.InputSets
                |> Map.toList
                |> List.map (fun (_, set) -> set.Name)
                |> List.sort

            Expect.equal inputNames [ "sample-a"; "sample-b" ] "Real input cells should still load as first-class sets."
            Expect.equal result.Model.OutputSets.Count 0 "Missing output columns must not create synthetic output sets."
            Expect.equal result.Model.Connections.Count 0 "One-sided loaded tables must not synthesize connections."

        testCase "converts an output-only loaded table into loaded outputs without fake inputs or connections"
        <| fun _ ->
            let assay =
                ArcAssay.create (identifier = "assay-1", tables = ResizeArray [ outputOnlyAssayTable () ])

            let arc =
                ARC(identifier = "arc-1", studies = ResizeArray [], assays = ResizeArray [ assay ])

            let location: ArcTableLocation = {
                Scope = ArcTableScope.Assay
                ParentIdentifier = "assay-1"
                TableName = "output-only-table"
            }

            let result =
                fromLoadedArc
                    {
                        LoadedTable = location
                        IncludePreviousContext = false
                    }
                    arc

            let outputNames =
                result.Model.OutputSets
                |> Map.toList
                |> List.map (fun (_, set) -> set.Name)
                |> List.sort

            Expect.equal
                outputNames
                [ "extract-a"; "extract-b" ]
                "Real output cells should still load as first-class sets."

            Expect.equal result.Model.InputSets.Count 0 "Missing input columns must not create synthetic input sets."
            Expect.equal result.Model.Connections.Count 0 "One-sided loaded tables must not synthesize connections."

        testCase "missing table lookup surfaces the ARCtrl exception instead of a converter-owned error DU"
        <| fun _ ->
            let missing = {
                Scope = ArcTableScope.Assay
                ParentIdentifier = "assay-1"
                TableName = "missing-table"
            }

            Expect.throws
                (fun () ->
                    fromLoadedArc
                        {
                            LoadedTable = missing
                            IncludePreviousContext = false
                        }
                        (arcFixture ())
                    |> ignore
                )
                "Missing table lookup should be delegated to ARCtrl."
    ]
