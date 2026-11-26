module ProvenanceGroupingTests

#if FABLE_COMPILER
open Fable.Mocha
#else
open Expecto
#endif

open Arcitisa.Components.Page.ProvenanceGrouping
open Arcitisa.Components.Shared.ProvenanceGrouping.Types
open Arcitisa.Components.Shared.ProvenanceGrouping.Grouping
open Arcitisa.Components.Shared.ProvenanceGrouping.Edit
open Arcitisa.Components.Shared.ProvenanceGrouping.Fixtures
open Arcitisa.Components.Shared.ProvenanceGrouping.Session

module Fixture = Arcitisa.Components.Shared.ProvenanceGrouping.Fixtures

let private sourceRef (tableName: string) : ProvenanceSourceRef = Fixture.source tableName tableName

let private propertyKey (tableName: string) (header: ProvenancePropertyHeader) : ProvenancePropertyKey = {
    Header = header
    OriginSource = sourceRef tableName
}

let private propertyKeyIn (model: ProvenanceModel) (header: ProvenancePropertyHeader) : ProvenancePropertyKey =
    model.PropertyValues
    |> Map.toList
    |> List.map snd
    |> List.tryFind (fun propertyValue -> propertyValue.Header = header)
    |> Option.map ProvenancePropertyValue.propertyKey
    |> Option.defaultValue {
        Header = header
        OriginSource = model.Source
    }

let private pendingModelSource = sourceRef "__pending-model-source__"

let private anchor
    (tableName: string)
    (processName: ProvenanceProcessName option)
    (header: ProvenancePropertyHeader)
    (inputNames: string list)
    (outputNames: string list)
    : ProvenanceWritebackAnchor =
    Fixture.anchor (sourceRef tableName) processName header inputNames outputNames

let private anchorOfOrigin (origin: ProvenancePropertyOrigin) : ProvenanceWritebackAnchor =
    match origin with
    | ProvenancePropertyOrigin.Real anchor
    | ProvenancePropertyOrigin.Virtual anchor -> anchor

let private replaceOriginSource
    (oldSource: ProvenanceSourceRef)
    (newSource: ProvenanceSourceRef)
    (origin: ProvenancePropertyOrigin)
    : ProvenancePropertyOrigin =
    let replace (anchor: ProvenanceWritebackAnchor) : ProvenanceWritebackAnchor =
        if anchor.Source.Id = oldSource.Id then
            { anchor with Source = newSource }
        else
            anchor

    match origin with
    | ProvenancePropertyOrigin.Real anchor -> ProvenancePropertyOrigin.Real(replace anchor)
    | ProvenancePropertyOrigin.Virtual anchor -> ProvenancePropertyOrigin.Virtual(replace anchor)

let private propertyValue
    id
    (header: ProvenancePropertyHeader)
    (value: ProvenanceValue)
    (unit: ProvenanceTerm option)
    (source: ProvenanceWritebackAnchor option)
    : ProvenancePropertyValue =
    let origin =
        source
        |> Option.map ProvenancePropertyOrigin.Real
        |> Option.defaultValue (ProvenancePropertyOrigin.Real(Fixture.anchor pendingModelSource None header [] []))

    Fixture.propertyValue id header value unit origin

let private inputSet id (tableName: string) header name propertyValueIds : ProvenanceSet =
    Fixture.inputSet id (sourceRef tableName) header name propertyValueIds

let private outputSet id (tableName: string) header name propertyValueIds : ProvenanceSet =
    Fixture.outputSet id (sourceRef tableName) header name propertyValueIds

let private connection id (tableName: string) processName inputSetId outputSetId : ProvenanceConnection =
    Fixture.connection id (sourceRef tableName) processName inputSetId outputSetId

let private model
    (tableName: string)
    (propertyValues: ProvenancePropertyValue list)
    (inputSets: ProvenanceSet list)
    (outputSets: ProvenanceSet list)
    (connections: ProvenanceConnection list)
    : ProvenanceModel =
    let modelSource = sourceRef tableName

    let propertyValues =
        propertyValues
        |> List.map (fun value -> {
            value with
                Origin = value.Origin |> replaceOriginSource pendingModelSource modelSource
        })

    Fixture.model modelSource propertyValues inputSets outputSets connections

(*
    The helpers above keep older behavior-focused tests compact while mapping
    their table-name shorthand onto the explicit source/origin model.
*)

let typeTests =
    testList "Types" [
        testCase "property identity uses header and overall origin source only"
        <| fun _ ->
            let temperature = propertyHeader FixtureKinds.parameterProperty "Temperature"
            let growth = sourceRef "growth"
            let cultivation = sourceRef "cultivation"

            let value id origin =
                Fixture.propertyValue id temperature (ProvenanceValue.Text "20") None origin

            let growthAnchor = Fixture.anchor growth (Some "process-a") temperature [ "A" ] []

            let sameReal = value "real" (ProvenancePropertyOrigin.Real growthAnchor)

            let sameVirtual =
                value
                    "virtual"
                    (ProvenancePropertyOrigin.Virtual {
                        growthAnchor with
                            ProcessId = Some "new-process-id"
                            ProcessName = Some "process-b"
                            InputNames = [ "B" ]
                    })

            let otherOrigin =
                value "other" (ProvenancePropertyOrigin.Real(Fixture.anchor cultivation None temperature [ "A" ] []))

            Expect.equal
                (ProvenancePropertyValue.propertyKey sameReal)
                (ProvenancePropertyValue.propertyKey sameVirtual)
                "Real/virtual form and process metadata must not split one property origin."

            Expect.notEqual
                (ProvenancePropertyValue.propertyKey sameReal)
                (ProvenancePropertyValue.propertyKey otherOrigin)
                "The same header from another overall source is a different property."

        testCase "loaded input set carries the actual input name"
        <| fun _ ->
            let species = propertyHeader FixtureKinds.characteristicProperty "Species"
            let assaySource = sourceRef "assay-table"

            let inputSet = {
                Id = "input-a"
                Source = assaySource
                Header = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"
                Name = "Input A"
                PropertyValueIds = [ "pv-species-a" ]
                InheritedPropertyValueIds = Map.empty
            }

            let propertyValue = {
                Id = "pv-species-a"
                Header = species
                Value = ProvenanceValue.Text "Arabidopsis"
                Unit = None
                Origin =
                    ProvenancePropertyOrigin.Real(
                        anchor "previous-table" (Some "previous-process") species [ "Ancestor A" ] []
                    )
            }

            Expect.equal inputSet.Name "Input A" "Loaded input name should live on the set."
            Expect.equal inputSet.PropertyValueIds [ propertyValue.Id ] "Set should point to property value occurrence."

            Expect.equal
                (anchorOfOrigin propertyValue.Origin).Source.Name
                "previous-table"
                "Collapsed value should keep writeback table metadata."

        testCase "inherited property helpers replace and remove connection-specific pointers"
        <| fun _ ->
            let assaySource = sourceRef "assay-table"

            let inputSet = {
                Id = "input-a"
                Source = assaySource
                Header = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"
                Name = "Input A"
                PropertyValueIds = []
                InheritedPropertyValueIds = Map.empty
            }

            let inherited =
                inputSet
                |> ProvenanceSet.inheritPropertyValueIds "connection-a" [ "pv-a"; "pv-a"; "pv-b" ]

            Expect.equal
                inherited.InheritedPropertyValueIds.["connection-a"]
                [ "pv-a"; "pv-b" ]
                "Inherited property IDs should be distinct per connection."

            let replaced = inherited |> ProvenanceSet.inheritPropertyValueIds "connection-a" []

            Expect.isFalse
                (replaced.InheritedPropertyValueIds.ContainsKey "connection-a")
                "Empty inherited IDs should remove the connection entry."

            let removed =
                inherited |> ProvenanceSet.removeInheritedPropertyValueIds "connection-a"

            Expect.isFalse
                (removed.InheritedPropertyValueIds.ContainsKey "connection-a")
                "Explicit removal should drop only the requested connection entry."

        testCase "adapter endpoint and property kinds keep the editor model source agnostic"
        <| fun _ ->
            let customEndpointKind =
                ProvenanceKind.create "external:endpoint:plate-well" "Plate well"

            let customPropertyKind =
                ProvenanceKind.create "external:property:quality-score" "Quality Score"

            let customEndpoint = ioHeader customEndpointKind "Input [Plate well]"
            let customProperty = propertyHeader customPropertyKind "Quality Score"

            let built =
                model
                    "external-process-table"
                    [
                        propertyValue "pv-quality" customProperty (ProvenanceValue.Float 0.98) None None
                    ]
                    [
                        inputSet "input-well-a1" "external-process-table" customEndpoint "A1" [ "pv-quality" ]
                    ] [] []

            let input = built.InputSets.["input-well-a1"]
            let value = built.PropertyValues.["pv-quality"]

            Expect.equal input.Header.Kind customEndpointKind "Endpoint kind should not require an ISA IO type."
            Expect.equal value.Header.Kind customPropertyKind "Property kind should not require an ISA property family."
            Expect.equal input.Name "A1" "Custom process endpoints should still use ProvenanceSet.Name for display."
    ]

let modelTests =
    testList "Model" [
        testCase "direct model builder preserves loaded names and repeated distinct property values"
        <| fun _ ->
            let species = propertyHeader FixtureKinds.characteristicProperty "Species"
            let replicate = propertyHeader FixtureKinds.parameterProperty "Replicate"
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"
            let outputHeader = ioHeader FixtureKinds.sampleEndpoint "Output [Sample Name]"

            let built =
                model
                    "assay-table"
                    [
                        propertyValue
                            "pv-species-a"
                            species
                            (ProvenanceValue.Text "Arabidopsis")
                            None
                            (Some(anchor "previous-table" (Some "previous-process") species [ "Ancestor A" ] []))
                        propertyValue
                            "pv-rep-1"
                            replicate
                            (ProvenanceValue.Text "1")
                            None
                            (Some(anchor "assay-table" (Some "assay-process") replicate [ "Input A" ] [ "Output A" ]))
                        propertyValue
                            "pv-rep-2"
                            replicate
                            (ProvenanceValue.Text "2")
                            None
                            (Some(anchor "assay-table" (Some "assay-process") replicate [ "Input A" ] [ "Output A" ]))
                    ]
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" [
                            "pv-species-a"
                            "pv-rep-1"
                            "pv-rep-2"
                        ]
                    ] [
                        outputSet "output-a" "assay-table" outputHeader "Output A" [ "pv-rep-1"; "pv-rep-2" ]
                    ] [
                        connection "connection-a" "assay-table" (Some "assay-process") "input-a" "output-a"
                    ]

            Expect.equal built.InputSets.["input-a"].Name "Input A" "Direct model should keep the loaded input name."

            Expect.equal
                built.OutputSets.["output-a"].Name
                "Output A"
                "Direct model should keep the loaded output name."

            Expect.equal built.PropertyValues.Count 3 "Distinct repeated values should remain separate model values."

        testCase "direct model builder keeps previous-context anchors without previous first-class sets"
        <| fun _ ->
            let previousTreatment =
                propertyHeader FixtureKinds.characteristicProperty "Previous Treatment"

            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"

            let built =
                model
                    "assay-table"
                    [
                        propertyValue
                            "pv-previous-treatment-a"
                            previousTreatment
                            (ProvenanceValue.Text "Drought")
                            None
                            (Some(
                                anchor "previous-study-table" (Some "previous-process") previousTreatment [
                                    "Ancestor A"
                                ] []
                            ))
                    ]
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" [ "pv-previous-treatment-a" ]
                    ] [] []

            let value = built.PropertyValues.["pv-previous-treatment-a"]
            Expect.equal built.InputSets.Count 1 "Only loaded sets should be first-class sets."

            Expect.equal
                (anchorOfOrigin value.Origin).Source.Name
                "previous-study-table"
                "Collapsed previous-context values should keep their source table."
    ]

let private validModel () =
    let species = propertyHeader FixtureKinds.characteristicProperty "Species"
    let replicate = propertyHeader FixtureKinds.parameterProperty "Replicate"
    let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"
    let outputHeader = ioHeader FixtureKinds.sampleEndpoint "Output [Sample Name]"

    model
        "assay-table"
        [
            propertyValue
                "pv-species-arabidopsis-a"
                species
                (ProvenanceValue.Text "Arabidopsis")
                None
                (Some(anchor "assay-table" (Some "assay-process") species [ "Input A" ] []))
            propertyValue
                "pv-species-arabidopsis-b"
                species
                (ProvenanceValue.Text "Arabidopsis")
                None
                (Some(anchor "assay-table" (Some "assay-process") species [ "Input B" ] []))
            propertyValue
                "pv-rep-output-b-1"
                replicate
                (ProvenanceValue.Text "1")
                None
                (Some(anchor "assay-table" (Some "assay-process") replicate [ "Input A" ] [ "Output B" ]))
            propertyValue
                "pv-rep-output-b-2"
                replicate
                (ProvenanceValue.Text "2")
                None
                (Some(anchor "assay-table" (Some "assay-process") replicate [ "Input B" ] [ "Output B" ]))
        ]
        [
            inputSet "input-a" "assay-table" inputHeader "Input A" [ "pv-species-arabidopsis-a" ]
            inputSet "input-b" "assay-table" inputHeader "Input B" [ "pv-species-arabidopsis-b" ]
            inputSet "input-c" "assay-table" inputHeader "Input C" []
        ] [
            outputSet "output-a" "assay-table" outputHeader "Output A" []
            outputSet "output-b" "assay-table" outputHeader "Output B" [ "pv-rep-output-b-1"; "pv-rep-output-b-2" ]
            outputSet "output-c" "assay-table" outputHeader "Output C" []
        ] [
            connection "connection-a" "assay-table" (Some "assay-process") "input-a" "output-a"
            connection "connection-b" "assay-table" (Some "assay-process") "input-a" "output-b"
            connection "connection-c" "assay-table" (Some "assay-process") "input-b" "output-b"
            connection "connection-d" "assay-table" (Some "assay-process") "input-c" "output-c"
        ]

let groupingTests =
    testList "Grouping" [
        testCase "no grouping displays each loaded set by name"
        <| fun _ ->
            let model = validModel ()
            let groups = displayGroups model ProvenanceSide.Input []

            Expect.equal
                (groups |> List.map (fun group -> group.Members.Head.Name))
                [ "Input A"; "Input B"; "Input C" ]
                "No grouping should preserve loaded input names."

        testCase "multi-value grouping uses the complete value set as one group key"
        <| fun _ ->
            let species = propertyHeader FixtureKinds.characteristicProperty "Species"
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"

            let built =
                model
                    "assay-table"
                    [
                        propertyValue
                            "pv-input-a-species-chlamy"
                            species
                            (ProvenanceValue.Text "Chlamydomonas")
                            None
                            None
                        propertyValue
                            "pv-input-b-species-arabidopsis"
                            species
                            (ProvenanceValue.Text "Arabidopsis")
                            None
                            None
                        propertyValue
                            "pv-input-c-species-arabidopsis"
                            species
                            (ProvenanceValue.Text "Arabidopsis")
                            None
                            None
                        propertyValue
                            "pv-input-c-species-chlamy"
                            species
                            (ProvenanceValue.Text "Chlamydomonas")
                            None
                            None
                    ]
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" [ "pv-input-a-species-chlamy" ]
                        inputSet "input-b" "assay-table" inputHeader "Input B" [ "pv-input-b-species-arabidopsis" ]
                        inputSet "input-c" "assay-table" inputHeader "Input C" [
                            "pv-input-c-species-arabidopsis"
                            "pv-input-c-species-chlamy"
                        ]
                    ] [] []

            let groups =
                displayGroups built ProvenanceSide.Input [ propertyKeyIn built species ]

            let membersByTitle =
                groups
                |> List.map (fun group ->
                    let title =
                        group.GroupingValues
                        |> List.map (fun value ->
                            match value.Value with
                            | ProvenanceValue.Text text -> text
                            | other -> string other
                        )
                        |> List.sort
                        |> String.concat ", "

                    title, group.Members |> List.map (fun member' -> member'.SetId) |> List.sort
                )
                |> Map.ofList

            Expect.equal
                (membersByTitle |> Map.toList |> List.map fst)
                [
                    "Arabidopsis"
                    "Arabidopsis, Chlamydomonas"
                    "Chlamydomonas"
                ]
                "Grouping by a multi-valued property should create one group for the complete value set."

            Expect.isTrue
                (groups
                 |> List.exists (fun group -> group.Id = "input:Species=Arabidopsis | Chlamydomonas"))
                "A multi-valued group id should print the grouping key once and join values with ' | '."

            Expect.equal
                membersByTitle.["Arabidopsis"]
                [ "input-b" ]
                "The multi-valued set must not appear in the Arabidopsis-only group."

            Expect.equal
                membersByTitle.["Chlamydomonas"]
                [ "input-a" ]
                "The multi-valued set must not appear in the Chlamydomonas-only group."

            Expect.equal
                membersByTitle.["Arabidopsis, Chlamydomonas"]
                [ "input-c" ]
                "The multi-valued set should appear in its combined value-set group."

        testCase "grouped members retain non-grouping property values for editing"
        <| fun _ ->
            let model = sampleModel ()
            let species = propertyHeader FixtureKinds.characteristicProperty "Species"

            let groups =
                displayGroups model ProvenanceSide.Input [ propertyKeyIn model species ]

            let inputA =
                groups
                |> List.collect (fun group -> group.Members)
                |> List.find (fun member' -> member'.SetId = "input-a")

            Expect.isTrue
                (inputA.PropertyValueIds |> List.contains "pv-input-a-temperature")
                "Grouping by Species must not hide Temperature from the member editing surface."

        testCase "outputs expose input properties inherited through current loaded connections"
        <| fun _ ->
            let species = propertyHeader FixtureKinds.characteristicProperty "Species"
            let temperature = propertyHeader FixtureKinds.parameterProperty "Temperature"
            let analysis = propertyHeader FixtureKinds.parameterProperty "Analysis"
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"
            let outputHeader = ioHeader FixtureKinds.sampleEndpoint "Output [Sample Name]"

            let built =
                model
                    "assay-table"
                    [
                        propertyValue "pv-input-a-species" species (ProvenanceValue.Text "Arabidopsis") None None
                        propertyValue "pv-input-b-temperature" temperature (ProvenanceValue.Text "12 C") None None
                        propertyValue "pv-output-a-analysis" analysis (ProvenanceValue.Text "LC-MS") None None
                    ]
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" [ "pv-input-a-species" ]
                        inputSet "input-b" "assay-table" inputHeader "Input B" [ "pv-input-b-temperature" ]
                    ] [
                        outputSet "output-a" "assay-table" outputHeader "Output A" [ "pv-output-a-analysis" ]
                    ] [
                        connection "connection-a" "assay-table" None "input-a" "output-a"
                        connection "connection-b" "assay-table" None "input-b" "output-a"
                    ]

            let outputA =
                displayGroups built ProvenanceSide.Output []
                |> List.collect (fun group -> group.Members)
                |> List.find (fun member' -> member'.SetId = "output-a")

            Expect.equal
                (outputA.PropertyValueIds |> List.sort)
                [
                    "pv-input-a-species"
                    "pv-input-b-temperature"
                    "pv-output-a-analysis"
                ]
                "An output should expose its own properties plus properties inherited from all directly connected loaded inputs."

        testCase "output inheritance follows the current loaded connection set"
        <| fun _ ->
            let species = propertyHeader FixtureKinds.characteristicProperty "Species"
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"
            let outputHeader = ioHeader FixtureKinds.sampleEndpoint "Output [Sample Name]"

            let build connections =
                model
                    "assay-table"
                    [
                        propertyValue "pv-input-a-species" species (ProvenanceValue.Text "Arabidopsis") None None
                        propertyValue "pv-input-b-species" species (ProvenanceValue.Text "Chlamydomonas") None None
                    ]
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" [ "pv-input-a-species" ]
                        inputSet "input-b" "assay-table" inputHeader "Input B" [ "pv-input-b-species" ]
                    ]
                    [
                        outputSet "output-a" "assay-table" outputHeader "Output A" []
                    ]
                    connections

            let withOneConnection =
                build [
                    connection "connection-a" "assay-table" None "input-a" "output-a"
                ]

            let withTwoConnections =
                build [
                    connection "connection-a" "assay-table" None "input-a" "output-a"
                    connection "connection-b" "assay-table" None "input-b" "output-a"
                ]

            let inheritedIds model =
                displayGroups model ProvenanceSide.Output []
                |> List.collect (fun group -> group.Members)
                |> List.find (fun member' -> member'.SetId = "output-a")
                |> fun member' -> member'.PropertyValueIds |> List.sort

            Expect.equal
                (inheritedIds withOneConnection)
                [ "pv-input-a-species" ]
                "Only properties from currently connected inputs should be inherited."

            Expect.equal
                (inheritedIds withTwoConnections)
                [ "pv-input-a-species"; "pv-input-b-species" ]
                "Adding a connection should add that input's properties to the output's effective properties."

        testCase "inputs expose output properties inherited through current loaded connections"
        <| fun _ ->
            let analysis = propertyHeader FixtureKinds.parameterProperty "Analysis"
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"
            let outputHeader = ioHeader FixtureKinds.sampleEndpoint "Output [Sample Name]"

            let built =
                model
                    "assay-table"
                    [
                        propertyValue "pv-output-a-analysis" analysis (ProvenanceValue.Text "LC-MS") None None
                    ]
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" []
                    ] [
                        outputSet "output-a" "assay-table" outputHeader "Output A" [ "pv-output-a-analysis" ]
                    ] [
                        connection "connection-a" "assay-table" None "input-a" "output-a"
                    ]

            Expect.equal
                (ProvenanceSet.effectivePropertyValueIds built.InputSets.["input-a"])
                [ "pv-output-a-analysis" ]
                "An input should expose properties inherited from its directly connected loaded output."

        testCase "same-layer inheritance spreads transitively across connected endpoints"
        <| fun _ ->
            let analysis = propertyHeader FixtureKinds.parameterProperty "Analysis"
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"
            let outputHeader = ioHeader FixtureKinds.sampleEndpoint "Output [Sample Name]"

            let built =
                model
                    "assay-table"
                    [
                        propertyValue "pv-output-b-analysis" analysis (ProvenanceValue.Text "LC-MS") None None
                    ]
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" []
                    ] [
                        outputSet "output-a" "assay-table" outputHeader "Output A" []
                        outputSet "output-b" "assay-table" outputHeader "Output B" [ "pv-output-b-analysis" ]
                    ] [
                        connection "connection-a" "assay-table" None "input-a" "output-a"
                        connection "connection-b" "assay-table" None "input-a" "output-b"
                    ]

            Expect.equal
                (ProvenanceSet.effectivePropertyValueIds built.InputSets.["input-a"])
                [ "pv-output-b-analysis" ]
                "A value added to one connected output should reach the shared input."

            Expect.equal
                (ProvenanceSet.effectivePropertyValueIds built.OutputSets.["output-a"])
                [ "pv-output-b-analysis" ]
                "A value added to one connected output should reach sibling outputs through the shared input."

            Expect.isFalse
                (built.OutputSets.["output-b"].InheritedPropertyValueIds
                 |> Map.exists (fun _ ids -> ids |> List.contains "pv-output-b-analysis"))
                "A value must never be inherited back onto the endpoint that owns it."

        testCase "refresh preserves inherited entries carried in from upstream connections"
        <| fun _ ->
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"
            let outputHeader = ioHeader FixtureKinds.sampleEndpoint "Output [Sample Name]"
            let modelSource = Fixture.source "layer-2:next-table" "next-table"

            let carriedInput = {
                Fixture.inputSet "carried-input" modelSource inputHeader "Carried Input" [] with
                    InheritedPropertyValueIds = Map.ofList [ "upstream-connection", [ "pv-upstream" ] ]
            }

            let built =
                Fixture.model modelSource [] [ carriedInput ] [
                    Fixture.outputSet "output-a" modelSource outputHeader "Output A" []
                ] [
                    Fixture.connection "local-connection" modelSource None "carried-input" "output-a"
                ]

            Expect.equal
                (built.InputSets.["carried-input"].InheritedPropertyValueIds
                 |> Map.tryFind "upstream-connection")
                (Some [ "pv-upstream" ])
                "Entries keyed by upstream connections must survive a same-layer refresh."

            Expect.equal
                (ProvenanceSet.effectivePropertyValueIds built.OutputSets.["output-a"])
                [ "pv-upstream" ]
                "Carried upstream values should flow on to connected outputs in the current layer."

        testCase "grouping collapses identical equal values for one set into one display member"
        <| fun _ ->
            let replicate = propertyHeader FixtureKinds.parameterProperty "Replicate"
            let outputHeader = ioHeader FixtureKinds.sampleEndpoint "Output [Sample Name]"

            let built =
                model
                    "assay-table"
                    [
                        propertyValue
                            "pv-rep-1a"
                            replicate
                            (ProvenanceValue.Text "1")
                            None
                            (Some(anchor "assay-table" (Some "assay-process") replicate [ "Input A" ] [ "Output A" ]))
                        propertyValue
                            "pv-rep-1b"
                            replicate
                            (ProvenanceValue.Text "1")
                            None
                            (Some(anchor "assay-table" (Some "assay-process") replicate [ "Input A" ] [ "Output A" ]))
                    ]
                    [] [
                        outputSet "output-a" "assay-table" outputHeader "Output A" [ "pv-rep-1a"; "pv-rep-1b" ]
                    ] []

            let groups =
                displayGroups built ProvenanceSide.Output [ propertyKeyIn built replicate ]

            let members = groups |> List.collect (fun group -> group.Members)

            Expect.equal groups.Length 1 "Identical equal values should collapse into one output group."
            Expect.equal members.Length 1 "The output set should appear once in the collapsed group."

            Expect.equal
                members.Head.PropertyValueIds
                [ "pv-rep-1a"; "pv-rep-1b" ]
                "Collapsed display membership should keep all underlying property value IDs."

        testCase "grouping keeps equal text with different units separate"
        <| fun _ ->
            let temperature = propertyHeader FixtureKinds.parameterProperty "Temperature"
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"

            let celsius = term "C"
            let fahrenheit = term "F"

            let built =
                model
                    "assay-table"
                    [
                        propertyValue
                            "pv-temp-c"
                            temperature
                            (ProvenanceValue.Integer 12)
                            (Some celsius)
                            (Some(anchor "assay-table" (Some "assay-process") temperature [ "Input A" ] []))
                        propertyValue
                            "pv-temp-f"
                            temperature
                            (ProvenanceValue.Integer 12)
                            (Some fahrenheit)
                            (Some(anchor "assay-table" (Some "assay-process") temperature [ "Input B" ] []))
                    ]
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" [ "pv-temp-c" ]
                        inputSet "input-b" "assay-table" inputHeader "Input B" [ "pv-temp-f" ]
                    ] [] []

            let groups =
                displayGroups built ProvenanceSide.Input [ propertyKeyIn built temperature ]

            Expect.equal groups.Length 2 "Values with the same scalar value but different units must not collapse."

        testCase "scoped input grouping uses multiple own values as one value-set group"
        <| fun _ ->
            let replicate = propertyHeader FixtureKinds.parameterProperty "Replicate"
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"

            let built =
                model
                    "assay-table"
                    [
                        propertyValue
                            "pv-input-a-rep-1"
                            replicate
                            (ProvenanceValue.Text "1")
                            None
                            (Some(anchor "assay-table" (Some "assay-process") replicate [ "Input A" ] []))
                        propertyValue
                            "pv-input-a-rep-2"
                            replicate
                            (ProvenanceValue.Text "2")
                            None
                            (Some(anchor "assay-table" (Some "assay-process") replicate [ "Input A" ] []))
                    ]
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" [
                            "pv-input-a-rep-1"
                            "pv-input-a-rep-2"
                        ]
                    ] [] []

            let groups =
                displayGroupsForAssignments built ProvenanceSide.Input [
                    {
                        Key = propertyKeyIn built replicate
                        Scope = GroupingScope.Input
                    }
                ]

            let groupedValues =
                groups
                |> List.choose (fun group ->
                    if group.Members |> List.exists (fun member' -> member'.SetId = "input-a") then
                        group.GroupingValues
                        |> List.map (fun groupingValue -> groupingValue.Value)
                        |> List.sort
                        |> Some
                    else
                        None
                )
                |> List.exactlyOne

            Expect.equal
                groupedValues
                [ ProvenanceValue.Text "1"; ProvenanceValue.Text "2" ]
                "An input with multiple values for the same grouping key must appear in one combined value-set group."

        testCase "both-side grouping lets inputs inherit missing values from direct connected outputs"
        <| fun _ ->
            let replicate = propertyHeader FixtureKinds.parameterProperty "Replicate"
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"
            let outputHeader = ioHeader FixtureKinds.sampleEndpoint "Output [Sample Name]"

            let built =
                model
                    "assay-table"
                    [
                        propertyValue
                            "pv-output-a-rep-1"
                            replicate
                            (ProvenanceValue.Text "1")
                            None
                            (Some(anchor "assay-table" (Some "assay-process") replicate [] [ "Output A" ]))
                        propertyValue
                            "pv-output-b-rep-2"
                            replicate
                            (ProvenanceValue.Text "2")
                            None
                            (Some(anchor "assay-table" (Some "assay-process") replicate [] [ "Output B" ]))
                    ]
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" []
                    ] [
                        outputSet "output-a" "assay-table" outputHeader "Output A" [ "pv-output-a-rep-1" ]
                        outputSet "output-b" "assay-table" outputHeader "Output B" [ "pv-output-b-rep-2" ]
                    ] [
                        connection "connection-a" "assay-table" (Some "assay-process") "input-a" "output-a"
                        connection "connection-b" "assay-table" (Some "assay-process") "input-a" "output-b"
                    ]

            let groups =
                displayGroupsForAssignments built ProvenanceSide.Input [
                    {
                        Key = propertyKeyIn built replicate
                        Scope = GroupingScope.Both
                    }
                ]

            let groupedValues =
                groups
                |> List.find (fun group -> group.Members |> List.exists (fun member' -> member'.SetId = "input-a"))
                |> fun group ->
                    group.GroupingValues
                    |> List.map (fun groupingValue -> groupingValue.Value)
                    |> List.sort

            Expect.equal
                groupedValues
                [ ProvenanceValue.Text "1"; ProvenanceValue.Text "2" ]
                "A missing input property should inherit direct connected output values as one combined value-set group."

        testCase "both-side grouping combines input values with connected output values"
        <| fun _ ->
            let replicate = propertyHeader FixtureKinds.parameterProperty "Replicate"
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"
            let outputHeader = ioHeader FixtureKinds.sampleEndpoint "Output [Sample Name]"

            let built =
                model
                    "assay-table"
                    [
                        propertyValue
                            "pv-input-a-rep-3"
                            replicate
                            (ProvenanceValue.Text "3")
                            None
                            (Some(anchor "assay-table" (Some "assay-process") replicate [ "Input A" ] []))
                        propertyValue
                            "pv-output-a-rep-1"
                            replicate
                            (ProvenanceValue.Text "1")
                            None
                            (Some(anchor "assay-table" (Some "assay-process") replicate [] [ "Output A" ]))
                    ]
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" [ "pv-input-a-rep-3" ]
                    ] [
                        outputSet "output-a" "assay-table" outputHeader "Output A" [ "pv-output-a-rep-1" ]
                    ] [
                        connection "connection-a" "assay-table" (Some "assay-process") "input-a" "output-a"
                    ]

            let groups =
                displayGroupsForAssignments built ProvenanceSide.Input [
                    {
                        Key = propertyKeyIn built replicate
                        Scope = GroupingScope.Both
                    }
                ]

            let groupedValues =
                groups
                |> List.find (fun group -> group.Members |> List.exists (fun member' -> member'.SetId = "input-a"))
                |> fun group ->
                    group.GroupingValues
                    |> List.map (fun groupingValue -> groupingValue.Value)
                    |> List.sort

            Expect.equal
                groupedValues
                [ ProvenanceValue.Text "1"; ProvenanceValue.Text "3" ]
                "An input's own values and direct connected output values belong to the same combined group."

        testCase "displayGroups works for input-only loaded models"
        <| fun _ ->
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"

            let built =
                model
                    "assay-table"
                    []
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" []
                    ] [] []

            let groups = displayGroups built ProvenanceSide.Input []

            Expect.equal groups.Length 1 "Input-only models should still render input groups."
            Expect.equal groups.Head.Members.Head.Name "Input A" "The loaded input should still be visible."

        testCase "displayConnections expands to represented loaded set pairs only"
        <| fun _ ->
            let model = validModel ()
            let species = propertyHeader FixtureKinds.characteristicProperty "Species"

            let inputGroups =
                displayGroups model ProvenanceSide.Input [ propertyKeyIn model species ]

            let outputGroups = displayGroups model ProvenanceSide.Output []
            let connections = displayConnections model inputGroups outputGroups

            let representedIds =
                connections
                |> List.collect (fun connection -> connection.ConnectionIds)
                |> List.sort

            Expect.equal
                representedIds
                [
                    "connection-a"
                    "connection-b"
                    "connection-c"
                    "connection-d"
                ]
                "Display lines should represent real loaded connections only."

        testCase "displayConnections returns no lines when one side is absent"
        <| fun _ ->
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"

            let built =
                model
                    "assay-table"
                    []
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" []
                    ] [] []

            let inputGroups = displayGroups built ProvenanceSide.Input []
            let outputGroups = displayGroups built ProvenanceSide.Output []
            let connections = displayConnections built inputGroups outputGroups

            Expect.isEmpty connections "One-sided models must not invent display connections."
    ]

let private sourceOriginModel () =
    let species = propertyHeader FixtureKinds.characteristicProperty "Species"
    let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"
    let outputHeader = ioHeader FixtureKinds.sampleEndpoint "Output [Sample Name]"

    let pvId = "pv-species-a"
    let setAId = "input-a"
    let setBId = "output-a"
    let connId = "connection-a"

    model
        "assay-table"
        [
            propertyValue
                pvId
                species
                (ProvenanceValue.Text "Arabidopsis")
                None
                (Some(anchor "assay-table" (Some "assay-process") species [ "Input A" ] [ "Output A" ]))
        ]
        [
            inputSet setAId "assay-table" inputHeader "Input A" [ pvId ]
        ] [
            outputSet setBId "assay-table" outputHeader "Output A" [ pvId ]
        ] [
            connection connId "assay-table" (Some "assay-process") setAId setBId
        ]

let editTests =
    testList "Edit" [
        testCase "updatePropertyValue preserves collapsed source anchor"
        <| fun _ ->
            let model = validModel ()

            match updatePropertyValue "pv-species-arabidopsis-a" (ProvenanceValue.Text "A. thaliana") None model with
            | Ok(nextModel, [ ProvenanceTablePatch.UpdatePropertyValue(propertyValueId, source, _, newValue, _) ]) ->
                Expect.equal propertyValueId "pv-species-arabidopsis-a" "Patch should identify edited occurrence."
                Expect.equal source.InputNames [ "Input A" ] "Patch should preserve source input name."
                Expect.equal newValue (ProvenanceValue.Text "A. thaliana") "Patch should carry edited value."

                Expect.equal
                    nextModel.PropertyValues.["pv-species-arabidopsis-a"].Value
                    (ProvenanceValue.Text "A. thaliana")
                    "Model should update the occurrence."
            | other -> failwithf "Expected one UpdatePropertyValue patch, got %A" other

        testCase "createLoadedPropertyValue adds occurrence to target loaded set"
        <| fun _ ->
            let model = validModel ()
            let treatment = propertyHeader FixtureKinds.characteristicProperty "Treatment"

            let command = {
                Target = ProvenancePropertyTarget.InputSets [ "input-c" ]
                CopiedFrom = None
                Header = treatment
                Value = ProvenanceValue.Text "Drought"
                Unit = None
            }

            match createLoadedPropertyValue command model with
            | Ok(nextModel, [ ProvenanceTablePatch.AddLoadedPropertyValue(target, copiedFrom, header, value, _) ]) ->
                Expect.equal
                    target
                    (ProvenancePropertyTarget.InputSets [ "input-c" ])
                    "Patch should target the loaded input set."

                Expect.equal copiedFrom None "New value should not be copied from another occurrence."
                Expect.equal header treatment "Patch should carry the requested header."
                Expect.equal value (ProvenanceValue.Text "Drought") "Patch should carry the requested value."

                Expect.equal
                    (nextModel.InputSets.["input-c"].PropertyValueIds.Length)
                    1
                    "Target input set should point to the new value."
            | other -> failwithf "Expected one AddLoadedPropertyValue patch, got %A" other

        testCase "createLoadedPropertyValue on an output reaches connected inputs through inheritance"
        <| fun _ ->
            let model = validModel ()
            let treatment = propertyHeader FixtureKinds.characteristicProperty "Treatment"

            let command = {
                Target = ProvenancePropertyTarget.OutputSets [ "output-a" ]
                CopiedFrom = None
                Header = treatment
                Value = ProvenanceValue.Text "Drought"
                Unit = None
            }

            match createLoadedPropertyValue command model with
            | Ok(nextModel, [ ProvenanceTablePatch.AddLoadedPropertyValue _ ]) ->
                let addedId =
                    nextModel.OutputSets.["output-a"].PropertyValueIds
                    |> List.find (fun id -> not (model.OutputSets.["output-a"].PropertyValueIds |> List.contains id))

                Expect.isTrue
                    (ProvenanceSet.effectivePropertyValueIds nextModel.InputSets.["input-a"]
                     |> List.contains addedId)
                    "A value added to an output should be inherited by its connected inputs."

                Expect.isFalse
                    (nextModel.InputSets.["input-a"].PropertyValueIds |> List.contains addedId)
                    "The connected input must inherit the value, not own it."
            | other -> failwithf "Expected one AddLoadedPropertyValue patch, got %A" other

        testCase "copyPropertyValueToLoadedTarget copies previous value to existing loaded connection"
        <| fun _ ->
            let model = validModel ()

            match
                copyPropertyValueToLoadedTarget
                    "pv-species-arabidopsis-a"
                    (ProvenancePropertyTarget.Connections [ "connection-d" ])
                    model
            with
            | Ok(nextModel, [ ProvenanceTablePatch.AddLoadedPropertyValue(target, copiedFrom, _, value, _) ]) ->
                Expect.equal
                    target
                    (ProvenancePropertyTarget.Connections [ "connection-d" ])
                    "Patch should target the existing loaded connection."

                Expect.equal
                    copiedFrom
                    (Some "pv-species-arabidopsis-a")
                    "Patch should preserve copied source occurrence."

                Expect.equal value (ProvenanceValue.Text "Arabidopsis") "Patch should copy the value."

                Expect.isTrue
                    (nextModel.InputSets.["input-c"].PropertyValueIds.Length > model.InputSets.["input-c"]
                        .PropertyValueIds.Length)
                    "Connection input set should point to the copied loaded occurrence."

                Expect.isTrue
                    (nextModel.OutputSets.["output-c"].PropertyValueIds.Length > model.OutputSets.["output-c"]
                        .PropertyValueIds.Length)
                    "Connection output set should point to the copied loaded occurrence."
            | other -> failwithf "Expected one AddLoadedPropertyValue patch, got %A" other

        testCase "createLoadedPropertyValue is a no-op when an identical loaded value already exists on the same target"
        <| fun _ ->
            let species = propertyHeader FixtureKinds.characteristicProperty "Species"
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"

            let built =
                model
                    "assay-table"
                    [
                        propertyValue
                            "pv-species-a"
                            species
                            (ProvenanceValue.Text "Arabidopsis")
                            None
                            (Some(anchor "assay-table" (Some "assay-process") species [ "Input A" ] []))
                    ]
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" [ "pv-species-a" ]
                    ] [] []

            match
                createLoadedPropertyValue
                    {
                        Target = ProvenancePropertyTarget.InputSets [ "input-a" ]
                        CopiedFrom = None
                        Header = species
                        Value = ProvenanceValue.Text "Arabidopsis"
                        Unit = None
                    }
                    built
            with
            | Ok(nextModel, []) ->
                Expect.equal nextModel built "An exact duplicate loaded value should not create a second model value."
            | other -> failwithf "Expected a no-op duplicate create, got %A" other

        testCase "copyPropertyValueToLoadedTarget is a no-op when the target already has an identical value"
        <| fun _ ->
            let species = propertyHeader FixtureKinds.characteristicProperty "Species"
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"

            let built =
                model
                    "assay-table"
                    [
                        propertyValue
                            "pv-species-a"
                            species
                            (ProvenanceValue.Text "Arabidopsis")
                            None
                            (Some(anchor "assay-table" (Some "assay-process") species [ "Input A" ] []))
                    ]
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" [ "pv-species-a" ]
                    ] [] []

            match
                copyPropertyValueToLoadedTarget "pv-species-a" (ProvenancePropertyTarget.InputSets [ "input-a" ]) built
            with
            | Ok(nextModel, []) ->
                Expect.equal nextModel built "Copying an identical loaded value onto the same target should be a no-op."
            | other -> failwithf "Expected a no-op duplicate copy, got %A" other

        testCase "createLoadedSet adds the missing output side to an input-only loaded model"
        <| fun _ ->
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"
            let outputHeader = ioHeader FixtureKinds.sampleEndpoint "Output [Sample Name]"

            let built =
                model
                    "assay-table"
                    []
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" []
                    ] [] []

            match
                createLoadedSet
                    {
                        Side = ProvenanceSide.Output
                        Header = outputHeader
                        Name = "Output A"
                    }
                    built
            with
            | Ok(nextModel, [ ProvenanceTablePatch.AddLoadedSet(side, tableName, header, name) ]) ->
                Expect.equal side ProvenanceSide.Output "Patch should carry the created side."
                Expect.equal tableName "assay-table" "Patch should target the loaded table."
                Expect.equal header outputHeader "Patch should preserve the created header."
                Expect.equal name "Output A" "Patch should preserve the created name."
                Expect.equal nextModel.OutputSets.Count 1 "The missing output side should be created."
            | other -> failwithf "Expected AddLoadedSet patch, got %A" other

        testCase "createLoadedSet is a no-op when the same loaded endpoint already exists"
        <| fun _ ->
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"

            let built =
                model
                    "assay-table"
                    []
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" []
                    ] [] []

            match
                createLoadedSet
                    {
                        Side = ProvenanceSide.Input
                        Header = inputHeader
                        Name = "Input A"
                    }
                    built
            with
            | Ok(nextModel, []) ->
                Expect.equal nextModel built "Creating an already-existing loaded endpoint should be a no-op."
            | other -> failwithf "Expected no-op duplicate create, got %A" other

        testCase "connectSets works after the missing side was created on a partial model"
        <| fun _ ->
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"
            let outputHeader = ioHeader FixtureKinds.sampleEndpoint "Output [Sample Name]"

            let built =
                model
                    "assay-table"
                    []
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" []
                    ] [] []

            let created =
                match
                    createLoadedSet
                        {
                            Side = ProvenanceSide.Output
                            Header = outputHeader
                            Name = "Output A"
                        }
                        built
                with
                | Ok(nextModel, _) -> nextModel
                | Error error -> failwithf "Unexpected createLoadedSet error: %A" error

            let createdOutputId = created.OutputSets |> Map.toList |> List.exactlyOne |> fst

            match connectSets "input-a" createdOutputId None created with
            | Ok(nextModel, [ ProvenanceTablePatch.AddLoadedConnection(_, _, _, inputSetId, outputSetId) ]) ->
                Expect.equal inputSetId "input-a" "Connection patch should target the existing loaded input."
                Expect.equal outputSetId createdOutputId "Connection patch should target the created loaded output."

                Expect.equal
                    nextModel.Connections.Count
                    1
                    "The partial model should become connectable after creating the missing side."
            | other -> failwithf "Expected AddLoadedConnection patch, got %A" other

        testCase "connectSets creates a loaded input output connection"
        <| fun _ ->
            let model = validModel ()

            match connectSets "input-c" "output-a" None model with
            | Ok(nextModel,
                 [ ProvenanceTablePatch.AddLoadedConnection(tableName, processId, processName, inputSetId, outputSetId) ]) ->
                Expect.equal tableName "assay-table" "Patch should target loaded table."
                Expect.equal processId None "Caller-created connections do not have a source process id."
                Expect.equal processName None "Caller may create a connection without assigning a process name yet."
                Expect.equal inputSetId "input-c" "Patch should keep input set."
                Expect.equal outputSetId "output-a" "Patch should keep output set."

                Expect.isTrue
                    (nextModel.Connections
                     |> Map.exists (fun _ connection ->
                         connection.InputSetId = "input-c" && connection.OutputSetId = "output-a"
                     ))
                    "Model should contain new connection."
            | other -> failwithf "Expected one AddLoadedConnection patch, got %A" other

        testCase "connecting an already-connected pair is a no-op"
        <| fun _ ->
            let model = validModel ()

            match connectSets "input-a" "output-a" None model with
            | Ok(nextModel, []) ->
                Expect.equal
                    nextModel.Connections.Count
                    model.Connections.Count
                    "Re-connecting an already-connected pair must not create a duplicate connection."
            | other -> failwithf "Expected a no-op duplicate connect, got %A" other

        testCase "removeConnection removes the loaded connection and its inherited values"
        <| fun _ ->
            let model = validModel ()

            Expect.isTrue
                (model.OutputSets.["output-b"].InheritedPropertyValueIds.ContainsKey "connection-c")
                "Sanity: output-b should inherit values through connection-c before removal."

            match removeConnection "connection-c" model with
            | Ok(nextModel,
                 [ ProvenanceTablePatch.RemoveLoadedConnection(tableName,
                                                               processId,
                                                               processName,
                                                               inputSetId,
                                                               outputSetId) ]) ->
                Expect.equal tableName "assay-table" "Patch should target the loaded table."
                Expect.equal processId None "Fixture connections do not have a source process id."
                Expect.equal processName (Some "assay-process") "Patch should keep the process name for writeback."
                Expect.equal inputSetId "input-b" "Patch should keep the input set."
                Expect.equal outputSetId "output-b" "Patch should keep the output set."

                Expect.isFalse
                    (nextModel.Connections.ContainsKey "connection-c")
                    "Model should no longer contain the removed connection."

                Expect.isFalse
                    (nextModel.OutputSets.["output-b"].InheritedPropertyValueIds.ContainsKey "connection-c")
                    "Inherited values from the removed connection should be dropped."

                Expect.isTrue
                    (nextModel.OutputSets.["output-b"].InheritedPropertyValueIds.ContainsKey "connection-b")
                    "Other connections keep their inherited values."

                Expect.isFalse
                    (nextModel.InputSets.["input-b"].InheritedPropertyValueIds.ContainsKey "connection-c")
                    "The input side of the removed connection should drop its inherited values too."
            | other -> failwithf "Expected one RemoveLoadedConnection patch, got %A" other

        testCase "removeConnection rejects unknown connections"
        <| fun _ ->
            match removeConnection "missing-connection" (validModel ()) with
            | Error(EditError.ConnectionNotFound connectionId) ->
                Expect.equal connectionId "missing-connection" "Error should carry the missing connection id."
            | other -> failwithf "Expected ConnectionNotFound, got %A" other

        testCase "updatePropertyValue reads complete real origin anchor directly"
        <| fun _ ->
            let model = sourceOriginModel ()

            match updatePropertyValue "pv-species-a" (ProvenanceValue.Text "A. thaliana") None model with
            | Ok(nextModel, [ ProvenanceTablePatch.UpdatePropertyValue(propertyValueId, source, _, newValue, _) ]) ->
                Expect.equal propertyValueId "pv-species-a" "Patch should identify edited occurrence."

                Expect.equal
                    source.InputNames
                    [ "Input A" ]
                    "Source should derive input names from loaded set membership."

                Expect.equal
                    source.OutputNames
                    [ "Output A" ]
                    "Source should derive output names from loaded set membership."

                Expect.equal newValue (ProvenanceValue.Text "A. thaliana") "Patch should carry edited value."

                match nextModel.PropertyValues.["pv-species-a"].Origin with
                | ProvenancePropertyOrigin.Real anchor ->
                    Expect.equal anchor.Source.Name "assay-table" "Model should keep the real source anchor."
                | ProvenancePropertyOrigin.Virtual _ -> failwith "Expected the edited value to keep a real origin."
            | other -> failwithf "Expected one UpdatePropertyValue patch, got %A" other

        testCase "createLoadedPropertyValue stores a complete virtual origin anchor"
        <| fun _ ->
            let model = validModel ()
            let treatment = propertyHeader FixtureKinds.characteristicProperty "Treatment"

            let command = {
                Target = ProvenancePropertyTarget.Connections [ "connection-a" ]
                CopiedFrom = None
                Header = treatment
                Value = ProvenanceValue.Text "Drought"
                Unit = None
            }

            match createLoadedPropertyValue command model with
            | Ok(nextModel, [ ProvenanceTablePatch.AddLoadedPropertyValue _ ]) ->
                let created =
                    nextModel.PropertyValues
                    |> Map.toList
                    |> List.map snd
                    |> List.find (fun value -> value.Header = treatment)

                match created.Origin with
                | ProvenancePropertyOrigin.Virtual anchor ->
                    Expect.equal anchor.Source model.Source "Virtual origin should use the loaded model source."
                    Expect.equal anchor.ProcessName (Some "assay-process") "Connection process should be captured."
                    Expect.equal anchor.InputNames [ "Input A" ] "Virtual anchor should capture target input names."
                    Expect.equal anchor.OutputNames [ "Output A" ] "Virtual anchor should capture target output names."
                | ProvenancePropertyOrigin.Real _ -> failwith "Expected caller-created values to be virtual."
            | other -> failwithf "Expected one AddLoadedPropertyValue patch, got %A" other

        testCase "updating a virtual property value emits an update patch"
        <| fun _ ->
            let model = validModel ()
            let treatment = propertyHeader FixtureKinds.characteristicProperty "Treatment"

            let command = {
                Target = ProvenancePropertyTarget.InputSets [ "input-c" ]
                CopiedFrom = None
                Header = treatment
                Value = ProvenanceValue.Text "Drought"
                Unit = None
            }

            let withVirtualValue =
                match createLoadedPropertyValue command model with
                | Ok(nextModel, _) -> nextModel
                | Error error -> failwithf "Unexpected createLoadedPropertyValue error: %A" error

            let virtualId =
                withVirtualValue.PropertyValues
                |> Map.toList
                |> List.pick (fun (id, value) -> if value.Header = treatment then Some id else None)

            match updatePropertyValue virtualId (ProvenanceValue.Text "Edited") None withVirtualValue with
            | Ok(_, [ ProvenanceTablePatch.UpdatePropertyValue(propertyValueId, _, oldValue, newValue, _) ]) ->
                Expect.equal propertyValueId virtualId "Patch should identify the edited virtual occurrence."

                Expect.equal
                    oldValue
                    (ProvenanceValue.Text "Drought")
                    "Patch should carry the value from before the edit."

                Expect.equal newValue (ProvenanceValue.Text "Edited") "Patch should carry the edited value."
            | other -> failwithf "Expected exactly one UpdatePropertyValue patch for a virtual value, got %A" other
    ]

let fixtureTests =
    testList "Fixtures" [
        testCase "sampleModel includes loaded names and previous collapsed value"
        <| fun _ ->
            let model = sampleModel ()

            Expect.equal model.InputSets.["input-a"].Name "Input A" "Fixture should expose actual loaded input name."

            Expect.equal
                model.OutputSets.["output-b"].Name
                "Output B"
                "Fixture should expose actual loaded output name."

            let previousValues =
                model.InputSets.["input-a"].PropertyValueIds
                |> List.choose (fun propertyValueId -> model.PropertyValues.TryFind propertyValueId)
                |> List.filter (fun value -> (anchorOfOrigin value.Origin).Source.Name = "previous-study-table")

            Expect.isNonEmpty previousValues "Fixture should include collapsed previous-context property value."

        testCase "sampleModel connections are loaded set pairs"
        <| fun _ ->
            let model = sampleModel ()

            let pairs =
                model.Connections
                |> Map.toList
                |> List.map (fun (_, connection) -> connection.InputSetId, connection.OutputSetId)
                |> List.sort

            Expect.equal
                pairs
                [
                    "input-a", "output-a"
                    "input-a", "output-b"
                    "input-b", "output-b"
                    "input-c", "output-c"
                    "input-d", "output-d"
                ]
                "Fixture should preserve exact loaded input/output set connections."
    ]

let sessionTests =
    testList "Session" [
        testCase "init creates one conceptual layer with input and output sides"
        <| fun _ ->
            let initial = sampleModel ()
            let session = Session.init initial
            let active = Session.activeLayer session

            Expect.equal session.LayerOrder [ "layer-1" ] "Initial session should contain one conceptual layer."
            Expect.equal session.ActiveLayerId "layer-1" "Initial layer should be active."
            Expect.equal active.Id "layer-1" "Active layer id should be layer-1."
            Expect.equal active.InputSideId "layer-1-input" "Input side id should belong to layer-1."
            Expect.equal active.OutputSideId "layer-1-output" "Output side id should belong to layer-1."
            Expect.equal active.Model initial "Initial active layer should preserve the converted model."

        testCase "session layer ids and side ids use distinct namespaces"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let layer = Session.activeLayer session

            Expect.notEqual layer.Id layer.InputSideId "Layer id should not be reused as input side id."
            Expect.notEqual layer.Id layer.OutputSideId "Layer id should not be reused as output side id."
            Expect.notEqual layer.InputSideId layer.OutputSideId "Input and output side ids should be distinct."

        testCase "selectLayer switches display layers without producing writeback patches"
        <| fun _ ->
            let session = Session.init (sampleModel ())

            match Session.selectLayer "layer-1" session with
            | Ok(next, patches) ->
                Expect.equal next.ActiveLayerId "layer-1" "Known layer should become active."
                Expect.isEmpty patches "Navigation is view/session state only."
            | Error error -> failwithf "Expected layer selection success, got %A" error

        testCase "addLayer defaults to active outputs and creates a new layer with seeded inputs"
        <| fun _ ->
            let session = Session.init (sampleModel ())

            match Session.addLayer { Name = "Layer 2"; SelectedSets = [] } session with
            | Ok(next, patches) ->
                let layer = Session.activeLayer next

                let names =
                    layer.Model.InputSets
                    |> Map.toList
                    |> List.map (fun (_, set) -> set.Name)
                    |> List.sort

                Expect.equal next.LayerOrder [ "layer-1"; "layer-2" ] "A second conceptual layer should be created."
                Expect.equal next.ActiveLayerId "layer-2" "The new layer should become active."
                Expect.equal layer.InputSideId "layer-2-input" "New input side should belong to layer-2."
                Expect.equal layer.OutputSideId "layer-2-output" "New output side should belong to layer-2."

                Expect.equal
                    names
                    [
                        "Output A"
                        "Output B"
                        "Output C"
                        "Output D"
                        "Output E"
                    ]
                    "Active outputs should seed later inputs by default."

                Expect.isEmpty layer.Model.OutputSets "New layer starts without designated outputs."
                Expect.isEmpty layer.Model.Connections "No connector exists until the user creates one."
                Expect.equal next.ReferenceLinks.Length 5 "Each seeded input should keep an upstream reference."
                Expect.isEmpty patches "Layer derivation should not write ARC data."
            | Error error -> failwithf "Expected layer addition success, got %A" error

        testCase "addLayer uses entered name as temporary source identity"
        <| fun _ ->
            let session = Session.init (sampleModel ())

            match
                Session.addLayer
                    {
                        Name = "Extraction"
                        SelectedSets = [ ProvenanceSide.Output, "output-a" ]
                    }
                    session
            with
            | Ok(next, patches) ->
                let layer = Session.activeLayer next
                let projected = layer.Model.InputSets |> Map.toList |> List.exactlyOne |> snd

                Expect.equal layer.Label "Extraction" "Layer label should use the entered source name."

                Expect.equal
                    layer.Model.Source.Id
                    $"{layer.Id}:Extraction"
                    "Temporary source id should be namespaced with the layer id so same-named layers cannot collide."

                Expect.equal layer.Model.Source.Name "Extraction" "Temporary source name should use the entered name."
                Expect.equal projected.Source layer.Model.Source "Projected sets should belong to the new source."
                Expect.isEmpty patches "Layer naming is view/session state only."
            | Error error -> failwithf "Expected named layer addition success, got %A" error

        testCase "two added layers with the same name get distinct source ids"
        <| fun _ ->
            let withLayerTwo =
                match
                    Session.addLayer
                        {
                            Name = "Processing"
                            SelectedSets = []
                        }
                        (Session.init (sampleModel ()))
                with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected addLayer error: %A" error

            let onLayerOne =
                match Session.selectLayer "layer-1" withLayerTwo with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected selectLayer error: %A" error

            let withLayerThree =
                match
                    Session.addLayer
                        {
                            Name = "Processing"
                            SelectedSets = []
                        }
                        onLayerOne
                with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected addLayer error: %A" error

            let ids =
                withLayerThree.Layers
                |> List.map (fun layer -> layer.Model.Source.Id)
                |> List.distinct

            Expect.hasLength ids withLayerThree.Layers.Length "Every layer must own a unique Source.Id."

        testCase "addLayer treats selected active inputs as new layer inputs"
        <| fun _ ->
            let session = Session.init (sampleModel ())

            match
                Session.addLayer
                    {
                        Name = "Layer 2"
                        SelectedSets = [ ProvenanceSide.Input, "input-a" ]
                    }
                    session
            with
            | Ok(next, patches) ->
                let layer = Session.activeLayer next
                let input = layer.Model.InputSets |> Map.toList |> List.exactlyOne |> snd

                Expect.equal
                    next.LayerOrder
                    [ "layer-1"; "layer-2" ]
                    "Input-only selection should create one new conceptual layer."

                Expect.equal input.Name "Input A" "The selected active input should become a new layer input snapshot."
                Expect.equal next.ReferenceLinks.Length 1 "The selected active input should keep an upstream reference."
                Expect.isEmpty layer.Model.OutputSets "New layer starts without designated outputs."
                Expect.isEmpty patches "Layer derivation should not write ARC data."
            | Error error -> failwithf "Expected input-seeded layer addition success, got %A" error

        testCase "addLayer carries output properties inherited through loaded connections"
        <| fun _ ->
            let species = propertyHeader FixtureKinds.characteristicProperty "Species"
            let analysis = propertyHeader FixtureKinds.parameterProperty "Analysis"
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"
            let outputHeader = ioHeader FixtureKinds.sampleEndpoint "Output [Sample Name]"

            let initial =
                model
                    "assay-table"
                    [
                        propertyValue "pv-input-a-species" species (ProvenanceValue.Text "Arabidopsis") None None
                        propertyValue "pv-output-a-analysis" analysis (ProvenanceValue.Text "LC-MS") None None
                    ]
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" [ "pv-input-a-species" ]
                    ] [
                        outputSet "output-a" "assay-table" outputHeader "Output A" [ "pv-output-a-analysis" ]
                    ] [
                        connection "connection-a" "assay-table" None "input-a" "output-a"
                    ]
                |> Session.init

            match
                Session.addLayer
                    {
                        Name = "Layer 2"
                        SelectedSets = [ ProvenanceSide.Output, "output-a" ]
                    }
                    initial
            with
            | Ok(next, patches) ->
                Expect.isEmpty patches "Layer projection should not write ARC data."

                let projected =
                    (Session.activeLayer next).Model.InputSets
                    |> Map.toList
                    |> List.exactlyOne
                    |> snd

                Expect.equal
                    (ProvenanceSet.effectivePropertyValueIds projected |> List.sort)
                    [ "pv-input-a-species"; "pv-output-a-analysis" ]
                    "A projected output should keep properties inherited from its previously connected loaded input."
            | Error error -> failwithf "Expected layer addition success, got %A" error

        testCase "addLayer treats mixed input and output seeds as new layer inputs"
        <| fun _ ->
            let session = Session.init (sampleModel ())

            let selected = {
                Name = "Layer 2"
                SelectedSets = [
                    ProvenanceSide.Input, "input-a"
                    ProvenanceSide.Output, "output-b"
                ]
            }

            match Session.addLayer selected session with
            | Ok(next, patches) ->
                let layer = Session.activeLayer next

                let names =
                    layer.Model.InputSets
                    |> Map.toList
                    |> List.map (fun (_, set) -> set.Name)
                    |> List.sort

                Expect.equal
                    next.LayerOrder
                    [ "layer-1"; "layer-2" ]
                    "Mixed selection should create one new conceptual layer."

                Expect.equal names [ "Input A"; "Output B" ] "Mixed selection should become the next input side."
                Expect.equal next.ReferenceLinks.Length 2 "Only seeded entities should be linked."

                Expect.all
                    next.LayerOrder
                    (fun layerId -> layerId.StartsWith "layer-")
                    "Mixed selection should keep conceptual layer ids."

                Expect.isEmpty patches "Layer derivation should not write ARC data."
            | Error error -> failwithf "Expected mixed layer addition success, got %A" error

        testCase "property edits propagate through linked snapshots on focus change"
        <| fun _ ->
            let first = Session.init (sampleModel ())

            let layered =
                match
                    Session.addLayer
                        {
                            Name = "Layer 2"
                            SelectedSets = [ ProvenanceSide.Output, "output-a" ]
                        }
                        first
                with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected addLayer error: %A" error

            let layer2 = Session.activeLayer layered
            let carriedInput = layer2.Model.InputSets |> Map.toList |> List.exactlyOne |> snd
            let analysisId = carriedInput.PropertyValueIds |> List.head

            let edited =
                match Session.updatePropertyValue analysisId (ProvenanceValue.Text "Imaging") None layered with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected updatePropertyValue error: %A" error

            let layer1BeforeFocus =
                (Session.layerById "layer-1" edited).Model.PropertyValues.[analysisId].Value

            let layer2BeforeFocus =
                (Session.layerById "layer-2" edited).Model.PropertyValues.[analysisId].Value

            Expect.notEqual
                layer1BeforeFocus
                (ProvenanceValue.Text "Imaging")
                "Upstream layer should not update before focus refresh."

            Expect.equal
                layer2BeforeFocus
                (ProvenanceValue.Text "Imaging")
                "Focused downstream layer should update immediately."

            match Session.selectLayer "layer-1" edited with
            | Ok(refreshed, patches) ->
                let layer1Value =
                    (Session.layerById "layer-1" refreshed).Model.PropertyValues.[analysisId].Value

                let layer2Value =
                    (Session.layerById "layer-2" refreshed).Model.PropertyValues.[analysisId].Value

                Expect.isEmpty patches "Focus refresh should not emit writeback patches."

                Expect.equal
                    layer1Value
                    (ProvenanceValue.Text "Imaging")
                    "Upstream layer should receive linked property edit."

                Expect.equal
                    layer2Value
                    (ProvenanceValue.Text "Imaging")
                    "Downstream layer should keep linked property edit."

                Expect.isEmpty refreshed.DirtyPropertyValueIds "Focus refresh should clear dirty property ids."
            | Error error -> failwithf "Unexpected selectLayer error: %A" error

        testCase "upstream property additions refresh into downstream referenced inputs on focus change"
        <| fun _ ->
            let layered =
                Session.init (sampleModel ())
                |> Session.addLayer {
                    Name = "Layer 2"
                    SelectedSets = [ ProvenanceSide.Output, "output-d" ]
                }
                |> function
                    | Ok(next, _) -> next
                    | Error error -> failwithf "Unexpected addLayer error: %A" error

            let backToLayer1 =
                match Session.selectLayer "layer-1" layered with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected selectLayer error: %A" error

            let treatment = propertyHeader FixtureKinds.characteristicProperty "Treatment"

            let command = {
                Target = ProvenancePropertyTarget.OutputSets [ "output-d" ]
                CopiedFrom = None
                Header = treatment
                Value = ProvenanceValue.Text "Drought"
                Unit = None
            }

            let edited =
                match Session.createLoadedPropertyValue command backToLayer1 with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected createLoadedPropertyValue error: %A" error

            let layer2BeforeFocus = Session.layerById "layer-2" edited

            let inputBeforeFocus =
                layer2BeforeFocus.Model.InputSets |> Map.toList |> List.exactlyOne |> snd

            let valuesBeforeFocus =
                ProvenanceSet.effectivePropertyValueIds inputBeforeFocus
                |> List.choose (fun id -> layer2BeforeFocus.Model.PropertyValues.TryFind id)

            Expect.isFalse
                (valuesBeforeFocus |> List.exists (fun pv -> pv.Header = treatment))
                "Downstream input should not receive upstream structural additions before focus refresh."

            match Session.selectLayer "layer-2" edited with
            | Ok(refreshed, patches) ->
                let layer2 = Session.layerById "layer-2" refreshed
                let input = layer2.Model.InputSets |> Map.toList |> List.exactlyOne |> snd

                let values =
                    ProvenanceSet.effectivePropertyValueIds input
                    |> List.choose (fun id -> layer2.Model.PropertyValues.TryFind id)

                Expect.isEmpty patches "Focus refresh should not emit writeback patches."

                Expect.isTrue
                    (values |> List.exists (fun pv -> pv.Header = treatment))
                    "Downstream input should receive upstream property addition."
            | Error error -> failwithf "Unexpected selectLayer error: %A" error

        testCase "upstream property removals refresh into downstream referenced inputs on focus change"
        <| fun _ ->
            let layered =
                Session.init (sampleModel ())
                |> Session.addLayer {
                    Name = "Layer 2"
                    SelectedSets = [ ProvenanceSide.Output, "output-d" ]
                }
                |> function
                    | Ok(next, _) -> next
                    | Error error -> failwithf "Unexpected addLayer error: %A" error

            let backToLayer1 =
                match Session.selectLayer "layer-1" layered with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected selectLayer error: %A" error

            let layer1 = Session.layerById "layer-1" backToLayer1
            let output = layer1.Model.OutputSets.["output-d"]
            let removedId = ProvenanceSet.effectivePropertyValueIds output |> List.head

            let trimmedOutput =
                if output.PropertyValueIds |> List.contains removedId then
                    {
                        output with
                            PropertyValueIds = output.PropertyValueIds |> List.filter ((<>) removedId)
                    }
                else
                    {
                        output with
                            InheritedPropertyValueIds =
                                output.InheritedPropertyValueIds
                                |> Map.map (fun _ ids -> ids |> List.filter ((<>) removedId))
                                |> Map.filter (fun _ ids -> not ids.IsEmpty)
                    }

            let trimmedLayer = {
                layer1 with
                    Model = {
                        layer1.Model with
                            OutputSets = layer1.Model.OutputSets |> Map.add output.Id trimmedOutput
                    }
            }

            let edited = {
                backToLayer1 with
                    Layers =
                        backToLayer1.Layers
                        |> List.map (fun layer -> if layer.Id = trimmedLayer.Id then trimmedLayer else layer)
            }

            let layer2BeforeFocus = Session.layerById "layer-2" edited

            let inputBeforeFocus =
                layer2BeforeFocus.Model.InputSets |> Map.toList |> List.exactlyOne |> snd

            Expect.isTrue
                (ProvenanceSet.effectivePropertyValueIds inputBeforeFocus
                 |> List.contains removedId)
                "Downstream input should keep removed upstream property ids until focus refresh."

            match Session.selectLayer "layer-2" edited with
            | Ok(refreshed, patches) ->
                let layer2 = Session.layerById "layer-2" refreshed
                let input = layer2.Model.InputSets |> Map.toList |> List.exactlyOne |> snd
                let inputIds = ProvenanceSet.effectivePropertyValueIds input

                Expect.isEmpty patches "Focus refresh should not emit writeback patches."

                Expect.isFalse
                    (inputIds |> List.contains removedId)
                    "Downstream input should drop removed upstream property ids."
            | Error error -> failwithf "Unexpected selectLayer error: %A" error

        testCase "connection-derived properties refresh downstream on focus change"
        <| fun _ ->
            let layered =
                Session.init (sampleModel ())
                |> Session.addLayer {
                    Name = "Layer 2"
                    SelectedSets = [ ProvenanceSide.Output, "output-a" ]
                }
                |> function
                    | Ok(next, _) -> next
                    | Error error -> failwithf "Unexpected addLayer error: %A" error

            let backToLayer1 =
                match Session.selectLayer "layer-1" layered with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected selectLayer error: %A" error

            let command = {
                Target = ProvenancePropertyTarget.Connections [ "connection-a" ]
                CopiedFrom = None
                Header = propertyHeader FixtureKinds.parameterProperty "Analysis"
                Value = ProvenanceValue.Text "Microscopy"
                Unit = None
            }

            let edited =
                match Session.createLoadedPropertyValue command backToLayer1 with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected createLoadedPropertyValue error: %A" error

            let outputBeforeFocus =
                (Session.layerById "layer-1" edited).Model.OutputSets.["output-a"]

            let inputBeforeFocus =
                (Session.layerById "layer-2" edited).Model.InputSets.["layer-2-from-output-0-output-a"]

            Expect.notEqual
                inputBeforeFocus.PropertyValueIds
                outputBeforeFocus.PropertyValueIds
                "Downstream input should not mirror connection-derived upstream property changes before focus refresh."

            match Session.selectLayer "layer-2" edited with
            | Ok(refreshed, patches) ->
                let firstOutput =
                    (Session.layerById "layer-1" refreshed).Model.OutputSets.["output-a"]

                let nextInput =
                    (Session.layerById "layer-2" refreshed).Model.InputSets.["layer-2-from-output-0-output-a"]

                Expect.isEmpty patches "Focus refresh should not emit writeback patches."

                Expect.equal
                    nextInput.PropertyValueIds
                    firstOutput.PropertyValueIds
                    "Downstream input should mirror connection-derived upstream output properties after refresh."
            | Error error -> failwithf "Unexpected selectLayer error: %A" error

        testCase "focus refresh does not change unreferenced downstream snapshots"
        <| fun _ ->
            let layered =
                Session.init (sampleModel ())
                |> Session.addLayer {
                    Name = "Layer 2"
                    SelectedSets = [ ProvenanceSide.Output, "output-a" ]
                }
                |> function
                    | Ok(next, _) -> next
                    | Error error -> failwithf "Unexpected addLayer error: %A" error

            let backToLayer1 =
                match Session.selectLayer "layer-1" layered with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected selectLayer error: %A" error

            let addedOutput =
                match
                    Session.createLoadedSet
                        {
                            Side = ProvenanceSide.Output
                            Header = ioHeader FixtureKinds.dataEndpoint "Output [Data]"
                            Name = "Unreferenced output"
                        }
                        backToLayer1
                with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected createLoadedSet error: %A" error

            match Session.selectLayer "layer-2" addedOutput with
            | Ok(refreshed, patches) ->
                let layer2 = Session.layerById "layer-2" refreshed

                Expect.isEmpty patches "Focus refresh should not emit writeback patches."

                Expect.equal
                    layer2.Model.InputSets.Count
                    1
                    "Only the referenced downstream input snapshot should remain present."

                Expect.isFalse
                    (layer2.Model.InputSets
                     |> Map.exists (fun _ set -> set.Name = "Unreferenced output"))
                    "Unreferenced upstream structural additions should not appear downstream."
            | Error error -> failwithf "Unexpected selectLayer error: %A" error

        testCase "assigning a palette value to a carried next input writes only to the active layer"
        <| fun _ ->
            let first = Session.init (sampleModel ())

            let layered =
                match
                    Session.addLayer
                        {
                            Name = "Layer 2"
                            SelectedSets = [ ProvenanceSide.Output, "output-d" ]
                        }
                        first
                with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected addLayer error: %A" error

            let projectedId =
                (Session.activeLayer layered).Model.InputSets
                |> Map.toList
                |> List.exactlyOne
                |> fst

            let previousBefore =
                (Session.layerById "layer-1" layered).Model.OutputSets.["output-d"]

            let treatment = propertyHeader FixtureKinds.characteristicProperty "Treatment"

            let command = {
                Target = ProvenancePropertyTarget.InputSets [ projectedId ]
                CopiedFrom = None
                Header = treatment
                Value = ProvenanceValue.Text "Drought"
                Unit = None
            }

            match Session.createCurrentLoadedPropertyValue command layered with
            | Ok(next, [ ProvenanceTablePatch.AddLoadedPropertyValue(target, _, header, value, _) ]) ->
                let previousOutput =
                    (Session.layerById "layer-1" next).Model.OutputSets.["output-d"]

                let nextInput = (Session.layerById "layer-2" next).Model.InputSets.[projectedId]

                Expect.equal
                    target
                    (ProvenancePropertyTarget.InputSets [ projectedId ])
                    "Palette drops should patch the active displayed target."

                Expect.equal header treatment "Patch should carry the palette property."
                Expect.equal value (ProvenanceValue.Text "Drought") "Patch should carry the palette value."

                Expect.equal
                    previousOutput.PropertyValueIds
                    previousBefore.PropertyValueIds
                    "Palette additions must not write through to the native boundary owner."

                Expect.isTrue
                    (nextInput.PropertyValueIds.Length > previousBefore.PropertyValueIds.Length)
                    "The active layer input should receive the new property value."
            | other -> failwithf "Expected one active-layer property-add patch, got %A" other

        testCase "active layer property additions on carried inputs survive focus refresh"
        <| fun _ ->
            let layered =
                Session.init (sampleModel ())
                |> Session.addLayer {
                    Name = "Layer 2"
                    SelectedSets = [ ProvenanceSide.Output, "output-d" ]
                }
                |> function
                    | Ok(next, _) -> next
                    | Error error -> failwithf "Unexpected addLayer error: %A" error

            let projectedId =
                (Session.activeLayer layered).Model.InputSets
                |> Map.toList
                |> List.exactlyOne
                |> fst

            let treatment = propertyHeader FixtureKinds.characteristicProperty "Treatment"

            let command = {
                Target = ProvenancePropertyTarget.InputSets [ projectedId ]
                CopiedFrom = None
                Header = treatment
                Value = ProvenanceValue.Text "Drought"
                Unit = None
            }

            let added =
                match Session.createCurrentLoadedPropertyValue command layered with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected createCurrentLoadedPropertyValue error: %A" error

            let hasTreatment session =
                let layer2 = Session.layerById "layer-2" session
                let carriedInput = layer2.Model.InputSets.[projectedId]

                ProvenanceSet.effectivePropertyValueIds carriedInput
                |> List.choose (fun id -> layer2.Model.PropertyValues.TryFind id)
                |> List.exists (fun value -> value.Header = treatment)

            Expect.isTrue (hasTreatment added) "Sanity: active layer input should receive the local property addition."

            let visitedLayer1 =
                match Session.selectLayer "layer-1" added with
                | Ok(next, patches) ->
                    Expect.isEmpty patches "Focus refresh should not emit writeback patches."
                    next
                | Error error -> failwithf "Unexpected selectLayer error: %A" error

            Expect.isTrue
                (hasTreatment visitedLayer1)
                "Local additions on a carried downstream input should not be dropped when focusing upstream."

            let visitedLayer2 =
                match Session.selectLayer "layer-2" visitedLayer1 with
                | Ok(next, patches) ->
                    Expect.isEmpty patches "Focus refresh should not emit writeback patches."
                    next
                | Error error -> failwithf "Unexpected selectLayer error: %A" error

            Expect.isTrue
                (hasTreatment visitedLayer2)
                "Local additions on a carried downstream input should remain after returning to that layer."

        testCase "updating multiple property values uses the edit path for every value"
        <| fun _ ->
            let session = Session.init (validModel ())

            let updates = [
                "pv-species-arabidopsis-a", ProvenanceValue.Text "A. thaliana", None
                "pv-species-arabidopsis-b", ProvenanceValue.Text "A. thaliana", None
            ]

            match Session.updatePropertyValues updates session with
            | Ok(next, patches) ->
                let model = (Session.activeLayer next).Model
                Expect.equal patches.Length 2 "Every existing value should produce an edit patch."

                Expect.equal
                    model.PropertyValues.["pv-species-arabidopsis-a"].Value
                    (ProvenanceValue.Text "A. thaliana")
                    "First value should be edited."

                Expect.equal
                    model.PropertyValues.["pv-species-arabidopsis-b"].Value
                    (ProvenanceValue.Text "A. thaliana")
                    "Second value should be edited."
            | other -> failwithf "Expected batched edit success, got %A" other

        testCase "creating and connecting a later output modifies only the active layer"
        <| fun _ ->
            let layered =
                Session.init (sampleModel ())
                |> Session.addLayer {
                    Name = "Layer 2"
                    SelectedSets = [ ProvenanceSide.Output, "output-a" ]
                }
                |> function
                    | Ok(next, _) -> next
                    | Error error -> failwithf "%A" error

            let outputHeader = ioHeader FixtureKinds.dataEndpoint "Output [Data]"

            let created =
                match
                    Session.createLoadedSet
                        {
                            Side = ProvenanceSide.Output
                            Header = outputHeader
                            Name = "Raw file"
                        }
                        layered
                with
                | Ok(next, _) -> next
                | Error error -> failwithf "%A" error

            let layer = Session.activeLayer created
            let inputId = layer.Model.InputSets |> Map.toList |> List.exactlyOne |> fst
            let outputId = layer.Model.OutputSets |> Map.toList |> List.exactlyOne |> fst

            match Session.connectSets inputId outputId None created with
            | Ok(next, [ ProvenanceTablePatch.AddLoadedConnection _ ]) ->
                Expect.equal
                    (Session.layerById "layer-1" next).Model.Connections.Count
                    5
                    "Prior transition should remain unchanged."

                Expect.equal
                    (Session.layerById "layer-2" next).Model.Connections.Count
                    1
                    "Later transition gets its connection."
            | other -> failwithf "Expected a later connection patch, got %A" other

        testCase "removeConnections removes loaded connections from the active layer"
        <| fun _ ->
            let session = sampleSession ()
            let initialCount = (Session.activeLayer session).Model.Connections.Count

            match Session.removeConnections [ "connection-a"; "connection-b"; "connection-a" ] session with
            | Ok(next, patches) ->
                Expect.equal patches.Length 2 "Duplicate ids should collapse to one patch per removed connection."

                Expect.all
                    patches
                    (function
                    | ProvenanceTablePatch.RemoveLoadedConnection _ -> true
                    | _ -> false)
                    "All emitted patches should remove loaded connections."

                Expect.equal
                    (Session.activeLayer next).Model.Connections.Count
                    (initialCount - 2)
                    "Both connections should be removed from the active layer."
            | Error error -> failwithf "Unexpected removeConnections error: %A" error

        testCase "init and addLayer work for an output-only model"
        <| fun _ ->
            let initial = Session.init (outputOnlyModel ())

            Expect.isEmpty
                (Session.activeLayer initial).Model.InputSets
                "Output-only input has no synthetic input endpoints."

            Expect.isNonEmpty (Session.activeLayer initial).Model.OutputSets "Real output endpoints remain visible."

            match Session.addLayer { Name = "Layer 2"; SelectedSets = [] } initial with
            | Ok(next, patches) ->
                let layer = Session.activeLayer next
                Expect.isNonEmpty layer.Model.InputSets "Current outputs should seed the next displayed input side."
                Expect.isEmpty layer.Model.OutputSets "A newly displayed transition has no invented outputs."
                Expect.isEmpty patches "View-layer derivation is not a persistence edit."
            | Error error -> failwithf "Expected output-only layer derivation success, got %A" error

        testCase "adding a value to a removed connection returns a session error"
        <| fun _ ->
            let command = {
                Target = ProvenancePropertyTarget.Connections [ "missing-connection" ]
                CopiedFrom = None
                Header = propertyHeader FixtureKinds.parameterProperty "Analysis"
                Value = ProvenanceValue.Text "Microscopy"
                Unit = None
            }

            match Session.createLoadedPropertyValue command (Session.init (sampleModel ())) with
            | Error(SessionError.EditFailed(EditError.ConnectionNotFound connectionId)) ->
                Expect.equal connectionId "missing-connection" "Removed target identity should be returned."
            | other -> failwithf "Expected missing-connection session error, got %A" other

        testCase "freshly created property value ids never collide with other layers"
        <| fun _ ->
            let outputHeader = ioHeader FixtureKinds.sampleEndpoint "Output [Sample Name]"
            let analysis = propertyHeader FixtureKinds.parameterProperty "Analysis"

            let createValue target value session =
                match
                    Session.createCurrentLoadedPropertyValue
                        {
                            Target = target
                            CopiedFrom = None
                            Header = analysis
                            Value = value
                            Unit = None
                        }
                        session
                with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected createCurrentLoadedPropertyValue error: %A" error

            let built =
                model "assay-table" [] [] [
                    outputSet "output-a" "assay-table" outputHeader "Output A" []
                    outputSet "output-b" "assay-table" outputHeader "Output B" []
                ] []

            // Layer 1 gets two generated values (one per output); layer 2 is seeded
            // from output-a only, so it starts out knowing about only the first one.
            let withLayerOneValues =
                Session.init built
                |> createValue (ProvenancePropertyTarget.OutputSets [ "output-a" ]) (ProvenanceValue.Text "First")
                |> createValue (ProvenancePropertyTarget.OutputSets [ "output-b" ]) (ProvenanceValue.Text "Second")

            let layered =
                match
                    Session.addLayer
                        {
                            Name = "Layer 2"
                            SelectedSets = [ ProvenanceSide.Output, "output-a" ]
                        }
                        withLayerOneValues
                with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected addLayer error: %A" error

            let projectedId =
                (Session.activeLayer layered).Model.InputSets
                |> Map.toList
                |> List.exactlyOne
                |> fst

            let withNewValue =
                layered
                |> createValue
                    (ProvenancePropertyTarget.InputSets [ projectedId ])
                    (ProvenanceValue.Text "LayerTwoOwnValue")

            let newId =
                (Session.activeLayer withNewValue).Model.PropertyValues
                |> Map.toList
                |> List.pick (fun (id, value) ->
                    if value.Value = ProvenanceValue.Text "LayerTwoOwnValue" then
                        Some id
                    else
                        None
                )

            let layerOneIds =
                (Session.layerById "layer-1" withNewValue).Model.PropertyValues
                |> Map.toList
                |> List.map fst
                |> Set.ofList

            Expect.isFalse
                (layerOneIds.Contains newId)
                $"Fresh id '{newId}' collides with an unrelated layer-1 property value."

        testCase "layer switch never overwrites an unrelated same-id value in another layer"
        <| fun _ ->
            let outputHeader = ioHeader FixtureKinds.sampleEndpoint "Output [Sample Name]"
            let analysis = propertyHeader FixtureKinds.parameterProperty "Analysis"

            let createValue target value session =
                match
                    Session.createCurrentLoadedPropertyValue
                        {
                            Target = target
                            CopiedFrom = None
                            Header = analysis
                            Value = value
                            Unit = None
                        }
                        session
                with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected createCurrentLoadedPropertyValue error: %A" error

            let built =
                model "assay-table" [] [] [
                    outputSet "output-a" "assay-table" outputHeader "Output A" []
                    outputSet "output-b" "assay-table" outputHeader "Output B" []
                ] []

            let withLayerOneValues =
                Session.init built
                |> createValue (ProvenancePropertyTarget.OutputSets [ "output-a" ]) (ProvenanceValue.Text "First")
                |> createValue (ProvenancePropertyTarget.OutputSets [ "output-b" ]) (ProvenanceValue.Text "Second")

            let outputBValueId =
                (Session.layerById "layer-1" withLayerOneValues).Model.OutputSets.["output-b"].PropertyValueIds
                |> List.exactlyOne

            let layered =
                match
                    Session.addLayer
                        {
                            Name = "Layer 2"
                            SelectedSets = [ ProvenanceSide.Output, "output-a" ]
                        }
                        withLayerOneValues
                with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected addLayer error: %A" error

            let projectedId =
                (Session.activeLayer layered).Model.InputSets
                |> Map.toList
                |> List.exactlyOne
                |> fst

            let withNewValue =
                layered
                |> createValue
                    (ProvenancePropertyTarget.InputSets [ projectedId ])
                    (ProvenanceValue.Text "LayerTwoOwnValue")

            let onLayerOne =
                match Session.selectLayer "layer-1" withNewValue with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected selectLayer error: %A" error

            let dirtied =
                match
                    Session.updatePropertyValue outputBValueId (ProvenanceValue.Text "EditedOnLayerOne") None onLayerOne
                with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected updatePropertyValue error: %A" error

            let backOnLayerTwo =
                match Session.selectLayer "layer-2" dirtied with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected selectLayer error: %A" error

            // The corruption does not overwrite the value record in place (that would
            // be too easy to spot) - it silently drops newId from the projected set's
            // own reference list, because copySetData's "is this id local to me"
            // check keys purely on string identity and an unrelated layer-1 value
            // happens to share that identity. So the real observable is whether the
            // set can still reach its own value at all, not just whether an orphaned
            // entry remains sitting in the PropertyValues map.
            let hasLayerTwoOwnValue session =
                let layer2 = Session.layerById "layer-2" session
                let projectedSet = layer2.Model.InputSets.[projectedId]

                ProvenanceSet.effectivePropertyValueIds projectedSet
                |> List.choose (fun id -> layer2.Model.PropertyValues.TryFind id)
                |> List.exists (fun value -> value.Value = ProvenanceValue.Text "LayerTwoOwnValue")

            Expect.isTrue
                (hasLayerTwoOwnValue backOnLayerTwo)
                "Layer-2's own value must remain attached to its set after an unrelated layer-1 edit and focus switch."

        testCase "session patch log accumulates leaf edits exactly once"
        <| fun _ ->
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"
            let session = Session.init (sampleModel ())

            let session1, patches1 =
                match Session.connectSets "input-d" "output-a" None session with
                | Ok(next, patches) -> next, patches
                | Error error -> failwithf "Unexpected connectSets error: %A" error

            Expect.equal session1.PatchLog patches1 "First edit: log equals its own delta."

            let session2, patches2 =
                match
                    Session.createLoadedSet
                        {
                            Side = ProvenanceSide.Input
                            Header = inputHeader
                            Name = "Input E"
                        }
                        session1
                with
                | Ok(next, patches) -> next, patches
                | Error error -> failwithf "Unexpected createLoadedSet error: %A" error

            Expect.equal session2.PatchLog (patches1 @ patches2) "Log is append-only across edits, no duplicates."

        testCase "restoring a prior session snapshot also restores its patch log"
        <| fun _ ->
            let before = Session.init (sampleModel ())

            let after =
                match Session.connectSets "input-d" "output-a" None before with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected connectSets error: %A" error

            Expect.isNonEmpty after.PatchLog "Sanity: the edit added a log entry."

            Expect.isEmpty
                before.PatchLog
                "Undo restoring the pre-edit snapshot must retract its patch log too, since PatchLog lives on the session."
    ]

let uiStateTests =
    testList "UI state" [
        testCase "initial UI state creates side states for the active layer sides"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let layer = Session.activeLayer session
            let state = State.init session

            Expect.isTrue (state.SideStates.ContainsKey layer.InputSideId) "Input side state should exist."
            Expect.isTrue (state.SideStates.ContainsKey layer.OutputSideId) "Output side state should exist."
            Expect.equal state.SideStates.Count 2 "Initial layer should create exactly two side states."

        testCase "new layer side states do not inherit grouping assignments"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let layer1 = Session.activeLayer session
            let replicate = propertyHeader FixtureKinds.parameterProperty "Replicate"
            let replicateKey = propertyKeyIn layer1.Model replicate

            let grouped =
                State.init session
                |> State.GroupingAssignments.toggleSide layer1.OutputSideId ProvenanceSide.Output replicateKey

            let layered =
                match
                    Session.addLayer
                        {
                            Name = "Layer 2"
                            SelectedSets = [ ProvenanceSide.Output, "output-a" ]
                        }
                        session
                with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected addLayer error: %A" error

            let layer2 = Session.activeLayer layered
            let nextState = State.Sides.ensure layered grouped

            Expect.equal
                (State.Sides.get layer2.InputSideId nextState).GroupingAssignments
                []
                "New layer input side should not inherit upstream grouping assignments."

            Expect.equal
                (State.Sides.get layer2.OutputSideId nextState).GroupingAssignments
                []
                "New layer output side should not inherit upstream grouping assignments."

        testCase "toggleSideGrouping applies grouping only to the selected layer side"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let layer = Session.activeLayer session
            let state = State.init session
            let replicate = propertyHeader FixtureKinds.parameterProperty "Replicate"
            let replicateKey = propertyKeyIn layer.Model replicate

            let next =
                State.GroupingAssignments.toggleSide layer.OutputSideId ProvenanceSide.Output replicateKey state

            Expect.equal
                (State.Sides.get layer.InputSideId next).GroupingAssignments
                []
                "Side-only output grouping should not change the input layer state."

            Expect.equal
                (State.Sides.get layer.OutputSideId next).GroupingAssignments
                [
                    {
                        Key = replicateKey
                        Scope = GroupingScope.Output
                    }
                ]
                "Side-only output grouping should be stored as an output-scoped assignment."

        testCase "toggleBothGrouping applies grouping to both active layer states"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let layer = Session.activeLayer session
            let state = State.init session
            let replicate = propertyHeader FixtureKinds.parameterProperty "Replicate"
            let replicateKey = propertyKeyIn layer.Model replicate

            let next =
                State.GroupingAssignments.toggleBoth layer.InputSideId layer.OutputSideId replicateKey state

            let expected = [
                {
                    Key = replicateKey
                    Scope = GroupingScope.Both
                }
            ]

            Expect.equal
                (State.Sides.get layer.InputSideId next).GroupingAssignments
                expected
                "Both-side grouping should be visible from the input layer."

            Expect.equal
                (State.Sides.get layer.OutputSideId next).GroupingAssignments
                expected
                "Both-side grouping should be visible from the output layer."

        testCase "moveGrouping switches a property to the target side only"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let layer = Session.activeLayer session
            let state = State.init session
            let species = propertyHeader FixtureKinds.characteristicProperty "Species"
            let speciesKey = propertyKeyIn layer.Model species

            let selected =
                State.GroupingAssignments.toggleSide layer.InputSideId ProvenanceSide.Input speciesKey state

            let next =
                State.GroupingAssignments.move
                    layer.Id
                    layer.InputSideId
                    layer.OutputSideId
                    ProvenanceSide.Output
                    speciesKey
                    selected

            Expect.equal
                (State.Sides.get layer.InputSideId next).GroupingAssignments
                []
                "Dragging a property away should remove it from the source layer."

            Expect.equal
                (State.Sides.get layer.OutputSideId next).GroupingAssignments
                [
                    {
                        Key = speciesKey
                        Scope = GroupingScope.Output
                    }
                ]
                "Dragging a property to output should make it output-only."

            Expect.equal
                (next.PropertyRailPlacements |> Map.tryFind (layer.Id, speciesKey))
                (Some ProvenanceSide.Output)
                "Dragging a property to output should move its rail control to the output side."

        testCase "toggleBothGrouping removes only both-scope assignments from inconsistent state"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let layer = Session.activeLayer session
            let state = State.init session
            let species = propertyHeader FixtureKinds.characteristicProperty "Species"
            let key = propertyKeyIn layer.Model species

            let sideAssignment = {
                Key = key
                Scope = GroupingScope.Input
            }

            let bothAssignment = {
                Key = key
                Scope = GroupingScope.Both
            }

            let inconsistent = {
                state with
                    SideStates =
                        state.SideStates
                        |> Map.add layer.InputSideId {
                            GroupingAssignments = [ sideAssignment; bothAssignment ]
                        }
                        |> Map.add layer.OutputSideId {
                            GroupingAssignments = [ bothAssignment ]
                        }
            }

            let next =
                State.GroupingAssignments.toggleBoth layer.InputSideId layer.OutputSideId key inconsistent

            Expect.equal
                (State.Sides.get layer.InputSideId next).GroupingAssignments
                [ sideAssignment ]
                "Removing a both-side grouping should not silently drop an existing side-specific assignment for the same key."

            Expect.equal
                (State.Sides.get layer.OutputSideId next).GroupingAssignments
                []
                "Only the both-side assignment should be removed from the opposite layer."

        testCase "panel layout clamps side panels and preserves a middle panel"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let layer = Session.activeLayer session
            let state = State.init session

            let tooSmallLeft =
                State.PanelLayout.setLeft layer.Id 2 state |> State.PanelLayout.get layer.Id

            Expect.equal tooSmallLeft.Left 15 "Left panel should not shrink below the minimum."
            Expect.equal tooSmallLeft.Middle 65 "Middle panel should keep the remaining usable width."
            Expect.equal tooSmallLeft.Right 20 "Unchanged right panel should stay at the default width."

            let tooLargeRight =
                state
                |> State.PanelLayout.setLeft layer.Id 45
                |> State.PanelLayout.setRight layer.Id 80
                |> State.PanelLayout.get layer.Id

            Expect.equal tooLargeRight.Left 45 "Left panel should retain the committed user ratio."
            Expect.equal tooLargeRight.Right 25 "Right panel should be clamped so the middle panel keeps its minimum."
            Expect.equal tooLargeRight.Middle 30 "Middle panel must not shrink below its minimum."

        testCase "connection handle identities round-trip through drag and drop ids"
        <| fun _ ->
            let handle: Types.ConnectionHandleRef = {
                Kind = Types.ConnectionHandleKind.GroupMember
                Side = ProvenanceSide.Input
                Id = "input-a"
                ParentGroupId = Some "input:Species=Arabidopsis"
            }

            Expect.equal
                (DragDrop.connectionHandleDragId handle |> DragDrop.tryDragId)
                (Some(DragDrop.Payload.ConnectionHandle handle))
                "Connection drag id should parse back to the same handle reference."

            Expect.equal
                (DragDrop.connectionHandleDropId handle |> DragDrop.tryConnectionDropId)
                (Some handle)
                "Connection drop id should parse back to the same handle reference."

        testCase "connection routing accepts compatible group and member targets only"
        <| fun _ ->
            let group side id : Types.ConnectionHandleRef = {
                Kind = Types.ConnectionHandleKind.GroupCard
                Side = side
                Id = id
                ParentGroupId = None
            }

            let memberHandle side parent id : Types.ConnectionHandleRef = {
                Kind = Types.ConnectionHandleKind.GroupMember
                Side = side
                Id = id
                ParentGroupId = Some parent
            }

            Expect.equal
                (ConnectionRouting.action
                    (group ProvenanceSide.Input "input:input-a")
                    (group ProvenanceSide.Output "output:output-z"))
                (Some(ConnectionRouting.ConnectionAction.ConnectGroups("input:input-a", "output:output-z")))
                "Input group handles should connect to output group handles."

            Expect.equal
                (ConnectionRouting.action
                    (group ProvenanceSide.Input "input:input-a")
                    (group ProvenanceSide.Input "input:input-b"))
                None
                "Same-side group handles should be rejected."

            Expect.equal
                (ConnectionRouting.action
                    (memberHandle ProvenanceSide.Input "input:g" "input-a")
                    (memberHandle ProvenanceSide.Output "output:g" "output-a"))
                (Some(ConnectionRouting.ConnectionAction.ConnectMembers("input:g", "output:g", "input-a", "output-a")))
                "Opposite-side member handles should create a manual member connection action."

        testCase "connection routing ignores property value and property header handles"
        <| fun _ ->
            let handle kind side id : Types.ConnectionHandleRef = {
                Kind = kind
                Side = side
                Id = id
                ParentGroupId = None
            }

            let valueHandle =
                handle Types.ConnectionHandleKind.PropertyValue ProvenanceSide.Input "pv-input-a-species"

            let headerHandle =
                handle Types.ConnectionHandleKind.PropertyHeader ProvenanceSide.Input "species"

            let sameSideGroup =
                handle Types.ConnectionHandleKind.GroupCard ProvenanceSide.Input "input:input-b"

            let oppositeGroup =
                handle Types.ConnectionHandleKind.GroupCard ProvenanceSide.Output "output:output-a"

            Expect.equal
                (ConnectionRouting.action valueHandle sameSideGroup)
                None
                "Value chip assignment is handled by value drag/drop, not connector routing."

            Expect.equal
                (ConnectionRouting.action valueHandle oppositeGroup)
                None
                "Value chip handles should not connect across sides."

            Expect.equal
                (ConnectionRouting.action headerHandle sameSideGroup)
                None
                "Property headers have no drag connection; their connectors derive from model data."

        testCase "member resolution prompt can be converted into a manual resolution layer"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let layer = Session.activeLayer session

            let pending: Types.PendingMemberResolution = {
                LayerId = layer.Id
                InputGroupId = "input:Species=Arabidopsis"
                OutputGroupId = "output:Analysis=Mass Spectrometry"
                InputMemberCount = 3
                OutputMemberCount = 1
            }

            let requested = State.init session |> State.MemberResolution.request pending

            Expect.equal
                requested.PendingMemberResolution
                (Some pending)
                "Mismatched group drops should store a pending user choice."

            let manual = requested |> State.MemberResolution.chooseManual pending

            Expect.equal manual.PendingMemberResolution None "Choosing manual should close the prompt."

            Expect.equal
                manual.ExpandedGroups
                (Set.ofList [
                    ProvenanceSide.Input, pending.InputGroupId
                    ProvenanceSide.Output, pending.OutputGroupId
                ])
                "Choosing manual should expand exactly the two pending groups."

            Expect.equal manual.Detail None "Choosing manual should clear the detail panel."

        testCase "publish error clears pending prompts but keeps the error"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let layer = Session.activeLayer session

            let pendingBatch: Types.PendingAssignmentBatch = {
                Batch = { Adds = []; Overwrites = [] }
                AffectedSideCount = 1
                AffectedValueCount = 1
                AffectedGroupCount = 1
                AffectedEntityCount = 1
            }

            let pendingResolution: Types.PendingMemberResolution = {
                LayerId = layer.Id
                InputGroupId = "input:Species=Arabidopsis"
                OutputGroupId = "output:Analysis=Mass Spectrometry"
                InputMemberCount = 1
                OutputMemberCount = 1
            }

            let state =
                State.init session
                |> State.AssignmentBatch.set pendingBatch
                |> State.MemberResolution.request pendingResolution

            let next = State.Publish.onError "boom" state

            Expect.isNone next.PendingAssignmentBatch "An assignment-batch prompt must not survive a failed publish."
            Expect.isNone next.PendingMemberResolution "A member-resolution prompt must not survive a failed publish."
            Expect.equal next.Error (Some "boom") "The error message itself must still be shown."

        testCase "publish success clears pending prompts and re-syncs derived state"
        <| fun _ ->
            let session = Session.init (sampleModel ())

            let pendingBatch: Types.PendingAssignmentBatch = {
                Batch = { Adds = []; Overwrites = [] }
                AffectedSideCount = 1
                AffectedValueCount = 1
                AffectedGroupCount = 1
                AffectedEntityCount = 1
            }

            let state =
                State.init session
                |> State.AssignmentBatch.set pendingBatch
                |> fun state -> {
                    state with
                        Error = Some "stale"
                        Hint = Some "stale"
                }

            let next = State.Publish.onSuccess session state

            Expect.isNone next.PendingAssignmentBatch "A confirmed batch must not remain pending after success."
            Expect.isNone next.Error "A successful publish must clear a previous error."
            Expect.isNone next.Hint "A successful publish must clear a previous hint."

        testCase "property value drop plan adds only when every group member has no value for the property"
        <| fun _ ->
            let species = propertyHeader FixtureKinds.characteristicProperty "Species"
            let treatment = propertyHeader FixtureKinds.characteristicProperty "Treatment"
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"

            let model =
                model
                    "assay-table"
                    [
                        propertyValue "pv-input-a-species" species (ProvenanceValue.Text "Arabidopsis") None None
                    ]
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" [ "pv-input-a-species" ]
                        inputSet "input-b" "assay-table" inputHeader "Input B" []
                    ] [] []

            let group: DisplayGroup = {
                Id = "manual"
                TableName = "assay-table"
                Side = ProvenanceSide.Input
                GroupingValues = []
                Members = [
                    {
                        SetId = "input-a"
                        Name = "Input A"
                        PropertyValueIds = [ "pv-input-a-species" ]
                    }
                    {
                        SetId = "input-b"
                        Name = "Input B"
                        PropertyValueIds = []
                    }
                ]
            }

            let source: Types.ValueAssignmentSource = {
                CopiedFrom = None
                Property = propertyKeyIn model treatment
                Value = ProvenanceValue.Text "Drought"
                Unit = None
            }

            match ValueAssignment.planPropertyValueDrop source group model with
            | Ok(Types.ValueAssignmentPlan.AddCurrent command) ->
                Expect.equal
                    command.Target
                    (ProvenancePropertyTarget.InputSets [ "input-a"; "input-b" ])
                    "Add plan should target all current group members."

                Expect.equal command.Header treatment "Add plan should preserve the dropped property."
            | other -> failwithf "Expected an add plan, got %A" other

            let speciesKey = propertyKeyIn model species

            let mixedSource: Types.ValueAssignmentSource = { source with Property = speciesKey }

            match ValueAssignment.planPropertyValueDrop mixedSource group model with
            | Error(Types.ValueAssignmentError.MixedPropertyValueCounts property) ->
                Expect.equal property speciesKey "Mixed zero/one members should reject the drop for that property."
            | other -> failwithf "Expected a mixed-count rejection, got %A" other

        testCase "property value drop plan warns when the exact property has one distinct assigned value"
        <| fun _ ->
            let species = propertyHeader FixtureKinds.characteristicProperty "Species"
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"

            let model =
                model
                    "assay-table"
                    [
                        propertyValue "pv-input-a-species" species (ProvenanceValue.Text "Arabidopsis") None None
                        propertyValue "pv-input-b-species" species (ProvenanceValue.Text "Arabidopsis") None None
                    ]
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" [ "pv-input-a-species" ]
                        inputSet "input-b" "assay-table" inputHeader "Input B" [ "pv-input-b-species" ]
                    ] [] []

            let group: DisplayGroup = {
                Id = "manual"
                TableName = "assay-table"
                Side = ProvenanceSide.Input
                GroupingValues = []
                Members = [
                    {
                        SetId = "input-a"
                        Name = "Input A"
                        PropertyValueIds = [ "pv-input-a-species" ]
                    }
                    {
                        SetId = "input-b"
                        Name = "Input B"
                        PropertyValueIds = [ "pv-input-b-species" ]
                    }
                ]
            }

            let source: Types.ValueAssignmentSource = {
                CopiedFrom = None
                Property = propertyKeyIn model species
                Value = ProvenanceValue.Text "A. thaliana"
                Unit = None
            }

            match ValueAssignment.planPropertyValueDrop source group model with
            | Ok(Types.ValueAssignmentPlan.ConfirmOverwrite warning) ->
                Expect.equal
                    warning.ExistingValueIds
                    [ "pv-input-a-species"; "pv-input-b-species" ]
                    "Overwrite warning should target the one editable value per member."

                Expect.equal warning.Header species "Warning should keep the overwritten property."
            | other -> failwithf "Expected an overwrite warning, got %A" other

        testCase "property value drop plan rejects members with multiple values for the same property"
        <| fun _ ->
            let species = propertyHeader FixtureKinds.characteristicProperty "Species"
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"

            let model =
                model
                    "assay-table"
                    [
                        propertyValue "pv-input-a-species" species (ProvenanceValue.Text "Arabidopsis") None None
                        propertyValue "pv-input-b-species-a" species (ProvenanceValue.Text "Arabidopsis") None None
                        propertyValue "pv-input-b-species-b" species (ProvenanceValue.Text "Chlamydomonas") None None
                    ]
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" [ "pv-input-a-species" ]
                        inputSet "input-b" "assay-table" inputHeader "Input B" [
                            "pv-input-b-species-a"
                            "pv-input-b-species-b"
                        ]
                    ] [] []

            let group: DisplayGroup = {
                Id = "manual"
                TableName = "assay-table"
                Side = ProvenanceSide.Input
                GroupingValues = []
                Members = [
                    {
                        SetId = "input-a"
                        Name = "Input A"
                        PropertyValueIds = [ "pv-input-a-species" ]
                    }
                    {
                        SetId = "input-b"
                        Name = "Input B"
                        PropertyValueIds = [ "pv-input-b-species-a"; "pv-input-b-species-b" ]
                    }
                ]
            }

            let source: Types.ValueAssignmentSource = {
                CopiedFrom = None
                Property = propertyKeyIn model species
                Value = ProvenanceValue.Text "A. thaliana"
                Unit = None
            }

            match ValueAssignment.planPropertyValueDrop source group model with
            | Error(Types.ValueAssignmentError.MultiplePropertyValues(property, setIds)) ->
                Expect.equal
                    property
                    (propertyKeyIn model species)
                    "The rejection should retain exact property identity."

                Expect.equal setIds [ "input-a"; "input-b" ] "The rejection should identify the assigned targets."
            | other -> failwithf "Expected a multiple-value rejection, got %A" other

        testCase "property value drop plan rejects members whose single values conflict across the group"
        <| fun _ ->
            let species = propertyHeader FixtureKinds.characteristicProperty "Species"
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"

            let model =
                model
                    "assay-table"
                    [
                        propertyValue "pv-input-a-species" species (ProvenanceValue.Text "Arabidopsis") None None
                        propertyValue "pv-input-b-species" species (ProvenanceValue.Text "Chlamydomonas") None None
                    ]
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" [ "pv-input-a-species" ]
                        inputSet "input-b" "assay-table" inputHeader "Input B" [ "pv-input-b-species" ]
                    ] [] []

            let group: DisplayGroup = {
                Id = "manual"
                TableName = "assay-table"
                Side = ProvenanceSide.Input
                GroupingValues = []
                Members = [
                    {
                        SetId = "input-a"
                        Name = "Input A"
                        PropertyValueIds = [ "pv-input-a-species" ]
                    }
                    {
                        SetId = "input-b"
                        Name = "Input B"
                        PropertyValueIds = [ "pv-input-b-species" ]
                    }
                ]
            }

            let source: Types.ValueAssignmentSource = {
                CopiedFrom = None
                Property = propertyKeyIn model species
                Value = ProvenanceValue.Text "A. thaliana"
                Unit = None
            }

            // Intentionally stricter than the pre-origin-aware planner: each member
            // has exactly one value, but no single existing group value can be
            // replaced safely, so the drop is rejected instead of confirmed.
            match ValueAssignment.planPropertyValueDrop source group model with
            | Error(Types.ValueAssignmentError.MultiplePropertyValues(property, setIds)) ->
                Expect.equal
                    property
                    (propertyKeyIn model species)
                    "The rejection should retain exact property identity."

                Expect.equal
                    setIds
                    [ "input-a"; "input-b" ]
                    "Every member holding a conflicting value should be identified."
            | other -> failwithf "Expected a cross-member conflict rejection, got %A" other

        testCase "property value drop plan overwrites one distinct value represented by several occurrence IDs"
        <| fun _ ->
            let temperature = propertyHeader FixtureKinds.parameterProperty "Temperature"
            let treatment = propertyHeader FixtureKinds.characteristicProperty "Treatment"
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"

            let model =
                model
                    "growth"
                    [
                        propertyValue "pv-temperature-a" temperature (ProvenanceValue.Text "20") None None
                        propertyValue "pv-temperature-b" temperature (ProvenanceValue.Text "20") None None
                        propertyValue "pv-treatment" treatment (ProvenanceValue.Text "Heat") None None
                    ]
                    [
                        inputSet "input-a" "growth" inputHeader "Input A" [
                            "pv-temperature-a"
                            "pv-temperature-b"
                            "pv-treatment"
                        ]
                    ] [] []

            let group: DisplayGroup = {
                Id = "manual"
                TableName = "growth"
                Side = ProvenanceSide.Input
                GroupingValues = []
                Members = [
                    {
                        SetId = "input-a"
                        Name = "Input A"
                        PropertyValueIds = [ "pv-temperature-a"; "pv-temperature-b"; "pv-treatment" ]
                    }
                ]
            }

            let source: Types.ValueAssignmentSource = {
                CopiedFrom = None
                Property = propertyKeyIn model temperature
                Value = ProvenanceValue.Text "21"
                Unit = None
            }

            match ValueAssignment.planPropertyValueDrop source group model with
            | Ok(Types.ValueAssignmentPlan.ConfirmOverwrite warning) ->
                Expect.equal
                    warning.ExistingValueIds
                    [ "pv-temperature-a"; "pv-temperature-b" ]
                    "All IDs representing the one assigned value should be updated."
            | other -> failwithf "Expected an overwrite warning, got %A" other

        testCase "property value drop plan ignores the same header from another origin"
        <| fun _ ->
            let temperature = propertyHeader FixtureKinds.parameterProperty "Temperature"
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"

            let growthValue =
                propertyValue
                    "pv-growth-temperature"
                    temperature
                    (ProvenanceValue.Text "20")
                    None
                    (Some(anchor "growth" None temperature [ "Input A" ] []))

            let cultivationValue =
                propertyValue
                    "pv-cultivation-temperature"
                    temperature
                    (ProvenanceValue.Text "30")
                    None
                    (Some(anchor "cultivation" None temperature [ "Input A" ] []))

            let model =
                model
                    "assay-table"
                    [ growthValue; cultivationValue ]
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" [ growthValue.Id; cultivationValue.Id ]
                    ] [] []

            let group: DisplayGroup = {
                Id = "manual"
                TableName = "assay-table"
                Side = ProvenanceSide.Input
                GroupingValues = []
                Members = [
                    {
                        SetId = "input-a"
                        Name = "Input A"
                        PropertyValueIds = [ growthValue.Id; cultivationValue.Id ]
                    }
                ]
            }

            let source: Types.ValueAssignmentSource = {
                CopiedFrom = None
                Property = ProvenancePropertyValue.propertyKey growthValue
                Value = ProvenanceValue.Text "21"
                Unit = None
            }

            match ValueAssignment.planPropertyValueDrop source group model with
            | Ok(Types.ValueAssignmentPlan.ConfirmOverwrite warning) ->
                Expect.equal
                    warning.ExistingValueIds
                    [ growthValue.Id ]
                    "Cultivation/Temperature must not block or join Growth/Temperature overwrite."
            | other -> failwithf "Expected an origin-specific overwrite warning, got %A" other

        testCase "upstream property values cannot create new assignments in a downstream layer"
        <| fun _ ->
            let temperature = propertyHeader FixtureKinds.parameterProperty "Temperature"
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"

            let model =
                model
                    "cultivation"
                    []
                    [
                        inputSet "input-a" "cultivation" inputHeader "Input A" []
                    ] [] []

            let group: DisplayGroup = {
                Id = "manual"
                TableName = "cultivation"
                Side = ProvenanceSide.Input
                GroupingValues = []
                Members = [
                    {
                        SetId = "input-a"
                        Name = "Input A"
                        PropertyValueIds = []
                    }
                ]
            }

            let upstreamProperty = propertyKey "growth" temperature

            let source: Types.ValueAssignmentSource = {
                CopiedFrom = None
                Property = upstreamProperty
                Value = ProvenanceValue.Text "21"
                Unit = None
            }

            match ValueAssignment.planPropertyValueDrop source group model with
            | Error(Types.ValueAssignmentError.UpstreamPropertyNotAssigned property) ->
                Expect.equal property upstreamProperty "The rejection should identify the upstream property."
            | other -> failwithf "Expected an upstream-add rejection, got %A" other

        testCase "current property can be assigned despite the same header from another origin"
        <| fun _ ->
            let temperature = propertyHeader FixtureKinds.parameterProperty "Temperature"
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"

            let growthValue =
                propertyValue
                    "pv-growth-temperature"
                    temperature
                    (ProvenanceValue.Text "20")
                    None
                    (Some(anchor "growth" None temperature [ "Input A" ] []))

            let model =
                model
                    "cultivation"
                    [ growthValue ]
                    [
                        inputSet "input-a" "cultivation" inputHeader "Input A" [ growthValue.Id ]
                    ] [] []

            let group: DisplayGroup = {
                Id = "manual"
                TableName = "cultivation"
                Side = ProvenanceSide.Input
                GroupingValues = []
                Members = [
                    {
                        SetId = "input-a"
                        Name = "Input A"
                        PropertyValueIds = [ growthValue.Id ]
                    }
                ]
            }

            let currentProperty = propertyKey "cultivation" temperature

            let source: Types.ValueAssignmentSource = {
                CopiedFrom = None
                Property = currentProperty
                Value = ProvenanceValue.Text "30"
                Unit = None
            }

            match ValueAssignment.planPropertyValueDrop source group model with
            | Ok(Types.ValueAssignmentPlan.AddCurrent command) ->
                Expect.equal command.Header temperature "The current property should remain assignable."
            | other -> failwithf "Expected a current-layer add plan, got %A" other

        testCase "palette value added from an upstream property keeps that property origin"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let layer = Session.activeLayer session
            let state = State.init session
            let analysis = propertyHeader FixtureKinds.parameterProperty "Analysis"
            let upstreamProperty = propertyKey "growth" analysis

            let next =
                State.Palette.addValue
                    layer.Id
                    ProvenanceSide.Input
                    upstreamProperty
                    (ProvenanceValue.Text "Imaging")
                    None
                    state

            let added =
                State.Palette.valuesForProperty layer.Id ProvenanceSide.Input upstreamProperty next
                |> List.exactlyOne

            Expect.equal
                (ProvenancePropertyValue.propertyKey added)
                upstreamProperty
                "The active layer must not become the origin of a replacement value."

        testCase "default state initializes with empty colors and filters"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let state = State.init session

            Expect.equal
                state.PropertyColors
                State.PropertyColors.empty
                "Initial state should have empty property colors."

            Expect.equal state.Filters State.Filters.defaultState "Initial state should have default filters."

        testCase "set and clear property color"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let layer = Session.activeLayer session
            let state = State.init session
            let header = propertyHeader FixtureKinds.characteristicProperty "Species"
            let property = propertyKeyIn layer.Model header
            let context = State.PropertyColors.visibleColorContextForLayer session layer

            let withColor = State.PropertyColors.setColor context.Id property "#2563eb" state

            let key: Types.VisiblePropertyColorKey = {
                ContextId = context.Id
                Property = property
            }

            Expect.equal
                withColor.PropertyColors.ManualPropertyColors.[key]
                "#2563eb"
                "Property color should be set by visible context and header."

            let cleared = State.PropertyColors.clearColor context.Id property withColor

            Expect.isFalse
                (cleared.PropertyColors.ManualPropertyColors.ContainsKey key)
                "Property color should be cleared."

        testCase "set and clear source color"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let sourceId = (Session.activeLayer session).Model.Source.Id
            let state = State.init session

            let withColor = State.PropertyColors.setSourceColor sourceId "#16a34a" state
            Expect.equal withColor.PropertyColors.SourceColors.[sourceId] "#16a34a" "Source color should be set."

            let cleared = State.PropertyColors.clearSourceColor sourceId withColor
            Expect.isFalse (cleared.PropertyColors.SourceColors.ContainsKey sourceId) "Source color should be cleared."

        testCase "source colors are assigned once per live source"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let state = State.init session

            let afterEnsure = State.PropertyColors.ensureSourceColors session state

            Expect.equal
                afterEnsure.SourceColors.Count
                2
                "The fixture's current and previous sources should receive automatic colors."

            Expect.isTrue
                (afterEnsure.SourceColors.ContainsKey "fixture:assay-table")
                "Initial source should have an automatic color."

            Expect.isTrue
                (afterEnsure.SourceColors.ContainsKey "fixture:previous-study-table")
                "Attached previous source should have an automatic color."

        testCase "automatic colors do not overwrite existing manual source colors"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let sourceId = (Session.activeLayer session).Model.Source.Id
            let state = State.init session

            let withManual = State.PropertyColors.setSourceColor sourceId "#be185d" state
            let afterEnsure = State.PropertyColors.ensureSourceColors session withManual

            Expect.equal afterEnsure.SourceColors.[sourceId] "#be185d" "Manual source color should be preserved."

        testCase "stale source colors are removed during cleanup"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let state = State.init session

            let withStale = {
                state with
                    PropertyColors = {
                        state.PropertyColors with
                            SourceColors = Map.ofList [ "nonexistent", "#000000" ]
                            SourceColorSetOrder = Map.ofList [ "nonexistent", 0 ]
                            NextSourceColorSetOrder = 1
                    }
            }

            let afterEnsure = State.PropertyColors.ensureSourceColors session withStale

            Expect.isFalse (afterEnsure.SourceColors.ContainsKey "nonexistent") "Stale source color should be removed."

            Expect.isFalse
                (afterEnsure.SourceColorSetOrder.ContainsKey "nonexistent")
                "Stale source color order should be removed."

        testCase "setting a shelf folder color for one duplicate-named layer leaves the other alone"
        <| fun _ ->
            let withLayerTwo =
                match
                    Session.addLayer
                        {
                            Name = "Processing"
                            SelectedSets = []
                        }
                        (Session.init (sampleModel ()))
                with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected addLayer error: %A" error

            let onLayerOne =
                match Session.selectLayer "layer-1" withLayerTwo with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected selectLayer error: %A" error

            let session =
                match
                    Session.addLayer
                        {
                            Name = "Processing"
                            SelectedSets = []
                        }
                        onLayerOne
                with
                | Ok(next, _) -> next
                | Error error -> failwithf "Unexpected addLayer error: %A" error

            let layerTwo = Session.layerById "layer-2" session
            let layerThree = Session.layerById "layer-3" session

            // Sides.ensure auto-assigns every live source a distinct default color;
            // capture layer-3's before recoloring layer-2 so the assertion below
            // catches the two layers sharing one color slot, not just a `None`.
            let uiState = State.init session |> State.Sides.ensure session

            let colorOf (state: Types.UiState) sourceId =
                state.PropertyColors.SourceColors |> Map.tryFind sourceId

            let layerThreeColorBefore = colorOf uiState layerThree.Model.Source.Id

            let folderId = PropertyFolders.sourceFolderId layerTwo.Model.Source

            let recolored =
                PropertyShelf.setFolderColor session folderId (Some "#ff0000") uiState

            Expect.equal
                (colorOf recolored layerTwo.Model.Source.Id)
                (Some "#ff0000")
                "The targeted duplicate-named layer's folder should receive the color."

            Expect.equal
                (colorOf recolored layerThree.Model.Source.Id)
                layerThreeColorBefore
                "The other same-named layer's automatic color must not change."

        testCase "default filter state uses Any filter and ValueCountDesc sort"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let state = State.init session

            Expect.equal state.Filters.SearchText "" "Default search should be empty."

            Expect.equal
                state.Filters.PropertySort
                Types.PropertySort.ValueCountDesc
                "Default sort should be value count desc."

            Expect.equal
                state.Filters.ValueCountFilter
                Types.PropertyValueCountFilter.Any
                "Default value count filter should be Any."

        testCase "filters can be updated through state helpers"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let state = State.init session

            let withSearch = State.Filters.setSearch "Arabidopsis" state
            Expect.equal withSearch.Filters.SearchText "Arabidopsis" "Search text should be updated."

            let withSort = State.Filters.setPropertySort Types.PropertySort.NameAsc withSearch
            Expect.equal withSort.Filters.PropertySort Types.PropertySort.NameAsc "Property sort should be updated."

            let withFilter =
                State.Filters.setValueCountFilter Types.PropertyValueCountFilter.Multiple withSort

            Expect.equal
                withFilter.Filters.ValueCountFilter
                Types.PropertyValueCountFilter.Multiple
                "Value count filter should be updated."

            let withOrigin =
                State.Filters.setOriginFilter Types.PropertyOriginFilter.CurrentOnly withFilter

            Expect.equal
                withOrigin.Filters.OriginFilter
                Types.PropertyOriginFilter.CurrentOnly
                "Origin filter should be updated."

            let withGroupSort =
                State.Filters.setGroupSort Types.GroupSort.MemberCountDesc withOrigin

            Expect.equal withGroupSort.Filters.GroupSort Types.GroupSort.MemberCountDesc "Group sort should be updated."

        testCase "value count sort keeps distinct value count as primary key"
        <| fun _ ->
            let highCount = propertyHeader (ProvenanceKind.create "z-kind" "Zeta kind") "Zeta"
            let lowCount = propertyHeader (ProvenanceKind.create "a-kind" "Alpha kind") "Alpha"
            let highCountKey = propertyKey "assay-table" highCount
            let lowCountKey = propertyKey "assay-table" lowCount

            let stats =
                Map.ofList [
                    highCountKey,
                    ({
                        Property = highCountKey
                        DistinctValueCount = 3
                        SetsWithValueCount = 3
                        TotalSetCount = 3
                    }
                    : Types.PropertyStats)
                    lowCountKey,
                    ({
                        Property = lowCountKey
                        DistinctValueCount = 1
                        SetsWithValueCount = 1
                        TotalSetCount = 1
                    }
                    : Types.PropertyStats)
                ]

            let sorted =
                PropertyProjection.sortHeaders Types.PropertySort.ValueCountDesc stats Map.empty [
                    lowCountKey
                    highCountKey
                ]

            Expect.equal sorted [ highCountKey; lowCountKey ] "Higher value count should sort before name/kind."

        testCase "rail projection applies search and resolves manual property color"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let layer = Session.activeLayer session
            let species = propertyHeader FixtureKinds.characteristicProperty "Species"
            let speciesKey = propertyKeyIn layer.Model species
            let context = State.PropertyColors.visibleColorContextForLayer session layer

            let uiState =
                State.init session
                |> State.Filters.setSearch "Arabidopsis"
                |> State.PropertyColors.setColor context.Id speciesKey "#2563eb"

            let projection =
                PropertyProjection.railProjectionWithFilters session layer.Id ProvenanceSide.Output layer.Model uiState

            Expect.isTrue
                (projection.Headers |> List.contains speciesKey)
                "Search should keep headers with matching projected values."

            Expect.equal projection.ColorByHeader.[speciesKey] (Some "#2563eb") "Manual color should be projected."

            let nonMatching =
                uiState |> State.Filters.setSearch "definitely-not-a-provenance-value"

            let emptyProjection =
                PropertyProjection.railProjectionWithFilters
                    session
                    layer.Id
                    ProvenanceSide.Output
                    layer.Model
                    nonMatching

            Expect.isEmpty emptyProjection.Headers "Search should remove non-matching property headers."

        testCase "source colors are used for single-origin rail headers"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let layer = Session.activeLayer session

            let state =
                let state = State.init session

                {
                    state with
                        PropertyColors = State.PropertyColors.ensureSourceColors session state
                }

            let previousTreatment =
                propertyHeader FixtureKinds.characteristicProperty "Previous Treatment"

            let previousTreatmentKey = propertyKeyIn layer.Model previousTreatment

            let projection =
                PropertyProjection.railProjectionWithFilters session layer.Id ProvenanceSide.Input layer.Model state

            Expect.isTrue
                (projection.Headers |> List.contains previousTreatmentKey)
                "Attached previous context should still appear in the property rail."

            Expect.equal
                projection.ColorByHeader.[previousTreatmentKey]
                (state.PropertyColors.SourceColors |> Map.tryFind "fixture:previous-study-table")
                "Single-origin headers should use their source color."

        testCase "manual visible property color overrides only that property"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let layer = Session.activeLayer session
            let species = propertyHeader FixtureKinds.characteristicProperty "Species"
            let temperature = propertyHeader FixtureKinds.parameterProperty "Temperature"
            let speciesKey = propertyKeyIn layer.Model species
            let temperatureKey = propertyKeyIn layer.Model temperature
            let context = State.PropertyColors.visibleColorContextForLayer session layer

            let state =
                State.init session
                |> State.PropertyColors.setSourceColor layer.Model.Source.Id "#16a34a"
                |> State.PropertyColors.setColor context.Id speciesKey "#2563eb"

            let projection =
                PropertyProjection.railProjectionWithFilters session layer.Id ProvenanceSide.Output layer.Model state

            Expect.equal projection.ColorByHeader.[speciesKey] (Some "#2563eb") "Manual color should win for Species."

            Expect.equal
                projection.ColorByHeader.[temperatureKey]
                (Some "#16a34a")
                "Other current-source headers should keep the source color."

        testCase "same header from different origins projects as separate colored properties"
        <| fun _ ->
            let species = propertyHeader FixtureKinds.characteristicProperty "Species"
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"

            let built =
                model
                    "assay-table"
                    [
                        propertyValue
                            "pv-current-species"
                            species
                            (ProvenanceValue.Text "Arabidopsis")
                            None
                            (Some(anchor "assay-table" None species [ "Input A" ] []))
                        propertyValue
                            "pv-previous-species"
                            species
                            (ProvenanceValue.Text "Arabidopsis")
                            None
                            (Some(anchor "previous-table" None species [ "Ancestor A" ] []))
                    ]
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" [
                            "pv-current-species"
                            "pv-previous-species"
                        ]
                    ] [] []

            let session = Session.init built
            let layer = Session.activeLayer session

            let state =
                State.init session
                |> State.PropertyColors.setSourceColor layer.Model.Source.Id "#2563eb"
                |> State.PropertyColors.setSourceColor "previous-table" "#be185d"

            let projection =
                PropertyProjection.railProjectionWithFilters session layer.Id ProvenanceSide.Input layer.Model state

            let currentKey = propertyKey "assay-table" species
            let previousKey = propertyKey "previous-table" species

            Expect.equal
                (projection.Headers
                 |> List.filter (fun property -> property.Header = species)
                 |> Set.ofList)
                (Set.ofList [ currentKey; previousKey ])
                "Same-named properties from different overall sources must remain separate rail rows."

            Expect.equal
                projection.ColorByHeader.[currentKey]
                (Some "#2563eb")
                "The current-source property should keep its own source color."

            Expect.equal
                projection.ColorByHeader.[previousKey]
                (Some "#be185d")
                "The upstream property should keep its own source color."

        testCase "same header shares manual color in downstream connected layer"
        <| fun _ ->
            let layered =
                Session.init (sampleModel ())
                |> Session.addLayer {
                    Name = "Layer 2"
                    SelectedSets = [ ProvenanceSide.Output, "output-a" ]
                }
                |> function
                    | Ok(next, _) -> next
                    | Error error -> failwithf "Unexpected addLayer error: %A" error

            let layer1 = Session.layerById "layer-1" layered
            let layer2 = Session.layerById "layer-2" layered
            let analysis = propertyHeader FixtureKinds.parameterProperty "Analysis"
            let analysisKey = propertyKeyIn layer1.Model analysis
            let context = State.PropertyColors.visibleColorContextForLayer layered layer2

            let state =
                State.init layered
                |> State.PropertyColors.setColor context.Id analysisKey "#2563eb"

            let upstreamProjection =
                PropertyProjection.railProjectionWithFilters layered layer1.Id ProvenanceSide.Output layer1.Model state

            let downstreamProjection =
                PropertyProjection.railProjectionWithFilters layered layer2.Id ProvenanceSide.Input layer2.Model state

            Expect.equal
                upstreamProjection.ColorByHeader.[analysisKey]
                (Some "#2563eb")
                "Upstream visible header should use the manual color."

            Expect.equal
                downstreamProjection.ColorByHeader.[analysisKey]
                (Some "#2563eb")
                "Connected downstream visible header should share the root manual color."

        testCase "same header can have different manual colors in independent visible color contexts"
        <| fun _ ->
            let baseSession = Session.init (sampleModel ())
            let layer1 = Session.activeLayer baseSession
            let independentModel = inputOnlyModel ()

            let independentLayer = {
                Id = "layer-2"
                Label = "Independent"
                InputSideId = "layer-2-input"
                OutputSideId = "layer-2-output"
                Model = independentModel
            }

            let session = {
                baseSession with
                    Layers = baseSession.Layers @ [ independentLayer ]
                    LayerOrder = baseSession.LayerOrder @ [ independentLayer.Id ]
                    ActiveLayerId = independentLayer.Id
                    ReferenceLinks = []
            }

            let species = propertyHeader FixtureKinds.characteristicProperty "Species"
            let layer1Species = propertyKeyIn layer1.Model species
            let independentSpecies = propertyKeyIn independentLayer.Model species

            let state =
                State.init session
                |> State.PropertyColors.setColor layer1.Id layer1Species "#2563eb"
                |> State.PropertyColors.setColor independentLayer.Id independentSpecies "#be185d"

            let layer1Projection =
                PropertyProjection.railProjectionWithFilters session layer1.Id ProvenanceSide.Output layer1.Model state

            let independentProjection =
                PropertyProjection.railProjectionWithFilters
                    session
                    independentLayer.Id
                    ProvenanceSide.Input
                    independentLayer.Model
                    state

            Expect.equal
                layer1Projection.ColorByHeader.[layer1Species]
                (Some "#2563eb")
                "Layer 1 should use its own visible-context color."

            Expect.equal
                independentProjection.ColorByHeader.[independentSpecies]
                (Some "#be185d")
                "Independent layer should use its own visible-context color for the same header."

        testCase "current input properties default to output rail when connected to outputs"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let layer = Session.activeLayer session
            let state = State.init session
            let species = propertyHeader FixtureKinds.characteristicProperty "Species"
            let speciesKey = propertyKeyIn layer.Model species

            let inputHeaders =
                (PropertyProjection.railProjectionWithFilters session layer.Id ProvenanceSide.Input layer.Model state)
                    .Headers

            let outputHeaders =
                (PropertyProjection.railProjectionWithFilters session layer.Id ProvenanceSide.Output layer.Model state)
                    .Headers

            Expect.isFalse
                (inputHeaders |> List.contains speciesKey)
                "A connected current input property should not be duplicated on the input rail by default."

            Expect.isTrue
                (outputHeaders |> List.contains speciesKey)
                "A connected current input property should default to the output rail."

        testCase "previous context properties stay on input rail even when inherited by outputs"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let layer = Session.activeLayer session
            let state = State.init session

            let previousTreatment =
                propertyHeader FixtureKinds.characteristicProperty "Previous Treatment"

            let previousTreatmentKey = propertyKeyIn layer.Model previousTreatment

            let inputHeaders =
                (PropertyProjection.railProjectionWithFilters session layer.Id ProvenanceSide.Input layer.Model state)
                    .Headers

            let outputHeaders =
                (PropertyProjection.railProjectionWithFilters session layer.Id ProvenanceSide.Output layer.Model state)
                    .Headers

            Expect.isTrue
                (inputHeaders |> List.contains previousTreatmentKey)
                "A previous-context property should stay on the input rail."

            Expect.isFalse
                (outputHeaders |> List.contains previousTreatmentKey)
                "A previous-context property should not move to the output rail just because it is inherited."

        testCase "current input properties remain on input rail until an output is designated"
        <| fun _ ->
            let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"
            let species = propertyHeader FixtureKinds.characteristicProperty "Species"

            let model =
                model
                    "assay-table"
                    [
                        propertyValue "pv-input-a-species" species (ProvenanceValue.Text "Arabidopsis") None None
                    ]
                    [
                        inputSet "input-a" "assay-table" inputHeader "Input A" [ "pv-input-a-species" ]
                    ] [] []

            let session = Session.init model
            let layer = Session.activeLayer session
            let state = State.init session
            let speciesKey = propertyKeyIn layer.Model species

            let inputHeaders =
                (PropertyProjection.railProjectionWithFilters session layer.Id ProvenanceSide.Input layer.Model state)
                    .Headers

            let outputHeaders =
                (PropertyProjection.railProjectionWithFilters session layer.Id ProvenanceSide.Output layer.Model state)
                    .Headers

            Expect.isTrue
                (inputHeaders |> List.contains speciesKey)
                "An incomplete layer with no output should keep current properties on the input rail."

            Expect.isFalse
                (outputHeaders |> List.contains speciesKey)
                "A property should not move to the output rail before any output is designated."

        testCase "selected drop targets include selected inputs and outputs only when dropped group is selected"
        <| fun _ ->
            let inputGroup: DisplayGroup = {
                Id = "input-group"
                TableName = "assay-table"
                Side = ProvenanceSide.Input
                GroupingValues = []
                Members = []
            }

            let outputGroup: DisplayGroup = {
                inputGroup with
                    Id = "output-group"
                    Side = ProvenanceSide.Output
            }

            let findGroup side groupId =
                match side, groupId with
                | ProvenanceSide.Input, "input-group" -> Some inputGroup
                | ProvenanceSide.Output, "output-group" -> Some outputGroup
                | _ -> None

            let selectedInputs = Set.ofList [ "layer-1", "input-group" ]
            let selectedOutputs = Set.ofList [ "layer-1", "output-group" ]

            let selectedTargets =
                ValueAssignment.selectedTargetGroupsForDrop
                    "layer-1"
                    ProvenanceSide.Input
                    "input-group"
                    selectedInputs
                    selectedOutputs
                    findGroup

            Expect.equal
                (selectedTargets |> List.map (fun (group: DisplayGroup) -> group.Side, group.Id))
                [
                    ProvenanceSide.Input, "input-group"
                    ProvenanceSide.Output, "output-group"
                ]
                "Dropping onto a selected group should target selected groups on both sides."

            let unselectedDrop =
                ValueAssignment.selectedTargetGroupsForDrop
                    "layer-1"
                    ProvenanceSide.Input
                    "unselected-input"
                    selectedInputs
                    selectedOutputs
                    (fun side groupId ->
                        if side = ProvenanceSide.Input && groupId = "unselected-input" then
                            Some {
                                inputGroup with
                                    Id = "unselected-input"
                            }
                        else
                            findGroup side groupId
                    )

            Expect.equal
                (unselectedDrop |> List.map (fun (group: DisplayGroup) -> group.Id))
                [ "unselected-input" ]
                "Dropping onto an unselected group should keep single-target behavior."
    ]

let sourceTests =
    testList "Source and origin" [
        testCase "property source info exposes table and process metadata"
        <| fun _ ->
            let session = Session.init (sampleModel ())
            let layer = Session.activeLayer session
            let value = layer.Model.PropertyValues.["pv-input-a-species"]
            let source = Session.propertyValueSourceInfo layer value

            match source with
            | Some source ->
                Expect.equal source.TableName (Some "assay-table") "Origin source name should be exposed."
                Expect.equal source.ProcessName (Some "assay-process") "Origin process name should be exposed."
                Expect.isTrue source.IsCurrentTable "Fixture value should belong to the current source."
            | None -> failwith "A fixture property value should expose source metadata."

        testCase "property origin in session returns stored source origin"
        <| fun _ ->
            let session = Session.init (sampleModel ())

            match
                Session.addLayer
                    {
                        Name = "Layer 2"
                        SelectedSets = [ ProvenanceSide.Output, "output-a" ]
                    }
                    session
            with
            | Ok(layered, _) ->
                let active = Session.activeLayer layered
                let propertyValueId = active.Model.PropertyValues |> Map.toList |> List.head |> fst

                let origin =
                    Session.propertyValueOriginInSession active.Id ProvenanceSide.Input propertyValueId layered

                Expect.equal
                    origin
                    (active.Model.PropertyValues
                     |> Map.tryFind propertyValueId
                     |> Option.map _.Origin)
                    "Origin lookup should return the stored real or virtual source origin."
            | Error error -> failwithf "Unexpected addLayer error: %A" error
    ]

let tests =
    testList "ProvenanceGrouping" [
        typeTests
        modelTests
        groupingTests
        editTests
        fixtureTests
        sessionTests
        sourceTests
        uiStateTests
    ]
