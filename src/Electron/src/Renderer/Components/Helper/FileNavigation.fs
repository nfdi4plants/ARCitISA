/// Opening and closing ARC files as the page. Every entry point (a click in the File Explorer, a Markdown link, the
/// Close button of an editor toolbar) goes through here, so the leave guard is asked before the selection or the
/// page changes, and a Cancel leaves both as they are.
module Renderer.Components.Helper.FileNavigation

open Fable.Core
open Feliz
open Arcitisa.Electron.Shared
open Renderer.Types

/// What opening and closing need from the app. useFileNavigation fills it from the contexts.
type FileNavigationDeps = {
    /// The path of the open text document, if any.
    DocumentPath: unit -> string option
    ConfirmLeave: unit -> JS.Promise<bool>
    SetSelection: string option -> unit
    OpenView: string -> JS.Promise<Result<PageState, string>>
    SetPage: PageState option -> unit
}

/// Opens path as the page and selects it. A file that is not the open document asks the leave guard first. With
/// needsLfsDownload the file is only selected and the page stays empty, because its content is not on disk yet.
/// Resolves false when the user cancelled.
let openFileWith (deps: FileNavigationDeps) (path: string) (needsLfsDownload: bool) : JS.Promise<bool> = promise {
    let isOpenDocument =
        deps.DocumentPath()
        |> Option.exists (TextDocumentHelper.isSameDocumentPath path)

    let! canLeave =
        if isOpenDocument then
            Promise.lift true
        else
            deps.ConfirmLeave()

    if canLeave then
        deps.SetSelection(Some path)

        if needsLfsDownload then
            deps.SetPage None
        else
            match! deps.OpenView path with
            | Ok page -> deps.SetPage(Some page)
            | Error message -> deps.SetPage(Some(PageState.ErrorPage $"Could not open '{path}': {message}"))

    return canLeave
}

/// Closes the open file: after the leave guard, nothing is selected and no page shows. Resolves false when the
/// user cancelled.
let closeFileWith (deps: FileNavigationDeps) : JS.Promise<bool> = promise {
    let! canLeave = deps.ConfirmLeave()

    if canLeave then
        deps.SetSelection None
        deps.SetPage None

    return canLeave
}

type FileNavigation = {
    /// path and needsLfsDownload as in openFileWith.
    OpenFile: string -> bool -> JS.Promise<bool>
    CloseFile: unit -> JS.Promise<bool>
}

/// The deps of the open ARC, read from the contexts on every call.
[<Hook>]
let useFileNavigationDeps () : FileNavigationDeps =
    let fileState = Renderer.Context.FileStateContext.useFileStateCtx ()
    let pageState = Renderer.Context.PageStateContext.usePageStateCtx ()
    let leaveGuard = Renderer.Context.LeaveGuardContext.useLeaveGuardCtx ()

    // The functions run after awaits, so they read the latest contexts instead of the ones of their render.
    let fileStateRef = React.useRef fileState
    let pageStateRef = React.useRef pageState
    let leaveGuardRef = React.useRef leaveGuard
    fileStateRef.current <- fileState
    pageStateRef.current <- pageState
    leaveGuardRef.current <- leaveGuard

    React.useMemo (
        (fun () -> {
            DocumentPath = fun () -> leaveGuardRef.current.documentPath ()
            ConfirmLeave = fun () -> leaveGuardRef.current.confirmLeave ()
            SetSelection =
                fun path ->
                    fileStateRef.current.setSelection (
                        match path with
                        | Some p -> ArcSelection.forTreePath (Some p)
                        | None -> ArcSelection.empty
                    )
            OpenView = ArcViewHelper.openView
            SetPage = fun page -> pageStateRef.current.setState page
        }),
        [||]
    )

[<Hook>]
let useFileNavigation () : FileNavigation =
    let deps = useFileNavigationDeps ()

    React.useMemo (
        (fun () -> {
            OpenFile = openFileWith deps
            CloseFile = fun () -> closeFileWith deps
        }),
        [| box deps |]
    )
