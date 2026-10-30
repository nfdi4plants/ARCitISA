module ElectronCore.ContractExecutorTests

open ARCtrl
open ARCtrl.Contract
open ARCtrl.Spreadsheet
open Fable.Core
open Main.ARCtrlExtensions.ContractExecutor
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Vitest

let private withTempDirectory (testBody: string -> JS.Promise<unit>) = promise {
    let! arcPath = TestHelpers.createTempDirectoryAsync "swate-contract-executor-"

    try
        do! testBody arcPath
        do! TestHelpers.removeDirectoryAsync arcPath
    with error ->
        do! TestHelpers.removeDirectoryAsync arcPath
        return raise error
}

let private expectSucceeded (result: Result<Contract[], string[]>) =
    match result with
    | Ok contracts -> contracts
    | Error errors -> failwith (String.concat "\n" errors)

let private textCreate path content =
    Contract.createCreate (path, DTOType.PlainText, DTO.Text content)

let private assayCreate path identifier =
    let workbook = ArcAssay(identifier) |> ArcAssay.toFsWorkbook
    Contract.createCreate (path, DTOType.ISA_Assay, DTO.Spreadsheet workbook)

let private readAssayIdentifierAsync path = promise {
    let! workbook = FileSystemHelper.readFileXlsxAsync path
    return (ArcAssay.fromFsWorkbook workbook).Identifier
}

Vitest.describe (
    "fulfillContractsSwateAsync",
    fun () ->
        Vitest.test (
            "creates missing text and empty files and reports only the paths it created",
            fun () ->
                withTempDirectory (fun arcPath -> promise {
                    let existingPath = join [| arcPath; ".gitignore" |]
                    do! writeFileAsync existingPath "user ignore rules" TextEncoding.Utf8
                    let existingEmptyContractPath = join [| arcPath; "existing.keep" |]

                    do!
                        writeFileAsync
                            existingEmptyContractPath
                            "user content in an empty-file contract"
                            TextEncoding.Utf8

                    let contracts = [|
                        textCreate ".gitignore" "default ignore rules"
                        Contract.createCreate ("existing.keep", DTOType.PlainText)
                        textCreate "notes/README.md" "requested note payload"
                        Contract.createCreate ("assays/.gitkeep", DTOType.PlainText)
                    |]

                    let! result = fulfillContractsSwateAsync arcPath contracts
                    let completed = expectSucceeded result
                    Vitest.expect(completed |> Array.map _.Path).toEqual [| "notes/README.md"; "assays/.gitkeep" |]

                    let! existingContent = readFileAsync existingPath TextEncoding.Utf8
                    let! existingEmptyContent = readFileAsync existingEmptyContractPath TextEncoding.Utf8
                    let! createdContent = readFileAsync (join [| arcPath; "notes"; "README.md" |]) TextEncoding.Utf8
                    let! emptyContent = readFileAsync (join [| arcPath; "assays"; ".gitkeep" |]) TextEncoding.Utf8
                    Vitest.expect(existingContent).toBe "user ignore rules"
                    Vitest.expect(existingEmptyContent).toBe "user content in an empty-file contract"
                    Vitest.expect(createdContent).toBe "requested note payload"
                    Vitest.expect(emptyContent).toBe ""

                    let! repeatedResult = fulfillContractsSwateAsync arcPath contracts
                    Vitest.expect(expectSucceeded repeatedResult).toEqual [||]
                })
        )

        Vitest.test (
            "creates a missing workbook with the supplied entity payload",
            fun () ->
                withTempDirectory (fun arcPath -> promise {
                    let relativePath = "assays/NewAssay/isa.assay.xlsx"
                    let! result = fulfillContractsSwateAsync arcPath [| assayCreate relativePath "CustomAssay" |]
                    Vitest.expect(expectSucceeded result |> Array.map _.Path).toEqual [| relativePath |]

                    let! identifier = readAssayIdentifierAsync (join [| arcPath; relativePath |])
                    Vitest.expect(identifier).toBe "CustomAssay"
                })
        )

        Vitest.test (
            "preserves an existing workbook byte for byte and omits its CREATE contract",
            fun () ->
                withTempDirectory (fun arcPath -> promise {
                    let relativePath = "assays/ExistingAssay/isa.assay.xlsx"
                    let absolutePath = join [| arcPath; relativePath |]

                    let! initialResult =
                        fulfillContractsSwateAsync arcPath [| assayCreate relativePath "OriginalAssay" |]

                    expectSucceeded initialResult |> ignore
                    let! initialBytes = readFileBase64Async absolutePath

                    let! result = fulfillContractsSwateAsync arcPath [| assayCreate relativePath "ReplacementAssay" |]
                    Vitest.expect(expectSucceeded result).toEqual [||]
                    let! finalBytes = readFileBase64Async absolutePath
                    Vitest.expect(finalBytes).toBe initialBytes
                    let! identifier = readAssayIdentifierAsync absolutePath
                    Vitest.expect(identifier).toBe "OriginalAssay"
                })
        )

        Vitest.test (
            "UPDATE replaces existing text and workbooks and creates missing targets",
            fun () ->
                withTempDirectory (fun arcPath -> promise {
                    let existingWorkbookPath = "existing/isa.assay.xlsx"
                    let missingWorkbookPath = "missing/isa.assay.xlsx"
                    do! writeFileAsync (join [| arcPath; "existing.txt" |]) "before" TextEncoding.Utf8

                    let! seedResult =
                        fulfillContractsSwateAsync arcPath [| assayCreate existingWorkbookPath "BeforeUpdate" |]

                    expectSucceeded seedResult |> ignore

                    let updatedWorkbookContract path identifier =
                        let contract = assayCreate path identifier

                        {
                            contract with
                                Operation = Operation.UPDATE
                        }

                    let updates = [|
                        Contract.createUpdate ("existing.txt", DTOType.PlainText, DTO.Text "updated")
                        Contract.createUpdate ("nested/missing.txt", DTOType.PlainText, DTO.Text "upserted")
                        updatedWorkbookContract existingWorkbookPath "UpdatedAssay"
                        updatedWorkbookContract missingWorkbookPath "CreatedByUpdate"
                    |]

                    let! result = fulfillContractsSwateAsync arcPath updates
                    Vitest.expect(expectSucceeded result |> Array.map _.Path).toEqual (updates |> Array.map _.Path)
                    let! existingContent = readFileAsync (join [| arcPath; "existing.txt" |]) TextEncoding.Utf8
                    let! missingContent = readFileAsync (join [| arcPath; "nested"; "missing.txt" |]) TextEncoding.Utf8
                    let! existingIdentifier = readAssayIdentifierAsync (join [| arcPath; existingWorkbookPath |])
                    let! missingIdentifier = readAssayIdentifierAsync (join [| arcPath; missingWorkbookPath |])
                    Vitest.expect(existingContent).toBe "updated"
                    Vitest.expect(missingContent).toBe "upserted"
                    Vitest.expect(existingIdentifier).toBe "UpdatedAssay"
                    Vitest.expect(missingIdentifier).toBe "CreatedByUpdate"
                })
        )

        Vitest.test (
            "concurrent text CREATE calls have one winner without replacing its content",
            fun () ->
                withTempDirectory (fun arcPath -> promise {
                    let contracts = [|
                        textCreate "race/note.md" "first payload"
                        textCreate "race/note.md" "second payload"
                    |]

                    let! results =
                        contracts
                        |> Array.map (fun contract -> fulfillContractsSwateAsync arcPath [| contract |])
                        |> Promise.all

                    let completed = results |> Array.collect expectSucceeded
                    Vitest.expect(completed.Length).toBe 1
                    let! finalContent = readFileAsync (join [| arcPath; "race"; "note.md" |]) TextEncoding.Utf8
                    Vitest.expect(finalContent).toBe (completed.[0].DTO.Value.AsText())
                })
        )

        Vitest.test (
            "concurrent workbook CREATE calls leave the winning workbook intact",
            fun () ->
                withTempDirectory (fun arcPath -> promise {
                    let relativePath = "race/isa.assay.xlsx"
                    let first = assayCreate relativePath "FirstAssay"
                    let second = assayCreate relativePath "SecondAssay"

                    let! results =
                        [|
                            fulfillContractsSwateAsync arcPath [| first |]
                            fulfillContractsSwateAsync arcPath [| second |]
                        |]
                        |> Promise.all

                    let completed = results |> Array.map expectSucceeded
                    Vitest.expect(completed |> Array.sumBy Array.length).toBe 1

                    let expectedIdentifier =
                        if completed.[0].Length = 1 then
                            "FirstAssay"
                        else
                            "SecondAssay"

                    let! identifier = readAssayIdentifierAsync (join [| arcPath; relativePath |])
                    Vitest.expect(identifier).toBe expectedIdentifier
                })
        )

        Vitest.test (
            "runs CREATE, UPDATE, READ, RENAME and DELETE sequentially",
            fun () ->
                withTempDirectory (fun arcPath -> promise {
                    let contracts = [|
                        textCreate "before.txt" "created"
                        Contract.createUpdate ("before.txt", DTOType.PlainText, DTO.Text "updated")
                        Contract.createRead ("before.txt", DTOType.PlainText)
                        Contract.createRename ("before.txt", "after.txt")
                        Contract.createRead ("after.txt", DTOType.PlainText)
                        Contract.createDelete "after.txt"
                    |]

                    let! result = fulfillContractsSwateAsync arcPath contracts
                    let completed = expectSucceeded result
                    Vitest.expect(completed.Length).toBe contracts.Length
                    Vitest.expect(completed.[2].DTO.Value.AsText()).toBe "updated"
                    Vitest.expect(completed.[4].DTO.Value.AsText()).toBe "updated"
                    let! beforeExists = TestHelpers.pathExistsAsync (join [| arcPath; "before.txt" |])
                    let! afterExists = TestHelpers.pathExistsAsync (join [| arcPath; "after.txt" |])
                    Vitest.expect(beforeExists).toBe false
                    Vitest.expect(afterExists).toBe false
                })
        )

        Vitest.test (
            "write batches stop at the first failure",
            fun () ->
                withTempDirectory (fun arcPath -> promise {
                    do! writeFileAsync (join [| arcPath; "blocker" |]) "file blocks parent folder" TextEncoding.Utf8
                    // Node rejects a NUL in a filename before opening it, independently of permissions or OS.
                    let invalidPath = "invalid" + string (char 0) + ".txt"

                    let contracts = [|
                        textCreate invalidPath "invalid filename"
                        textCreate "ok.txt" "must not execute"
                        textCreate "blocker/x.txt" "cannot create"
                    |]

                    match! fulfillContractsSwateAsync arcPath contracts with
                    | Ok _ -> failwith "Expected invalid create paths to return errors."
                    | Error errors ->
                        Vitest.expect(errors.Length).toBe 1
                        Vitest.expect(errors.[0]).toContain invalidPath

                    let! exists = TestHelpers.pathExistsAsync (join [| arcPath; "ok.txt" |])
                    Vitest.expect(exists).toBe false
                })
        )

        Vitest.test (
            "read batches report every unreadable file",
            fun () ->
                withTempDirectory (fun arcPath -> promise {
                    do! writeFileAsync (join [| arcPath; "readable.txt" |]) "content" TextEncoding.Utf8

                    let contracts = [|
                        Contract.createRead ("missing-first.txt", DTOType.PlainText)
                        Contract.createRead ("readable.txt", DTOType.PlainText)
                        Contract.createRead ("missing-last.txt", DTOType.PlainText)
                    |]

                    match! fulfillReadContractsSwateAsync arcPath contracts with
                    | Ok _ -> failwith "Expected both missing files to be reported."
                    | Error errors ->
                        Vitest.expect(errors.Length).toBe 2
                        Vitest.expect(errors.[0]).toContain "missing-first.txt"
                        Vitest.expect(errors.[1]).toContain "missing-last.txt"
                })
        )

        Vitest.test (
            "read executor rejects non-READ contracts without I/O",
            fun () ->
                withTempDirectory (fun arcPath -> promise {
                    let contracts = [| textCreate "must-not-exist.txt" "content" |]

                    match! fulfillReadContractsSwateAsync arcPath contracts with
                    | Ok _ -> failwith "Expected a CREATE contract to be rejected."
                    | Error errors ->
                        Vitest.expect(errors).toEqual [| "Contract must-not-exist.txt is not a READ contract." |]

                    let! exists = TestHelpers.pathExistsAsync (join [| arcPath; "must-not-exist.txt" |])
                    Vitest.expect(exists).toBe false
                })
        )
)
