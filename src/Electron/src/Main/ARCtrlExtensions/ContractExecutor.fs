module Main.ARCtrlExtensions.ContractExecutor

open ARCtrl
open ARCtrl.Contract
open Fable.Core
open FsSpreadsheet
open FsSpreadsheet.Js
open Main.Bindings.Filesystem
open Main.Bindings.Path

let private fulfillCreateContractAsync
    (arcPath: string)
    (contract: Contract)
    : JS.Promise<Result<Contract option, string>> =
    promise {
        try
            let path = ArcPathHelper.combine arcPath contract.Path

            let createAsync () = promise {
                match contract.DTO with
                | Some(DTO.Text content) ->
                    let! _ = mkdirAsync (dirname path) (MkdirOptions(recursive = true))
                    return! createTextFileExclusiveAsync path content
                | Some(DTO.Spreadsheet workbook) ->
                    let! bytes = FsWorkbook.toXlsxBytes workbook
                    let! _ = mkdirAsync (dirname path) (MkdirOptions(recursive = true))
                    return! createBinaryFileExclusiveAsync path bytes
                | None ->
                    let! _ = mkdirAsync (dirname path) (MkdirOptions(recursive = true))
                    return! createTextFileExclusiveAsync path ""
                | _ -> return failwithf "Contract %s is not an ISA contract" contract.Path
            }

            let! created = createAsync ()
            return Ok(if created then Some contract else None)
        with error ->
            return Error(sprintf "Error writing contract %s: %s" contract.Path error.Message)
    }

/// Executes contracts in order. CREATE fills missing files without replacing existing content.
/// Stops at the first failure so later writes cannot discard data needed for a retry.
/// The successful result contains fulfilled contracts, excluding CREATE paths that already existed.
let fulfillContractsSwateAsync (arcPath: string) (contracts: Contract[]) : JS.Promise<Result<Contract[], string[]>> = promise {
    let fulfilled = ResizeArray<Contract>()

    let rec execute index = promise {
        if index = contracts.Length then
            return Ok(fulfilled.ToArray())
        else
            let contract = contracts.[index]

            let! result =
                if contract.Operation = Operation.CREATE then
                    fulfillCreateContractAsync arcPath contract
                else
                    promise {
                        let! result = fullFillContract false arcPath contract
                        return Result.map Some result
                    }

            match result with
            | Error error -> return Error [| error |]
            | Ok completed ->
                completed |> Option.iter fulfilled.Add
                return! execute (index + 1)
    }

    return! execute 0
}

/// Validates the entire read batch before I/O, then reports every unreadable file.
let fulfillReadContractsSwateAsync
    (arcPath: string)
    (contracts: Contract[])
    : JS.Promise<Result<Contract[], string[]>> =
    promise {
        match
            contracts
            |> Array.tryFind (fun contract -> contract.Operation <> Operation.READ)
        with
        | Some contract -> return Error [| $"Contract {contract.Path} is not a READ contract." |]
        | None ->
            let fulfilled = ResizeArray<Contract>()
            let errors = ResizeArray<string>()

            for contract in contracts do
                match! fullFillContract false arcPath contract with
                | Ok completed -> fulfilled.Add completed
                | Error error -> errors.Add error

            return
                if errors.Count = 0 then
                    Ok(fulfilled.ToArray())
                else
                    Error(errors.ToArray())
    }
