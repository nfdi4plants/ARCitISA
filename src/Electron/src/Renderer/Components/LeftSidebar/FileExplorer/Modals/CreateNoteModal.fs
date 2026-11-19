namespace Renderer.Components.LeftSidebar.FileExplorer.Modals

open Fable.Core
open Feliz
open Swate.Components.Primitive.Dialog
open Renderer.Components.Helper.NoteCreation

[<Erase; Mangle(false)>]
type CreateNoteModal =

    /// Asks for the title of a new note. The note file opens once it is created.
    [<ReactComponent>]
    static member Main
        (
            isOpen: bool,
            location: NoteLocation,
            date: System.DateTime,
            close: unit -> unit,
            submit: string -> unit,
            ?isCreating: bool
        ) =
        Dialog.StringSubmissionDialog(
            isOpen = isOpen,
            title = "Create Note",
            description = describe location date,
            fieldLabel = "Title",
            initialValue = "",
            close = close,
            submit = submit,
            validate = validateTitle,
            submitLabel = "Create Note",
            isBusy = defaultArg isCreating false,
            busyLabel = "Creating...",
            debug = "create-note"
        )
