module internal Swate.Components.Tests.JsonParse

open ARCtrl
open ARCtrl.Json
open Swate.Components.Shared
open Vitest

let private errorMessage (result: Result<ArcFiles, exn>) =
    match result with
    | Error e -> e.Message
    | Ok _ -> failwith "Expected an error."

Vitest.describe (
    "JSON parse errors",
    fun () ->
        Vitest.test (
            "a format that is not supported for the file type names both",
            fun () ->
                let result =
                    Import.tryParseFromJsonString ("{}", Some JsonExportFormat.ISA, Some ArcFilesDiscriminate.Run, None)

                Vitest.expect(errorMessage result).toBe "ISA JSON is not supported for a run."
        )

        Vitest.test (
            "a file name without format and type is explained",
            fun () ->
                let result = Import.tryParseFromJsonString ("{}", None, None, Some "data.json")

                Vitest.expect(errorMessage result).toBe
                    "Could not detect the JSON format and file type from the file name \"data.json\". The name must contain both, separated by \"_\", for example \"ARCtrlCompressed_Assay.json\"."
        )

        Vitest.test (
            "missing format and file name is explained",
            fun () ->
                let result = Import.tryParseFromJsonString ("{}", None, None, None)

                Vitest.expect(errorMessage result).toBe
                    "Could not detect the JSON format and file type, because no file name was given."
        )

        Vitest.test (
            "invalid JSON reports the reason of the ARCtrl parser",
            fun () ->
                let reason =
                    try
                        ArcAssay.fromROCrateJsonString "not json" |> ignore
                        ""
                    with e ->
                        e.Message

                let result =
                    Import.tryParseFromJsonString (
                        "not json",
                        Some JsonExportFormat.ROCrate,
                        Some ArcFilesDiscriminate.Assay,
                        None
                    )

                let message = errorMessage result

                Vitest.expect(reason.Length).toBeGreaterThan 0
                Vitest.expect(message.StartsWith "Could not read the file as").toBe true
                Vitest.expect(message.Contains reason).toBe true
        )

        Vitest.test (
            "a valid ARCtrl assay string parses",
            fun () ->
                let _, json =
                    Export.parseToJsonString (ArcFiles.Assay(ArcAssay.init "MyAssay"), JsonExportFormat.ARCtrl)

                match
                    Import.tryParseFromJsonString (
                        json,
                        Some JsonExportFormat.ARCtrl,
                        Some ArcFilesDiscriminate.Assay,
                        None
                    )
                with
                | Ok(ArcFiles.Assay assay) -> Vitest.expect(assay.Identifier).toBe "MyAssay"
                | Ok _ -> failwith "Expected an assay."
                | Error e -> failwith e.Message
        )
)
