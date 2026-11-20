module Renderer.Components.Helper.ArcVaultHelper

open Browser.Dom
open Fable.Core
open Swate.Components.PageComponents.SettingsPage
open Swate.Components.Primitive.ErrorModal.Types
open Swate.Electron.Shared.IPCTypes

/// What the one-time move of the old localStorage value does with its key.
[<RequireQualifiedAccess>]
type LegacyAutoCreateNotesFolderAction =
    /// No old value is stored.
    | Leave
    /// The old value is unusable or equals the effective one.
    | RemoveKey
    /// The old value differs. Store it as the effective default and remove the key after that succeeds.
    | AdoptThenRemoveKey of bool

/// The old settings page wrote the value with useLocalStorage, which stores a JSON boolean.
let legacyAutoCreateNotesFolderAction (stored: string option) (effective: bool) : LegacyAutoCreateNotesFolderAction =
    match stored |> Option.map _.Trim() with
    | None -> LegacyAutoCreateNotesFolderAction.Leave
    | Some "true" when effective -> LegacyAutoCreateNotesFolderAction.RemoveKey
    | Some "false" when not effective -> LegacyAutoCreateNotesFolderAction.RemoveKey
    | Some "true" -> LegacyAutoCreateNotesFolderAction.AdoptThenRemoveKey true
    | Some "false" -> LegacyAutoCreateNotesFolderAction.AdoptThenRemoveKey false
    | Some _ -> LegacyAutoCreateNotesFolderAction.RemoveKey

/// Moves the value of the old localStorage key into the user defaults of the main process.
/// An unanswered call keeps the key, so the next start tries again.
let migrateLegacyAutoCreateNotesFolder () : JS.Promise<unit> = promise {
    let key = SettingsPageDefaults.AutoCreateNotesFolderLocalStorageKey
    let stored = window.localStorage.getItem key |> Option.ofObj
    let! userDefaults = Api.ipcUserDefaultsApi.getUserDefaults ()

    match legacyAutoCreateNotesFolderAction stored userDefaults.Effective.AutoCreateNotesFolder with
    | LegacyAutoCreateNotesFolderAction.Leave -> ()
    | LegacyAutoCreateNotesFolderAction.RemoveKey -> window.localStorage.removeItem key
    | LegacyAutoCreateNotesFolderAction.AdoptThenRemoveKey value ->
        match!
            Api.ipcUserDefaultsApi.setUserDefaults {
                userDefaults.Effective with
                    AutoCreateNotesFolder = value
            }
        with
        | Ok _ -> window.localStorage.removeItem key
        | Error _ -> ()
}

let private isAutoCreateNotesFolderEnabled () : JS.Promise<bool> = promise {
    try
        let! userDefaults = Api.ipcUserDefaultsApi.getUserDefaults ()
        return userDefaults.Effective.AutoCreateNotesFolder
    with _ ->
        return true
}

/// The pick calls of the main process answer a closed dialog with an error that has this message.
/// A closed dialog is no failure, so callers ignore it.
let isPickCancelled (error: exn) : bool =
    System.String.Equals(error.Message, "Cancelled", System.StringComparison.OrdinalIgnoreCase)

let createErrorModalCallback
    (enqueueErrorModal: ErrorModalRequest -> unit)
    (title: string)
    (scopeId: string option)
    : (string -> unit) =
    fun errorMessage -> enqueueErrorModal (ErrorModalRequest.create (errorMessage, title = title, ?scopeId = scopeId))

let ensureNotesFolder (onError: string -> unit) : JS.Promise<unit> = promise {
    match! Api.ipcArcVaultApi.ensureNotesFolder () with
    | Ok() -> ()
    | Error exn -> onError exn.Message
}

let ensureNotesFolderIfEnabled (onError: string -> unit) : JS.Promise<unit> = promise {
    let! isEnabled = isAutoCreateNotesFolderEnabled ()

    if isEnabled then
        do! ensureNotesFolder onError
}

let openArc (onError: string -> unit) : JS.Promise<bool> = promise {
    match! Api.ipcArcVaultApi.openARC () with
    // ARC-open failures are presented by the Electron main process in a native dialog.
    | Error _ -> return false
    | Ok None -> return false
    | Ok(Some _) ->
        do! ensureNotesFolderIfEnabled onError
        return true
}

let openArcByPath (onError: string -> unit) (arcPath: string) : JS.Promise<bool> = promise {
    match! Api.ipcArcVaultApi.openARCByPath arcPath with
    // ARC-open failures are presented by the Electron main process in a native dialog.
    | Error _ -> return false
    | Ok _ ->
        do! ensureNotesFolderIfEnabled onError
        return true
}

let createArc (onError: string -> unit) (identifier: string) (initGit: bool) : JS.Promise<string option> = promise {
    let request = {
        identifier = identifier
        initGit = initGit
    }

    match! Api.ipcArcVaultApi.createARC request with
    | Error exn ->
        onError exn.Message
        return None
    | Ok CreateArcOutcome.Cancelled
    | Ok(CreateArcOutcome.FocusedExisting _) -> return None
    // Notes scaffolding reads the user default from the main process and needs the active vault IPC
    // of the creating renderer. A closed window therefore skips it on purpose. Path-only main-process
    // work (such as Git) has already completed before CreatedButClosed is returned.
    | Ok(CreateArcOutcome.CreatedButClosed _) -> return None
    | Ok(CreateArcOutcome.Created path) ->
        do! ensureNotesFolderIfEnabled onError
        return Some path
}
