module Arcitisa.Electron.Shared.ARCtrlJsonObject

open Arcitisa.Components.Shared


type ARCDatasets =
    | Assay
    | Study
    | Workflow
    | Run

type ARCMaterial =
    | Sources
    | Samples

type ARCData =
    | Files
    | FragmentSelector

type ProcessType =
    | Material of ARCMaterial
    | Data of ARCData

type CurrentProcess =
    | Input of ProcessType
    | Output of ProcessType

type ARCObjects =
    | Arc
    | Datasets of ARCDatasets
    | Protocols
    | FormalParameters
    | Processes of CurrentProcess

type ARCtrlJsonObject = {
    Json: string
    JsonObject: ARCObjects
    JsonType: JsonExportFormat
} with

    static member init(json, jsonObj, jsonType) = {
        Json = json
        JsonObject = jsonObj
        JsonType = jsonType
    }

type JsonDTOs =
    | ARCtrlJsonObject of ARCtrlJsonObject
    | ARCtrlContract


type JsonDTOUnwrapper =

    static member tryUnwrap(jsonDTO: JsonDTOs) =
        match jsonDTO with
        | JsonDTOs.ARCtrlJsonObject dto -> Some dto
        | JsonDTOs.ARCtrlContract -> None //ToDo

    static member unwrap(jsonDTO: JsonDTOs) =
        match JsonDTOUnwrapper.tryUnwrap jsonDTO with
        | Some dto -> Ok dto
        | None -> Error "ARCtrlContract is not supported by JsonDTOUnwrapper.unwrap yet."
