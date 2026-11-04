module ARCtrl.JsonCompatibility

open ARCtrl
open ARCtrl.Json
open Thoth.Json.Core

/// ARCtrl 3.2 renamed the serialized entity property DataMap to Datamap.
/// Normalize object keys without modifying user text or replacing a current value.
let normalizeDatamaps (json: string) =
    if not (json.Contains "\"DataMap\"") then
        json
    else
        let rec normalize =
            function
            | Json.Object properties ->
                let isEntity = properties |> List.exists (fun (key, _) -> key = "Identifier")
                let hasCurrent = properties |> List.exists (fun (key, _) -> key = "Datamap")

                properties
                |> List.map (fun (key, value) ->
                    let key =
                        if isEntity && not hasCurrent && key = "DataMap" then
                            "Datamap"
                        else
                            key

                    key, normalize value
                )
                |> Json.Object
            | Json.Array values -> values |> List.map normalize |> Json.Array
            | value -> value

        ARCtrl.Json.Decode.fromJsonString Decode.value json
        |> normalize
        |> Encode.value
        |> ARCtrl.Json.Encode.toJsonString 0

let fromInvestigationJsonString json =
    ArcInvestigation.fromJsonString (normalizeDatamaps json)

let fromInvestigationCompressedJsonString json =
    ArcInvestigation.fromCompressedJsonString (normalizeDatamaps json)

let fromAssayJsonString json =
    ArcAssay.fromJsonString (normalizeDatamaps json)

let fromAssayCompressedJsonString json =
    ArcAssay.fromCompressedJsonString (normalizeDatamaps json)

let fromStudyJsonString json =
    ArcStudy.fromJsonString (normalizeDatamaps json)

let fromStudyCompressedJsonString json =
    ArcStudy.fromCompressedJsonString (normalizeDatamaps json)

let fromRunJsonString json =
    ArcRun.fromJsonString (normalizeDatamaps json)

let fromRunCompressedJsonString json =
    ArcRun.fromCompressedJsonString (normalizeDatamaps json)

let fromWorkflowJsonString json =
    ArcWorkflow.fromJsonString (normalizeDatamaps json)

let fromWorkflowCompressedJsonString json =
    ArcWorkflow.fromCompressedJsonString (normalizeDatamaps json)

let private legacyInvestigationRootDecoder =
    Decode.object (fun get -> get.Required.Field "@id" Decode.string, get.Required.Field "@type" Decode.string)

/// Accept both the earlier bare Investigation export and the current ARC graph.
let fromInvestigationROCrateJsonString (json: string) : ArcInvestigation =
    let isLegacyRoot =
        try
            let id, nodeType =
                ARCtrl.Json.Decode.fromJsonString legacyInvestigationRootDecoder json

            id = "./" && nodeType = "Investigation"
        with _ ->
            false

    if isLegacyRoot then
        ARCtrl.Json.Decode.fromJsonString ARCtrl.Json.Investigation.ROCrate.decoder json
    else
        ARC.fromROCrateJsonString json :> ArcInvestigation
