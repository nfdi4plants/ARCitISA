namespace Renderer.Components.LeftSidebar.FileExplorer

open Fable.Core
open ARCtrl
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Renderer.Components.Helper
open Renderer.Components.LeftSidebar.FileExplorer.Types

module FileTreeCreateWorkflow =
    type CreateConfig = {
        setIsCreating: bool -> unit
        applyError: string -> unit
        revealInTree: string -> unit
        closeCreateModal: unit -> unit
    }

    let importRevealPath
        (result: Result<ImportExternalFilesResult option, exn>)
        (targetRelativePath: string)
        : string option =
        match result with
        | Ok(Some ImportExternalFilesResult.Completed) -> Some targetRelativePath
        | _ -> None

    /// Creates the item and asks the tree to reveal it. Success closes the dialog and an error keeps it open.
    /// Nothing selects or opens the item, so the open view keeps its unsaved work.
    let createAndReveal (config: CreateConfig) (create: unit -> JS.Promise<Result<string, exn>>) =
        FileTreeDialogWorkflow.run
            config.setIsCreating
            config.applyError
            (fun () -> promise {
                match! create () with
                | Ok createdPath ->
                    config.revealInTree createdPath
                    config.closeCreateModal ()
                    return Ok()
                | Error exn -> return Error exn.Message
            })

    let createArcEntry
        (config: CreateConfig)
        (addArcFile: FileContentDTO -> JS.Promise<Result<unit, exn>>)
        (draft: ArcCreateDraft)
        =
        createAndReveal
            config
            (fun () -> promise {
                let! result = ArcFileApiHelper.withArcFileRequest draft.ArcFile addArcFile
                return result |> Result.map (fun () -> draft.Path)
            })

    let createFileSystemItem
        (config: CreateConfig)
        (createItem: CreateFileSystemItemRequest -> JS.Promise<Result<string, exn>>)
        (request: CreateFileSystemItemRequest)
        =
        createAndReveal config (fun () -> createItem request)

    let createDataMap
        (config: CreateConfig)
        (addArcFile: FileContentDTO -> JS.Promise<Result<unit, exn>>)
        (parentInfo: DatamapParentInfo)
        =
        createAndReveal
            config
            (fun () -> promise {
                let! result =
                    ArcFileApiHelper.withArcFileRequest (ArcFiles.DataMap(Some parentInfo, DataMap.init ())) addArcFile

                return result |> Result.map (fun () -> DatamapParentInfo.toPath parentInfo)
            })
