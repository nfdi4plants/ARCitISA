namespace Renderer.Components.LeftSidebar.FileExplorer

open Fable.Core
open Swate.Components.Primitive.ErrorModal.Types
open Swate.Components.Page.FileExplorer.Types
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.RenamePathRules
open Renderer.Components.LeftSidebar.FileExplorer.Types
open Renderer.Components.LeftSidebar.FileExplorer.FileTreeRenameHelper
open Renderer.Components.LeftSidebar.FileExplorer.FileTreeDialogWorkflow
open Renderer

module FileTreeRenameWorkflow =

    type ConfirmRenameConfig = {
        pendingRenameDraft: ArcRenameDraft option
        selectedTreePath: string option
        pageState: Renderer.Types.PageState option
        closeRenameModal: unit -> unit
        setIsRenaming: bool -> unit
        setSelection: ArcSelection -> unit
        refreshGitStatus: unit -> unit
        reloadPreviewByPath: string -> JS.Promise<Result<unit, string>>
        /// Resolves false when the user keeps the open text document, which cancels the rename.
        confirmLeaveFor: string -> JS.Promise<bool>
        renamePath: RenamePathRequest -> JS.Promise<Result<unit, exn>>
        enqueueError: ErrorModalRequest -> unit
    }

    /// The new path of the open page when the rename moves its file. An open text page reloads there, so a
    /// later save does not recreate the old file.
    let private tryRemapActivePagePath
        (sourcePath: string)
        (targetPath: string)
        (pageState: Renderer.Types.PageState option)
        =
        match pageState with
        | Some(Renderer.Types.PageState.ArcFilePage(arcFile, _, _)) ->
            arcFile.TryGetRelativePath()
            |> Option.bind (PathHelpers.tryRemapPathPrefix sourcePath targetPath)
        | Some(Renderer.Types.PageState.TextDocumentPage data) ->
            PathHelpers.tryRemapPathPrefix sourcePath targetPath data.Path
        | _ -> None

    let requestRenameItem
        (setPendingRenameDraft: ArcRenameDraft option -> unit)
        (enqueueError: ErrorModalRequest -> unit)
        (item: FileItem)
        =
        match tryBuildRenameDraft item with
        | Ok renameDraft -> setPendingRenameDraft (Some renameDraft)
        | Error validationError ->
            enqueueError (ErrorModalRequest.create (validationError, title = "Could not rename item"))

    let confirmRenameItem (config: ConfirmRenameConfig) (newName: string) =
        let applyError message =
            config.enqueueError (ErrorModalRequest.create (message, title = "Could not rename item"))

        match config.pendingRenameDraft with
        | None -> config.closeRenameModal ()
        | Some renameDraft ->
            match validateRenameDraftName renameDraft newName with
            | Error validationError -> applyError validationError
            | Ok normalizedNewName ->
                let targetPath = buildRenamedSiblingPath renameDraft.SourcePath normalizedNewName

                if PathHelpers.pathsEqual targetPath renameDraft.SourcePath then
                    config.closeRenameModal ()
                else
                    let renameAndReload () = promise {
                        let! renameResult =
                            config.renamePath {
                                relativePath = renameDraft.SourcePath
                                newName = normalizedNewName
                            }

                        match renameResult with
                        | Error renameError -> return Error renameError.Message
                        | Ok() ->
                            config.selectedTreePath
                            |> Option.bind (PathHelpers.tryRemapPathPrefix renameDraft.SourcePath targetPath)
                            |> Option.iter (fun remappedSelectionPath ->
                                config.setSelection (ArcSelection.forTreePath (Some remappedSelectionPath))
                            )

                            match tryRemapActivePagePath renameDraft.SourcePath targetPath config.pageState with
                            | Some remappedPagePath ->
                                let! reloadResult = config.reloadPreviewByPath remappedPagePath

                                match reloadResult with
                                | Ok() -> ()
                                | Error reloadError ->
                                    applyError $"Renamed item, but could not refresh the open file: {reloadError}"
                            | None -> ()

                            config.refreshGitStatus ()
                            config.closeRenameModal ()
                            return Ok()
                    }

                    run
                        config.setIsRenaming
                        applyError
                        (fun () -> promise {
                            // Cancel in the leave dialog cancels the rename before anything changes on disk.
                            let! canLeave = config.confirmLeaveFor renameDraft.SourcePath

                            if canLeave then
                                return! renameAndReload ()
                            else
                                config.closeRenameModal ()
                                return Ok()
                        })
