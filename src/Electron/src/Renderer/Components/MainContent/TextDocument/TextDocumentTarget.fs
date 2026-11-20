module Renderer.Components.MainContent.TextDocument.TextDocumentTargetView

open Fable.Core
open Feliz
open Swate.Components.Composite.TextEditor
open Swate.Components.Composite.TextEditor.Types
open Swate.Components.Composite.EditorToolbar
open Swate.Components.Composite.MarkdownText
open Swate.Components.Composite.MarkdownText.Editing
open Swate.Components.Composite.MarkdownText.Editing.Types
open Swate.Components.Composite.MarkdownText.Links
open Swate.Components.Page.FileViewer
open Swate.Components.Page.FileViewer.Types
open Swate.Components.Util
open Renderer.Components.Helper
open Swate.Electron.Shared
open Swate.Electron.Shared.FileIOTypes
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

    let alert (role: string) (className: string) (children: ReactElement list) =
        Html.div [
            prop.role role
            prop.className ("swt:alert swt:rounded-none swt:flex swt:flex-wrap swt:gap-2 " + className)
            prop.children children
        ]

open TextDocumentTargetHelpers

/// The text editor page for one file. TextDocumentSync owns the file's state and its saves, so this view only
/// renders it: the header, the toolbar, the notices, the editor and the rendered side.
[<ReactComponent(true)>]
let TextDocumentTarget (data: TextDocumentPageData) =
    let handleRef = React.useRef<TextEditorHandle option> None
    let focusEditorRef = React.useRef false
    let linkError, setLinkError = React.useState (None: string option)
    let markdownMessage, setMarkdownMessage = React.useState (None: string option)
    let ontologyReferenceOpen, setOntologyReferenceOpen = React.useState false

    // Markdown opens for writing, HTML and SVG open as they look. The editor mounts on first use and then stays
    // mounted while the rendered side shows, so cursor and undo history survive the switch.
    let rendering = renderingOf data.FileType

    let showRendered, setShowRendered =
        React.useState (
            match rendering with
            | Some TextRendering.Html
            | Some TextRendering.Svg -> true
            | _ -> false
        )

    let editorMounted, setEditorMounted = React.useState (not showRendered)
    let renderedText, setRenderedText = React.useState data.Content

    let document =
        useTextDocumentSync
            data
            handleRef
            (fun text ->
                setMarkdownMessage None
                setRenderedText text
            )

    let state = document.State
    let readOnlyText = readOnlyText state
    let isReadOnly = readOnlyText.IsSome
    let isMarkdown = rendering = Some TextRendering.Markdown
    let saving = state.SavesRunning > 0
    let baseFolder = LinkResolution.baseFolderOf data.Path

    let linkContext =
        MarkdownLinkContext.useMarkdownLinkContext baseFolder (fun message -> setLinkError (Some message))

    let surface =
        React.useMemo ((fun () -> MarkdownSurface.ofTextEditor handleRef), [||])

    let linkLint =
        React.useMemo (
            (fun () ->
                if isMarkdown then
                    Some(LinkDiagnostics.lint baseFolder MarkdownLinkContext.checkTargets)
                else
                    None
            ),
            [| box baseFolder; box isMarkdown |]
        )

    // Lists ARC folders while a link or image target that starts with "/", "./" or "../" is typed.
    let pathCompletion =
        React.useMemo (
            (fun () ->
                if isMarkdown then
                    Some(PathCompletion.source baseFolder MarkdownLinkContext.listFolder)
                else
                    None
            ),
            [| box baseFolder; box isMarkdown |]
        )

    let lineEndings =
        React.useMemo ((fun () -> lineEndingNotice state.SavedText), [| box state.SavedText |])

    // The editor reports it, so Undo and Redo in the toolbar are enabled only with a step to take.
    let history, setHistory =
        React.useState ({| canUndo = false; canRedo = false |}: TextEditorHistory)

    // The click moved the focus to the toolbar button, so the editor gets it back after the step.
    let stepHistory (step: TextEditorHandle -> bool) () =
        handleRef.current
        |> Option.iter (fun handle ->
            step handle |> ignore
            handle.focus ()
        )

    let navigation = FileNavigation.useFileNavigation ()
    // Closing asks about unsaved edits first, like opening another file.
    let closeFile () = navigation.CloseFile() |> Promise.start

    let markdownKeyBindings =
        React.useMemo (
            (fun () -> MarkdownCommands.keyBindingsWith setMarkdownMessage (fun () -> setOntologyReferenceOpen true)),
            [||]
        )

    // The editor takes focus only after the render that shows it. On the first switch it mounts in that render.
    React.useEffect (
        (fun () ->
            if focusEditorRef.current && not showRendered then
                focusEditorRef.current <- false
                handleRef.current |> Option.iter (fun h -> h.focus ())
        ),
        [| box showRendered; box editorMounted |]
    )

    let renderedElement =
        React.useMemo (
            (fun () ->
                match rendering with
                | Some current when showRendered ->
                    RenderedText.RenderedText(current, renderedText, ?linkContext = linkContext)
                | _ -> Html.none
            ),
            [|
                box showRendered
                box renderedText
                box data.FileType
                box linkContext
            |]
        )

    let status, isError = status state

    let showEditor () =
        setLinkError None
        setEditorMounted true
        setShowRendered false
        focusEditorRef.current <- true

    // The rendered side shows the text in the editor, saved or not.
    let showRenderedView () =
        setLinkError None
        setMarkdownMessage None

        setRenderedText (
            handleRef.current
            |> Option.map (fun h -> h.getText ())
            |> Option.defaultValue state.SavedText
        )

        setShowRendered true

    let noticeButtons (current: DiskNotice) =
        match current with
        | RemovedFromDisk -> [ ("Save", document.Save) ]
        | ChangedOnDisk
        | SaveConflict
        | SaveConflictMissing -> [
            "Overwrite", document.Overwrite
            "Reload from disk", document.ReloadFromDisk
          ]

    Html.div [
        prop.className "swt:size-full swt:min-w-0 swt:min-h-0 swt:flex swt:flex-col swt:bg-base-100"
        prop.children [
            Html.div [
                prop.className
                    "swt:flex swt:flex-wrap swt:items-center swt:gap-2 swt:px-3 swt:py-2 swt:border-b swt:border-base-300"
                prop.children [
                    Html.span [
                        prop.className "swt:font-mono swt:text-sm swt:truncate"
                        prop.title data.Path
                        prop.text data.Path
                    ]
                    Html.span [
                        prop.className (
                            if isError then
                                "swt:text-xs swt:text-error"
                            else
                                "swt:text-xs swt:opacity-70"
                        )
                        prop.text status
                    ]
                    Html.div [
                        prop.className "swt:ml-auto swt:flex swt:items-center swt:gap-2"
                        prop.children [
                            match rendering with
                            | Some current ->
                                RenderedText.ModeToggle(current, showRendered, showEditor, showRenderedView)
                            | None -> Html.none
                            Html.button [
                                prop.type'.button
                                prop.className "swt:btn swt:btn-sm swt:btn-primary"
                                prop.disabled (isReadOnly || saving || not state.Dirty)
                                prop.title (Shortcuts.title (Shortcuts.isMac ()) "Save" Shortcuts.saveKey)
                                prop.text "Save"
                                prop.onClick (fun _ -> document.Save())
                            ]
                        ]
                    ]
                ]
            ]
            // Every slot of this list renders something, at least Html.none, and the editor has its own key. A slot
            // that drops out shifts the index keys of the slots after it, and React then remounts the editor from the
            // last saved text, which loses the unsaved edits.
            if showRendered then
                // The rendered side has nothing to edit, so its bar only closes the file.
                EditorToolbar.EditorToolbar(onClose = closeFile)
            elif isMarkdown then
                MarkdownToolbar.MarkdownToolbar(
                    surface,
                    history,
                    disabled = isReadOnly,
                    ?disabledReason = readOnlyText,
                    ?message = markdownMessage,
                    setMessage = setMarkdownMessage,
                    isOntologyReferenceOpen = ontologyReferenceOpen,
                    setOntologyReferenceOpen = setOntologyReferenceOpen,
                    onClose = closeFile
                )
            else
                EditorToolbar.EditorToolbar(
                    children =
                        EditorToolbar.HistoryButtons(
                            history,
                            stepHistory (fun handle -> handle.undo ()),
                            stepHistory (fun handle -> handle.redo ()),
                            disabled = isReadOnly,
                            ?disabledReason = readOnlyText
                        ),
                    onClose = closeFile
                )
            match readOnlyText with
            | Some text -> alert "status" "swt:alert-info" [ Html.span [ prop.text text ] ]
            | None -> Html.none
            match lineEndings with
            | Some text when not isReadOnly -> alert "status" "swt:alert-info" [ Html.span [ prop.text text ] ]
            | _ -> Html.none
            match state.Notice with
            | Some current ->
                alert "alert" "swt:alert-warning" [
                    Html.span [ prop.text (noticeMessage current) ]
                    for label, action in noticeButtons current do
                        Html.button [
                            prop.type'.button
                            prop.key label
                            prop.className "swt:btn swt:btn-sm"
                            prop.disabled saving
                            prop.text label
                            prop.onClick (fun _ -> action ())
                        ]
                ]
            | None -> Html.none
            match linkError with
            | Some message ->
                alert "alert" "swt:alert-warning" [
                    Html.span [ prop.text message ]
                    Html.button [
                        prop.type'.button
                        prop.className "swt:btn swt:btn-sm"
                        prop.text "Dismiss"
                        prop.onClick (fun _ -> setLinkError None)
                    ]
                ]
            | None -> Html.none
            if editorMounted then
                Html.div [
                    prop.key "text-editor"
                    prop.className [
                        "swt:flex-1 swt:min-h-0"
                        if showRendered then
                            "swt:hidden"
                    ]
                    prop.children [
                        TextEditor.TextEditor(
                            TextDocumentHelper.documentUri data.Path,
                            // The last saved text, so a remount after a switch keeps a save that happened in between.
                            state.SavedText,
                            fileName = data.Path,
                            readOnly = isReadOnly,
                            ?schemaValidator = TextDocumentSchemas.tryFindValidator data.Path,
                            ?extraLint = linkLint,
                            onSave = document.OnSave,
                            onDirtyChange = document.OnDirtyChange,
                            ?keyBindings = (if isMarkdown then Some markdownKeyBindings else None),
                            ?completions = pathCompletion,
                            onChange = (fun () -> setMarkdownMessage None),
                            onHistoryChange = setHistory,
                            handleRef = handleRef
                        )
                    ]
                ]
            else
                Html.none
            renderedElement
        ]
    ]
