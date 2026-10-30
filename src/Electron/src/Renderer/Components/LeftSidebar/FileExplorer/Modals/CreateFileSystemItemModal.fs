namespace Renderer.Components.LeftSidebar.FileExplorer.Modals

open Fable.Core
open Feliz
open Swate.Components.Primitive.Dialog
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.RenamePathRules
open Renderer.Components.LeftSidebar.FileExplorer.Helper

[<Erase; Mangle(false)>]
type CreateFileSystemItemModal =

    [<ReactComponent>]
    static member Main
        (
            isOpen: bool,
            kind: FileSystemItemKind,
            parentName: string option,
            close: unit -> unit,
            submit: string -> unit,
            ?isCreating: bool
        ) =

        let isCreating = defaultArg isCreating false

        let label = fileSystemCreateKindLabel kind
        let actionLabel = $"Create {label}"
        let parentDisplayName = parentName |> Option.defaultValue "this folder"

        Dialog.StringSubmissionDialog(
            isOpen = isOpen,
            title = actionLabel,
            description = $"Create a new {label.ToLowerInvariant()} in '{parentDisplayName}'.",
            fieldLabel = "Name",
            initialValue = "",
            close = close,
            submit = submit,
            validate = validateRenameName,
            submitLabel = actionLabel,
            validationMessage = "Enter a valid name.",
            isBusy = isCreating,
            busyLabel = "Creating...",
            debug = "file-system-create"
        )
