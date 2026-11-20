namespace Renderer.Components.LeftSidebar.FileExplorer

open Swate.Components.Primitive.ErrorModal.Types
open Swate.Components.Page.FileExplorer.Types
open Fable.Core
open Swate.Components.Shared
open Helper
open FileTreeDialogWorkflow

module FileTreeDeleteWorkflow =

    type ConfirmDeleteConfig = {
        pendingDeleteItem: FileItem option
        closeDeleteModal: unit -> unit
        setIsDeleting: bool -> unit
        enqueueError: ErrorModalRequest -> unit
        /// Returns the paths of the running Download and Free actions when the user confirms.
        /// A function keeps the check current, because an action can start while the modal is open.
        getLfsActivePaths: unit -> Renderer.Context.LfsActivityContext.LfsActivePathIndex
        deletePath: string -> JS.Promise<Result<unit, exn>>
    }

    let requestDeleteItem (setPendingDeleteItem: FileItem option -> unit) (item: FileItem) =
        if canDeleteFileItem item then
            setPendingDeleteItem (Some item)

    let confirmDeleteItem (config: ConfirmDeleteConfig) =
        let deleteTarget =
            config.pendingDeleteItem
            |> Option.filter canDeleteFileItem
            |> Option.bind (fun item ->
                item.Path
                |> Option.map (fun path -> item, PathHelpers.normalizeCanonicalRelativePath path)
            )

        match deleteTarget with
        | None -> config.closeDeleteModal ()
        | Some(item, deletePath) ->
            let applyError message =
                config.enqueueError (ErrorModalRequest.create (message, title = "Could not delete item"))

            let isLocked =
                FileTreeContextMenu.isLockedByLfsActivity (config.getLfsActivePaths ()) item

            if isLocked then
                config.closeDeleteModal ()

                applyError
                    $"ARCitISA cannot delete '{deletePath}' while a large file download or free runs on a file the delete would remove. Wait until it finishes, then try again."
            else
                run
                    config.setIsDeleting
                    applyError
                    (fun () -> promise {
                        match! config.deletePath deletePath with
                        | Ok() ->
                            config.closeDeleteModal ()
                            return Ok()
                        | Error exn -> return Error exn.Message
                    })
