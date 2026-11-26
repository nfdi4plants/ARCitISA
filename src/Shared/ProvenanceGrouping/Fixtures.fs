module Arcitisa.Components.Shared.ProvenanceGrouping.Fixtures

open Arcitisa.Components.Shared.ProvenanceGrouping.Types
open Arcitisa.Components.Shared.ProvenanceGrouping.Session

let term name = {
    Name = name
    TermSource = None
    TermAccession = None
}

module FixtureKinds =

    let endpoint id label =
        ProvenanceKind.create $"fixture:endpoint:{id}" label

    let property id label =
        ProvenanceKind.create $"fixture:property:{id}" label

    let sampleEndpoint = endpoint "sample" "Sample"
    let dataEndpoint = endpoint "data" "Data"
    let characteristicProperty = property "characteristic" "Characteristic"
    let factorProperty = property "factor" "Factor"
    let parameterProperty = property "parameter" "Parameter"
    let componentProperty = property "component" "Component"

let ioHeader kind text = { Kind = kind; Text = text }

let propertyHeader kind name = { Kind = kind; Category = term name }

let source id name : ProvenanceSourceRef = { Id = id; Name = name }

let anchor source processName header inputNames outputNames : ProvenanceWritebackAnchor = {
    Source = source
    ProcessId = None
    ProcessName = processName
    Header = header
    InputNames = inputNames
    OutputNames = outputNames
}

let real source processName header inputNames outputNames =
    ProvenancePropertyOrigin.Real(anchor source processName header inputNames outputNames)

let virtualOrigin source processName header inputNames outputNames =
    ProvenancePropertyOrigin.Virtual(anchor source processName header inputNames outputNames)

let propertyValue id header value unit origin : ProvenancePropertyValue = {
    Id = id
    Header = header
    Value = value
    Unit = unit
    Origin = origin
}

let inputSet id source header name propertyValueIds : ProvenanceSet = {
    Id = id
    Source = source
    Header = header
    Name = name
    PropertyValueIds = propertyValueIds
    InheritedPropertyValueIds = Map.empty
}

let outputSet id source header name propertyValueIds : ProvenanceSet = {
    Id = id
    Source = source
    Header = header
    Name = name
    PropertyValueIds = propertyValueIds
    InheritedPropertyValueIds = Map.empty
}

let connection id source processName inputSetId outputSetId : ProvenanceConnection = {
    Id = id
    Source = source
    ProcessId = None
    ProcessName = processName
    InputSetId = inputSetId
    OutputSetId = outputSetId
}

let model
    (source: ProvenanceSourceRef)
    (propertyValues: ProvenancePropertyValue list)
    (inputSets: ProvenanceSet list)
    (outputSets: ProvenanceSet list)
    (connections: ProvenanceConnection list)
    : ProvenanceModel =
    {
        Source = source
        PropertyValues = propertyValues |> List.map (fun value -> value.Id, value) |> Map.ofList
        InputSets = inputSets |> List.map (fun set -> set.Id, set) |> Map.ofList
        OutputSets = outputSets |> List.map (fun set -> set.Id, set) |> Map.ofList
        Connections =
            connections
            |> List.map (fun connection -> connection.Id, connection)
            |> Map.ofList
    }
    |> ProvenanceModel.refreshInheritedProperties

let sampleModel () : ProvenanceModel =
    let assaySource = source "fixture:assay-table" "assay-table"
    let previousSource = source "fixture:previous-study-table" "previous-study-table"
    let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"
    let outputHeader = ioHeader FixtureKinds.sampleEndpoint "Output [Sample Name]"
    let species = propertyHeader FixtureKinds.characteristicProperty "Species"
    let temperature = propertyHeader FixtureKinds.parameterProperty "Temperature"
    let analysis = propertyHeader FixtureKinds.parameterProperty "Analysis"
    let replicate = propertyHeader FixtureKinds.parameterProperty "Replicate"

    let previousTreatment =
        propertyHeader FixtureKinds.characteristicProperty "Previous Treatment"

    let propertyValues = [
        propertyValue
            "pv-input-a-species"
            species
            (ProvenanceValue.Text "Arabidopsis")
            None
            (real assaySource (Some "assay-process") species [ "Input A" ] [])
        propertyValue
            "pv-input-b-species"
            species
            (ProvenanceValue.Text "Arabidopsis")
            None
            (real assaySource (Some "assay-process") species [ "Input B" ] [])
        propertyValue
            "pv-input-c-species"
            species
            (ProvenanceValue.Text "Arabidopsis")
            None
            (real assaySource (Some "assay-process") species [ "Input C" ] [])
        propertyValue
            "pv-input-d-species"
            species
            (ProvenanceValue.Text "Chlamydomonas")
            None
            (real assaySource (Some "assay-process") species [ "Input D" ] [])
        propertyValue
            "pv-input-a-temperature"
            temperature
            (ProvenanceValue.Text "12 C")
            None
            (real assaySource (Some "assay-process") temperature [ "Input A" ] [])
        propertyValue
            "pv-input-b-temperature"
            temperature
            (ProvenanceValue.Text "12 C")
            None
            (real assaySource (Some "assay-process") temperature [ "Input B" ] [])
        propertyValue
            "pv-input-c-temperature"
            temperature
            (ProvenanceValue.Text "24 C")
            None
            (real assaySource (Some "assay-process") temperature [ "Input C" ] [])
        propertyValue
            "pv-output-a-analysis"
            analysis
            (ProvenanceValue.Text "Mass Spectrometry")
            None
            (real assaySource (Some "assay-process") analysis [] [ "Output A" ])
        propertyValue
            "pv-output-b-analysis"
            analysis
            (ProvenanceValue.Text "Mass Spectrometry")
            None
            (real assaySource (Some "assay-process") analysis [] [ "Output B" ])
        propertyValue
            "pv-output-c-analysis"
            analysis
            (ProvenanceValue.Text "LC-MS")
            None
            (real assaySource (Some "assay-process") analysis [] [ "Output C" ])
        propertyValue
            "pv-output-b-replicate-1"
            replicate
            (ProvenanceValue.Text "1")
            None
            (real assaySource (Some "assay-process") replicate [ "Input A" ] [ "Output B" ])
        propertyValue
            "pv-output-b-replicate-2"
            replicate
            (ProvenanceValue.Text "2")
            None
            (real assaySource (Some "assay-process") replicate [ "Input B" ] [ "Output B" ])
        propertyValue
            "pv-previous-treatment-a"
            previousTreatment
            (ProvenanceValue.Text "Drought")
            None
            (real previousSource (Some "previous-process") previousTreatment [ "Ancestor A" ] [])
    ]

    let inputSets = [
        inputSet "input-a" assaySource inputHeader "Input A" [
            "pv-input-a-species"
            "pv-input-a-temperature"
            "pv-previous-treatment-a"
        ]
        inputSet "input-b" assaySource inputHeader "Input B" [ "pv-input-b-species"; "pv-input-b-temperature" ]
        inputSet "input-c" assaySource inputHeader "Input C" [ "pv-input-c-species"; "pv-input-c-temperature" ]
        inputSet "input-d" assaySource inputHeader "Input D" [ "pv-input-d-species" ]
    ]

    let outputSets = [
        outputSet "output-a" assaySource outputHeader "Output A" [ "pv-output-a-analysis" ]
        outputSet "output-b" assaySource outputHeader "Output B" [
            "pv-output-b-analysis"
            "pv-output-b-replicate-1"
            "pv-output-b-replicate-2"
        ]
        outputSet "output-c" assaySource outputHeader "Output C" [ "pv-output-c-analysis" ]
        outputSet "output-d" assaySource outputHeader "Output D" []
        outputSet "output-e" assaySource outputHeader "Output E" []
    ]

    let connections = [
        connection "connection-a" assaySource (Some "assay-process") "input-a" "output-a"
        connection "connection-b" assaySource (Some "assay-process") "input-a" "output-b"
        connection "connection-c" assaySource (Some "assay-process") "input-b" "output-b"
        connection "connection-d" assaySource (Some "assay-process") "input-c" "output-c"
        connection "connection-e" assaySource (Some "assay-process") "input-d" "output-d"
    ]

    model assaySource propertyValues inputSets outputSets connections

let sampleSession () : ProvenanceSession = sampleModel () |> Session.init

let inputOnlyModel () : ProvenanceModel =
    let inputOnlySource = source "fixture:input-only-table" "input-only-table"
    let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"
    let species = propertyHeader FixtureKinds.characteristicProperty "Species"

    model
        inputOnlySource
        [
            propertyValue
                "pv-input-only-species"
                species
                (ProvenanceValue.Text "Arabidopsis")
                None
                (real inputOnlySource None species [ "Input Only A" ] [])
        ]
        [
            inputSet "input-only-a" inputOnlySource inputHeader "Input Only A" [ "pv-input-only-species" ]
        ] [] []

let outputOnlyModel () : ProvenanceModel =
    let outputOnlySource = source "fixture:output-only-table" "output-only-table"
    let outputHeader = ioHeader FixtureKinds.sampleEndpoint "Output [Sample Name]"
    let analysis = propertyHeader FixtureKinds.parameterProperty "Analysis"

    model
        outputOnlySource
        [
            propertyValue
                "pv-output-only-analysis"
                analysis
                (ProvenanceValue.Text "LC-MS")
                None
                (real outputOnlySource None analysis [] [ "Output Only A" ])
        ]
        [] [
            outputSet "output-only-a" outputOnlySource outputHeader "Output Only A" [ "pv-output-only-analysis" ]
        ] []

let disconnectedPropertyModel () : ProvenanceModel =
    let modelSource =
        source "fixture:disconnected-property-table" "disconnected-property-table"

    let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"
    let outputHeader = ioHeader FixtureKinds.sampleEndpoint "Output [Sample Name]"
    let analysis = propertyHeader FixtureKinds.parameterProperty "Analysis"
    let replicate = propertyHeader FixtureKinds.parameterProperty "Replicate"

    model
        modelSource
        [
            propertyValue
                "pv-disconnected-output-analysis"
                analysis
                (ProvenanceValue.Text "Mass Spectrometry")
                None
                (real modelSource None analysis [] [ "Disconnected Output" ])
            propertyValue
                "pv-disconnected-output-replicate"
                replicate
                (ProvenanceValue.Text "1")
                None
                (real modelSource None replicate [] [ "Disconnected Output" ])
        ]
        [
            inputSet "disconnected-input" modelSource inputHeader "Disconnected Input" []
        ] [
            outputSet "disconnected-output" modelSource outputHeader "Disconnected Output" [
                "pv-disconnected-output-analysis"
                "pv-disconnected-output-replicate"
            ]
        ] []

let switchablePropertyModel () : ProvenanceModel =
    let switchableSource = source "fixture:switchable-table" "switchable-table"
    let inputHeader = ioHeader FixtureKinds.sampleEndpoint "Input [Sample Name]"
    let outputHeader = ioHeader FixtureKinds.sampleEndpoint "Output [Sample Name]"
    let batch = propertyHeader FixtureKinds.parameterProperty "Batch"

    model
        switchableSource
        [
            propertyValue
                "pv-input-a-batch"
                batch
                (ProvenanceValue.Text "A")
                None
                (real switchableSource None batch [ "Input A" ] [])
            propertyValue
                "pv-output-a-batch"
                batch
                (ProvenanceValue.Text "A")
                None
                (real switchableSource None batch [] [ "Output A" ])
            propertyValue
                "pv-output-b-batch"
                batch
                (ProvenanceValue.Text "B")
                None
                (real switchableSource None batch [] [ "Output B" ])
        ]
        [
            inputSet "input-a" switchableSource inputHeader "Input A" [ "pv-input-a-batch" ]
            inputSet "input-b" switchableSource inputHeader "Input B" []
        ] [
            outputSet "output-a" switchableSource outputHeader "Output A" [ "pv-output-a-batch" ]
            outputSet "output-b" switchableSource outputHeader "Output B" [ "pv-output-b-batch" ]
        ] [
            connection "connection-a" switchableSource None "input-a" "output-a"
            connection "connection-b" switchableSource None "input-b" "output-b"
        ]

let typedSampleModel () : ProvenanceModel =
    let baseModel = sampleModel ()
    let instrument = propertyHeader FixtureKinds.parameterProperty "Instrument"

    let degreeCelsius = {
        Name = "degree Celsius"
        TermSource = Some "UO"
        TermAccession = Some "UO:0000027"
    }

    let instrumentValue = {
        Name = "mass spectrometer"
        TermSource = Some "OBI"
        TermAccession = Some "OBI:0000049"
    }

    let temperature = {
        baseModel.PropertyValues.["pv-input-a-temperature"] with
            Value = ProvenanceValue.Float 12.
            Unit = Some degreeCelsius
    }

    let outputInstrument =
        propertyValue
            "pv-output-a-instrument"
            instrument
            (ProvenanceValue.Term instrumentValue)
            None
            (real baseModel.Source (Some "assay-process") instrument [] [ "Output A" ])

    let outputA = {
        baseModel.OutputSets.["output-a"] with
            PropertyValueIds = baseModel.OutputSets.["output-a"].PropertyValueIds @ [ outputInstrument.Id ]
    }

    {
        baseModel with
            PropertyValues =
                baseModel.PropertyValues
                |> Map.add temperature.Id temperature
                |> Map.add outputInstrument.Id outputInstrument
            OutputSets = baseModel.OutputSets |> Map.add outputA.Id outputA
    }

let dataOutputOnlyModel () : ProvenanceModel =
    let dataOutputOnlySource =
        source "fixture:data-output-only-table" "data-output-only-table"

    let outputHeader = ioHeader FixtureKinds.dataEndpoint "Output [Data]"
    let analysis = propertyHeader FixtureKinds.parameterProperty "Analysis"

    model
        dataOutputOnlySource
        [
            propertyValue
                "pv-data-output-analysis"
                analysis
                (ProvenanceValue.Text "LC-MS")
                None
                (real dataOutputOnlySource None analysis [] [ "Data Output A" ])
        ]
        [] [
            outputSet "data-output-only-a" dataOutputOnlySource outputHeader "Data Output A" [
                "pv-data-output-analysis"
            ]
        ] []
