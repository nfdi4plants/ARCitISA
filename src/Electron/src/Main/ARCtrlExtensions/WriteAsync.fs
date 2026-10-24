namespace Main.ARCtrlExtensions

open ARCtrl
open ARCtrl.Contract
open ARCtrl.Helper
open ARCtrl.Spreadsheet
open Main.Notes.NoteConstants

[<AutoOpen>]
module ArcWriteExtensions =

    /// The distinct ARC-relative paths a contract batch touches. A rename contract also touches its target.
    let contractWrittenPaths (contracts: Contract[]) : string[] =
        contracts
        |> Array.collect (fun contract ->
            match contract.Operation, contract.DTO with
            | Operation.RENAME, Some(DTO.Text targetPath) -> [| contract.Path; targetPath |]
            | _ -> [| contract.Path |]
        )
        |> Array.distinct

    let private notesReadmeContract =
        let path = ArcPathHelper.combine NotesRootFolderName NotesReadmeFileName
        Contract.createCreate (path, DTOType.PlainText, DTO.Text NotesReadmeContent)

    let private swateScaffoldContracts = [|
        ARCtrl.Contract.Git.gitignoreContract
        notesReadmeContract
    |]

    let private collectionGitKeepContracts =
        [|
            ArcPathHelper.AssaysFolderName
            ArcPathHelper.StudiesFolderName
            ArcPathHelper.WorkflowsFolderName
            ArcPathHelper.RunsFolderName
        |]
        |> Array.map (fun collectionFolder ->
            let path = ArcPathHelper.combine collectionFolder ArcPathHelper.GitKeepFileName

            Contract.createCreate (path, DTOType.PlainText)
        )

    let private isUnmanagedPlainTextCreateContract (contract: Contract) =
        contract.Operation = Operation.CREATE
        && contract.DTOType = Some DTOType.PlainText
        && contract.DTO.IsNone

    type ARC with

        /// Hotfix for #618, not fixed in the consumed ARCtrl 3.0.0-beta.12.
        /// Mirrors ARCtrl.GetWriteContracts except that it emits only managed ARC payload contracts instead of
        /// iterating the complete filesystem model, where stale or Git metadata paths could be recreated.
        member this.GetWriteContractsSwate(?skipUpdateFS: bool) =
            if not (defaultArg skipUpdateFS false) then
                this.UpdateFileSystem()
            //let datamapFile = defaultArg datamapFile false
            /// Map containing the fileName and the types for DTOTypes and objects.
            let filemap = System.Collections.Generic.Dictionary<string, DTOType * DTO>()

            let investigationConverter = ArcInvestigation.toFsWorkbook

            filemap.Add(
                ARCtrl.ArcPathHelper.InvestigationFileName,
                (DTOType.ISA_Investigation, investigationConverter this |> box |> DTO.Spreadsheet)
            )

            this.StaticHash <- this.GetLightHashCode()

            this.Studies
            |> Seq.iter (fun s ->
                s.StaticHash <- s.GetLightHashCode()

                filemap.Add(
                    Identifier.Study.fileNameFromIdentifier s.Identifier,
                    (DTOType.ISA_Study, ArcStudy.toFsWorkbook s |> box |> DTO.Spreadsheet)
                )

                if s.DataMap.IsSome (*&& datamapFile*) then
                    let dm = s.DataMap.Value
                    dm.StaticHash <- dm.GetHashCode()

                    filemap.Add(
                        Identifier.Study.datamapFileNameFromIdentifier s.Identifier,
                        (DTOType.ISA_Datamap, Spreadsheet.DataMap.toFsWorkbook dm |> box |> DTO.Spreadsheet)
                    )

            )

            this.Assays
            |> Seq.iter (fun a ->
                a.StaticHash <- a.GetLightHashCode()

                filemap.Add(
                    Identifier.Assay.fileNameFromIdentifier a.Identifier,
                    (DTOType.ISA_Assay, ArcAssay.toFsWorkbook a |> box |> DTO.Spreadsheet)
                )

                if a.DataMap.IsSome (*&& datamapFile*) then
                    let dm = a.DataMap.Value
                    dm.StaticHash <- dm.GetHashCode()

                    filemap.Add(
                        Identifier.Assay.datamapFileNameFromIdentifier a.Identifier,
                        (DTOType.ISA_Datamap, Spreadsheet.DataMap.toFsWorkbook dm |> box |> DTO.Spreadsheet)
                    )
            )

            this.Workflows
            |> Seq.iter (fun w ->
                w.StaticHash <- w.GetLightHashCode()

                filemap.Add(
                    Identifier.Workflow.fileNameFromIdentifier w.Identifier,
                    (DTOType.ISA_Workflow, ArcWorkflow.toFsWorkbook w |> box |> DTO.Spreadsheet)
                )
                //if w.CWLDescription.IsSome then
                //    failwith "Not implemented yet: CWL description in ARC.GetWriteContracts"
                if w.DataMap.IsSome (*&& datamapFile*) then
                    let dm = w.DataMap.Value
                    dm.StaticHash <- dm.GetHashCode()

                    filemap.Add(
                        Identifier.Workflow.datamapFileNameFromIdentifier w.Identifier,
                        (DTOType.ISA_Datamap, Spreadsheet.DataMap.toFsWorkbook dm |> box |> DTO.Spreadsheet)
                    )
            )

            this.Runs
            |> Seq.iter (fun r ->
                r.StaticHash <- r.GetLightHashCode()

                filemap.Add(
                    Identifier.Run.fileNameFromIdentifier r.Identifier,
                    (DTOType.ISA_Run, ArcRun.toFsWorkbook r |> box |> DTO.Spreadsheet)
                )
                //if r.CWLDescription.IsSome then
                //    failwith "Not implemented yet: CWL description in ARC.GetWriteContracts"
                //if r.CWLInput.Count > 0 then
                //    failwith "Not implemented yet: CWL YAML input in ARC.GetWriteContracts"
                if r.DataMap.IsSome (*&& datamapFile*) then
                    let dm = r.DataMap.Value
                    dm.StaticHash <- dm.GetHashCode()

                    filemap.Add(
                        Identifier.Run.datamapFileNameFromIdentifier r.Identifier,
                        (DTOType.ISA_Datamap, Spreadsheet.DataMap.toFsWorkbook dm |> box |> DTO.Spreadsheet)
                    )
            )

            match this.License with
            | Some l ->
                match l.Type with
                | LicenseContentType.Fulltext ->
                    l.StaticHash <- l.GetHashCode()
                    filemap.Add(l.Path, (DTOType.PlainText, DTO.Text l.Content))
            | None -> ()

            [|
                yield! swateScaffoldContracts
                yield! collectionGitKeepContracts

                for entry in filemap do
                    let dtoType, dto = entry.Value
                    yield Contract.createCreate (entry.Key, dtoType, dto)
            |]

        /// Hotfix for #618. Writes only contracts selected by GetWriteContractsSwate.
        member this.TryWriteAsyncSwate(arcPath: string) =
            this.GetWriteContractsSwate() |> fullFillContractBatchAsync arcPath

        /// Writes only managed update contracts. This prevents ARC save from overwriting unmanaged text files
        /// such as notes with empty content when ARCtrl falls back to full write contracts.
        member this.GetUpdateContractsSwate(?skipUpdateFS: bool) =
            this.GetUpdateContracts(?skipUpdateFS = skipUpdateFS)
            |> Array.filter (isUnmanagedPlainTextCreateContract >> not)
