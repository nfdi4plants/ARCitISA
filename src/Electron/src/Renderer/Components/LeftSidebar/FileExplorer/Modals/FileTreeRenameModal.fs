namespace Renderer.Components.LeftSidebar.FileExplorer.Modals

open Fable.Core
open Feliz
open Swate.Components.Primitive.Dialog
open Swate.Electron.Shared.RenamePathRules

[<Erase; Mangle(false)>]
type FileTreeRenameModal =

    [<ReactComponent>]
    static member Main
        (
            isOpen: bool,
            itemName: string option,
            initialName: string option,
            close: unit -> unit,
            submit: string -> unit,
            ?isRenaming: bool
        ) =

        let isRenaming = defaultArg isRenaming false
        let displayName = itemName |> Option.defaultValue "this item"

        Dialog.StringSubmissionDialog(
            isOpen = isOpen,
            title = "Rename Item",
            description = $"Rename '{displayName}' in the current ARC.",
            fieldLabel = "New name",
            initialValue = (initialName |> Option.defaultValue ""),
            close = close,
            submit = submit,
            validate = validateRenameName,
            submitLabel = "Rename",
            validationMessage = "Enter a valid name.",
            isBusy = isRenaming,
            busyLabel = "Renaming...",
            debug = "arc-rename"
        )
