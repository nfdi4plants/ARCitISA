module Renderer.Components.MainContent.TextDocument.TextDocumentTargetView

open Fable.Core
open Feliz
open Arcitisa.Components.Composite.MarkdownText.Links
open Arcitisa.Components.Page.FileViewer
open Arcitisa.Components.Page.FileViewer.Types
open Renderer.Components.Helper
open Arcitisa.Electron.Shared
open Arcitisa.Electron.Shared.FileIOTypes
open Renderer.Types
open Renderer.Components.MainContent.TextDocument.TextDocumentState
open Renderer.Components.MainContent.TextDocument.TextDocumentSync

module private TextDocumentTargetHelpers =

    /// File types with a rendered side next to the source.
    let renderingOf (fileType: FileContentType) =
        match fileType with
        | FileContentType.Markdown -> Some TextRendering.Markdown
        | FileContentType.Html -> Some TextRendering.Html
        | FileContentType.SVG -> Some TextRendering.Svg
        | _ -> None

open TextDocumentTargetHelpers

/// The text page of one file of the open ARC. TextDocumentSync owns the file: its state, its saves, its changes on
/// disk and the leave guard. TextDocument draws the page, and this connects the two with the links of the ARC.
[<ReactComponent(true)>]
let TextDocumentTarget (data: TextDocumentPageData) =
    let handleRef = React.useRef<TextDocumentHandle option> None
    let linkError, setLinkError = React.useState (None: string option)

    let document = useTextDocumentSync data handleRef
    let state = document.State
    let saving = state.SavesRunning > 0
    let status, isError = status state

    let linkContext =
        MarkdownLinkContext.useMarkdownLinkContext
            (LinkResolution.baseFolderOf data.Path)
            (fun message -> setLinkError (Some message))

    let navigation = FileNavigation.useFileNavigation ()

    let diskNoticeActions (current: DiskNotice) =
        let action label run = {
            Label = label
            Run = run
            Disabled = saving
        }

        match current with
        | RemovedFromDisk -> [ action "Save" document.Save ]
        | ChangedOnDisk
        | SaveConflict
        | SaveConflictMissing -> [
            action "Overwrite" document.Overwrite
            action "Reload from disk" document.ReloadFromDisk
          ]

    let notices = [
        match state.Notice with
        | Some current -> {
            Key = "disk"
            Message = noticeMessage current
            Actions = diskNoticeActions current
          }
        | None -> ()
        match linkError with
        | Some message -> {
            Key = "link"
            Message = message
            Actions = [
                {
                    Label = "Dismiss"
                    Run = (fun () -> setLinkError None)
                    Disabled = false
                }
            ]
          }
        | None -> ()
    ]

    TextDocument.TextDocument(
        data.Path,
        TextDocumentHelper.documentUri data.Path,
        state.SavedText,
        status,
        document.OnSave,
        ?rendering = renderingOf data.FileType,
        ?readOnlyReason = readOnlyText state,
        statusIsError = isError,
        saving = saving,
        dirty = state.Dirty,
        notices = notices,
        onDirtyChange = document.OnDirtyChange,
        // A link error belongs to the side it happened on.
        onModeChange = (fun () -> setLinkError None),
        // Closing asks about unsaved edits first, like opening another file.
        onClose = (fun () -> navigation.CloseFile() |> Promise.start),
        ?linkContext = linkContext,
        checkLinkTargets = MarkdownLinkContext.checkTargets,
        listFolder = MarkdownLinkContext.listFolder,
        ?schemaValidator = TextDocumentSchemas.tryFindValidator data.Path,
        handleRef = handleRef
    )
