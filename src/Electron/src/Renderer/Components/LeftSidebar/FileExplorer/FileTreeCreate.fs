namespace Renderer.Components.LeftSidebar.FileExplorer

open Fable.Core
open ARCtrl
open Swate.Components.Shared
open Swate.Components.Page.FileExplorer.Types
open Swate.Electron.Shared.FileIOTypes
open Renderer.Components.Helper
open Renderer.Components.LeftSidebar.FileExplorer.Types

module FileTreeCreateWorkflow =
    type CreateConfig = {
        setIsCreating: bool -> unit
        applyError: string -> unit
        revealInTree: string list -> unit
        /// Opens the created item like a click on it in the tree.
        openCreated: FileItem -> unit
        closeCreateModal: unit -> unit
    }

    let private toItem (isDirectory: bool) (path: string) : FileItem =
        let name = PathHelpers.getFileName path

        let item =
            if isDirectory then
                FileTree.createFolder name (Some path) FileItemIcon.Folder
            else
                FileTree.createFile name (Some path) FileItemIcon.Document

        { item with Id = path }

    /// Creates the item and asks the tree to reveal the path the create call returns, which is the zone folder for a
    /// study or assay. Success closes the dialog and an error keeps it open.
    /// The created item then opens like a click on it, so a study or assay shows its metadata and a new file opens.
    let createAndReveal
        (config: CreateConfig)
        (create: unit -> JS.Promise<Result<string, exn>>)
        (createdItem: string -> FileItem)
        =
        FileTreeDialogWorkflow.run
            config.setIsCreating
            config.applyError
            (fun () -> promise {
                match! create () with
                | Ok revealPath ->
                    config.revealInTree [ revealPath ]
                    config.openCreated (createdItem revealPath)
                    config.closeCreateModal ()
                    return Ok()
                | Error exn -> return Error exn.Message
            })

    let createArcEntry
        (config: CreateConfig)
        (addArcFile: FileContentDTO -> JS.Promise<Result<unit, exn>>)
        (draft: ArcCreateDraft)
        =
        let entityFolder =
            PathHelpers.tryGetParentPath draft.Path |> Option.defaultValue draft.Path

        let zoneFolder =
            PathHelpers.tryGetParentPath entityFolder |> Option.defaultValue entityFolder

        // The reveal opens only the zone folder, so the new entity shows up collapsed and selected.
        createAndReveal
            config
            (fun () -> promise {
                let! result = ArcFileApiHelper.withArcFileRequest draft.ArcFile addArcFile
                return result |> Result.map (fun () -> zoneFolder)
            })
            (fun _ -> toItem true entityFolder)

    let createFileSystemItem
        (config: CreateConfig)
        (createItem: CreateFileSystemItemRequest -> JS.Promise<Result<string, exn>>)
        (request: CreateFileSystemItemRequest)
        =
        createAndReveal config (fun () -> createItem request) (toItem (request.kind = FileSystemItemKind.Folder))

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
            (toItem false)
