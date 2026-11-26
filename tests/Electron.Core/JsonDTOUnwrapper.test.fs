module ElectronCore.JsonDTOUnwrapperTests

open Arcitisa.Components.Shared
open Arcitisa.Electron.Shared.ARCtrlJsonObject
open Vitest

let private expectSome value errorMessage =
    match value with
    | Some value -> value
    | None -> failwith errorMessage

Vitest.describe (
    "JsonDTOUnwrapper",
    fun () ->
        Vitest.test (
            "returns Some/Ok for ARCtrlJsonObject DTO",
            fun () ->
                let dto =
                    ARCtrlJsonObject.init ("{\"id\":\"arc-1\"}", ARCObjects.Arc, JsonExportFormat.ARCtrl)

                let wrapped = JsonDTOs.ARCtrlJsonObject dto

                let unwrappedOption =
                    JsonDTOUnwrapper.tryUnwrap wrapped |> expectSome <| "Expected DTO to unwrap."

                let unwrappedResult = JsonDTOUnwrapper.unwrap wrapped

                Vitest.expect(unwrappedOption.Json).toBe (dto.Json)
                Vitest.expect(unwrappedOption.JsonObject).toEqual (dto.JsonObject)

                match unwrappedResult with
                | Ok value ->
                    Vitest.expect(value.JsonType).toEqual (JsonExportFormat.ARCtrl)
                    Vitest.expect(value.JsonObject).toEqual (dto.JsonObject)
                | Error message -> failwith $"Expected Ok result but got Error: {message}"
        )

        Vitest.test (
            "returns None/Error and does not throw for ARCtrlContract",
            fun () ->
                let optionResult = JsonDTOUnwrapper.tryUnwrap JsonDTOs.ARCtrlContract

                let resultResult =
                    try
                        JsonDTOUnwrapper.unwrap JsonDTOs.ARCtrlContract
                    with exn ->
                        failwith $"Expected non-throwing unwrap, but got exception: {exn.Message}"

                Vitest.expect(optionResult.IsNone).toBe (true)

                match resultResult with
                | Ok _ -> failwith "Expected Error for ARCtrlContract unwrap."
                | Error message ->
                    Vitest.expect(message.Length > 0).toBe (true)
                    Vitest.expect(message.Contains("ARCtrlContract")).toBe (true)
        )
)
