namespace Renderer.Components.LeftSidebar.FileExplorer.Modals

open Fable.Core
open Feliz
open Swate.Components.Shared
open Swate.Components.Primitive.Dialog
open Swate.Electron.Shared.RenamePathRules
open Renderer.Components.LeftSidebar.FileExplorer.Helper
open Renderer.Components.LeftSidebar.FileExplorer.Types

[<Erase; Mangle(false)>]
type CreateArcFileModal =

    [<ReactComponent>]
    static member Main
        (
            isOpen: bool,
            kind: ArcFilesDiscriminate,
            close: unit -> unit,
            submit: ArcFilesDiscriminate -> string -> unit,
            ?isCreating: bool
        ) =

        let config = arcCreateKinds |> List.find (fun config -> config.Kind = kind)
        let isCreating = defaultArg isCreating false

        Dialog.StringSubmissionDialog(
            isOpen = isOpen,
            title = $"Add {config.Label}",
            description = $"Create a new {config.Label.ToLowerInvariant()} in the current ARC.",
            fieldLabel = "Identifier",
            initialValue = $"New {config.Label}",
            close = close,
            submit = (fun identifier -> submit kind identifier),
            validate = validateArcIdentifier,
            submitLabel = $"Create {config.Label}",
            validationMessage = arcIdentifierError,
            isBusy = isCreating,
            busyLabel = "Creating...",
            debug = "arc-create"
        )
