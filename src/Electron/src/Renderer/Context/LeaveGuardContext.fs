/// The guard against losing unsaved text edits when the page, the selection or the file changes.
/// The open text document registers here, and code that would leave it asks first.
module Renderer.Context.LeaveGuardContext

open Fable.Core
open Feliz

type LeaveGuardRegistration = {
    /// ARC-relative path of the open document.
    documentPath: string
    isDirty: unit -> bool
    /// Resolves true only when the document was saved and is clean.
    save: unit -> JS.Promise<bool>
    /// Drops the unsaved edits. The caller leaves the document right after.
    discard: unit -> unit
    /// The file of the document is gone from disk while it has unsaved edits.
    onRemovedFromDisk: unit -> unit
}

type LeaveGuardController = {
    /// Registers the open document. The returned function unregisters it, unless a newer registration replaced it.
    register: LeaveGuardRegistration -> (unit -> unit)
    /// The editor reports every change of its dirty state here.
    setDirty: bool -> unit
    /// True when the registered document has unsaved edits.
    isDirty: unit -> bool
    /// Resolves true at once when nothing is dirty. Otherwise asks Save, Discard or Cancel. Save resolves true only
    /// when the save succeeded, Discard drops the edits and resolves true, Cancel resolves false. A request while
    /// the dialog is open resolves false.
    confirmLeave: unit -> JS.Promise<bool>
    /// True when the registered document has this path and unsaved edits.
    isDirtyDocument: string -> bool
    /// The path of the registered document, if any.
    documentPath: unit -> string option
    /// Tells the registered document that its file is gone, when the path matches.
    notifyRemoved: string -> unit
    /// Saves for "Save and Close". Ok when nothing is dirty or the save succeeded.
    saveForClose: unit -> JS.Promise<Result<unit, exn>>
}

module LeaveGuardContextHelper =

    /// The value without a provider: nothing is guarded.
    let passThrough: LeaveGuardController = {
        register = fun _ -> ignore
        setDirty = ignore
        isDirty = fun () -> false
        confirmLeave = fun () -> Promise.lift true
        isDirtyDocument = fun _ -> false
        documentPath = fun () -> None
        notifyRemoved = ignore
        saveForClose = fun () -> Promise.lift (Ok())
    }

let LeaveGuardCtx =
    React.createContext<LeaveGuardController> (LeaveGuardContextHelper.passThrough)

[<Hook>]
let useLeaveGuardCtx () = React.useContext LeaveGuardCtx
