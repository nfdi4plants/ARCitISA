namespace Renderer.Components.LeftSidebar.FileExplorer.Modals

open Fable.Core
open Feliz
open Swate.Components.Primitive.Dialog
open Renderer.Components.LeftSidebar.FileExplorer.Types
open Renderer.Components.LeftSidebar.FileExplorer.FileTreeRenameHelper

[<Erase; Mangle(false)>]
type FileTreeRenameModal =

    [<ReactComponent>]
    static member Main
        (isOpen: bool, draft: ArcRenameDraft option, close: unit -> unit, submit: string -> unit, ?isRenaming: bool)
        =

        let isRenaming = defaultArg isRenaming false
        let displayName = draft |> Option.map _.Item.Name |> Option.defaultValue "this item"

        Dialog.StringSubmissionDialog(
            isOpen = isOpen,
            title = "Rename Item",
            description = $"Rename '{displayName}' in the current ARC.",
            fieldLabel = "New name",
            initialValue = (draft |> Option.map _.InitialName |> Option.defaultValue ""),
            close = close,
            submit = submit,
            validate =
                (fun name ->
                    match draft with
                    | Some draft -> validateRenameDraftName draft name
                    | None -> Error "Select an item to rename."
                ),
            submitLabel = "Rename",
            validationMessage = "Enter a valid name.",
            isBusy = isRenaming,
            busyLabel = "Renaming...",
            debug = "arc-rename"
        )
