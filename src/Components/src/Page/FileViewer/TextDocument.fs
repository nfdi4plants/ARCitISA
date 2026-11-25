namespace Swate.Components.Page.FileViewer

open Fable.Core
open Feliz
open Swate.Components
open Swate.Components.Util
open Swate.Components.Composite.TextEditor
open Swate.Components.Composite.TextEditor.Types
open Swate.Components.Composite.TextEditor.SchemaValidation
open Swate.Components.Composite.EditorToolbar
open Swate.Components.Composite.MarkdownText
open Swate.Components.Composite.MarkdownText.Editing
open Swate.Components.Composite.MarkdownText.Editing.Types
open Swate.Components.Composite.MarkdownText.Links
open Swate.Components.Composite.MarkdownText.Links.Types
open Swate.Components.Page.FileViewer.Helper
open Swate.Components.Page.FileViewer.Types

[<Erase; Mangle(false)>]
type TextDocument =

    /// A bar above the editor. role is "status" for information and "alert" for a warning.
    [<ReactComponent>]
    static member private Notice
        (role: string, className: string, message: string, ?actions: TextDocumentNoticeAction list, ?key: string)
        =
        Html.div [
            prop.role role
            prop.className ("swt:alert swt:rounded-none swt:flex swt:flex-wrap swt:gap-2 " + className)
            prop.children [
                Html.span [ prop.text message ]
                for action in defaultArg actions [] do
                    Html.button [
                        prop.type'.button
                        prop.key action.Label
                        prop.className "swt:btn swt:btn-sm"
                        prop.disabled action.Disabled
                        prop.text action.Label
                        prop.onClick (fun _ -> action.Run())
                    ]
            ]
        ]

    /// One text file in an editor, for a host that owns the file. The host saves through onSave and shows the result
    /// in status, statusIsError, saving and dirty. savedText is the text of the last save or read; a remounted editor
    /// starts from it. Markdown, HTML and SVG get a rendered side. They open on the side chosen last in any text
    /// document, which localStorage keeps. Before the first choice Markdown opens for writing, HTML and SVG open as
    /// they look. readOnlyReason makes the editor read-only and says why. notices are the host's warnings, such as a
    /// change on disk. onModeChange hears a switch between the editor and the rendered side. onClose adds the Close
    /// file button next to Save. For Markdown, linkContext resolves the links and images of the rendered side,
    /// checkLinkTargets marks broken links in the editor and listFolder completes paths. Pass the same function
    /// instances across renders, a new instance checks or lists again. handleRef gives the host replaceText, save and
    /// isDirty.
    [<ReactComponent>]
    static member TextDocument
        (
            path: string,
            documentUri: string,
            savedText: string,
            status: string,
            onSave: TextEditorSnapshot -> JS.Promise<bool>,
            ?rendering: TextRendering,
            ?readOnlyReason: string,
            ?statusIsError: bool,
            ?saving: bool,
            ?dirty: bool,
            ?notices: TextDocumentNotice list,
            ?onDirtyChange: bool -> unit,
            ?onModeChange: unit -> unit,
            ?onClose: unit -> unit,
            ?linkContext: IMarkdownLinkContext,
            ?checkLinkTargets: string[] -> JS.Promise<LinkTargetState[]>,
            ?listFolder: ListFolderRequest -> JS.Promise<FolderContents option>,
            ?schemaValidator: Validator.SchemaValidator,
            ?handleRef: IRefValue<TextDocumentHandle option>
        ) =
        let statusIsError = defaultArg statusIsError false
        let saving = defaultArg saving false
        let dirty = defaultArg dirty false
        let isReadOnly = readOnlyReason.IsSome
        let isMarkdown = rendering = Some TextRendering.Markdown
        let baseFolder = LinkResolution.baseFolderOf path

        let editorRef = React.useRef<TextEditorHandle option> None
        let focusEditorRef = React.useRef false
        let markdownMessage, setMarkdownMessage = React.useState (None: string option)
        let ontologyReferenceOpen, setOntologyReferenceOpen = React.useState false

        // The side chosen last in any text document. The default None leaves the key unset, because the hook would
        // write a default into localStorage on mount, and then the first file opened would choose for all others.
        let rememberedShowRendered, rememberShowRendered =
            React.useLocalStorage<bool option> (Keys.mkLocalStorageKey "fileViewer" "textDocument" "showRendered", None)

        // Without a remembered side, Markdown opens for writing, HTML and SVG open as they look. This state is read
        // from localStorage only on mount, so a switch in another document does not move this one. The editor mounts
        // on first use and then stays mounted while the rendered side shows, so cursor and undo history survive the
        // switch.
        let showRendered, setShowRendered =
            React.useState (
                match rendering, rememberedShowRendered with
                | None, _ -> false
                | Some _, Some remembered -> remembered
                | Some TextRendering.Html, None
                | Some TextRendering.Svg, None -> true
                | Some _, None -> false
            )

        let editorMounted, setEditorMounted = React.useState (not showRendered)
        let renderedText, setRenderedText = React.useState savedText

        // The same object for the life of the page. The state setters it calls stay the same too.
        let handle =
            React.useMemo (
                (fun () ->
                    { new TextDocumentHandle with
                        member _.replaceText text =
                            // Without an editor, the editor mounts later from savedText, which the host updates.
                            editorRef.current |> Option.iter (fun editor -> editor.replaceContent text)
                            setMarkdownMessage None
                            setRenderedText text

                        member _.save() =
                            match editorRef.current with
                            | Some editor -> editor.save ()
                            | None -> Promise.lift false

                        member _.isDirty() =
                            editorRef.current |> Option.exists (fun editor -> editor.isDirty ())
                    }
                ),
                [||]
            )

        React.useEffectOnce (fun () ->
            handleRef |> Option.iter (fun r -> r.current <- Some handle)

            { new System.IDisposable with
                member _.Dispose() =
                    handleRef |> Option.iter (fun r -> r.current <- None)
            }
        )

        let surface =
            React.useMemo ((fun () -> MarkdownSurface.ofTextEditor editorRef), [||])

        let linkLint =
            React.useMemo (
                (fun () ->
                    match checkLinkTargets with
                    | Some check when isMarkdown -> Some(LinkDiagnostics.lint baseFolder check)
                    | _ -> None
                ),
                [| box baseFolder; box isMarkdown; box checkLinkTargets |]
            )

        // Lists folders while a link or image target that starts with "/", "./" or "../" is typed.
        let pathCompletion =
            React.useMemo (
                (fun () ->
                    match listFolder with
                    | Some list when isMarkdown -> Some(PathCompletion.source baseFolder list)
                    | _ -> None
                ),
                [| box baseFolder; box isMarkdown; box listFolder |]
            )

        let lineEndings =
            React.useMemo ((fun () -> LineEndings.notice savedText), [| box savedText |])

        // The editor reports it, so Undo and Redo in the toolbar are enabled only with a step to take.
        let history, setHistory =
            React.useState ({| canUndo = false; canRedo = false |}: TextEditorHistory)

        // The click moved the focus to the toolbar button, so the editor gets it back after the step.
        let stepHistory (step: TextEditorHandle -> bool) () =
            editorRef.current
            |> Option.iter (fun editor ->
                step editor |> ignore
                editor.focus ()
            )

        let markdownKeyBindings =
            React.useMemo (
                (fun () ->
                    MarkdownCommands.keyBindingsWith setMarkdownMessage (fun () -> setOntologyReferenceOpen true)
                ),
                [||]
            )

        // The editor takes focus only after the render that shows it. On the first switch it mounts in that render.
        React.useEffect (
            (fun () ->
                if focusEditorRef.current && not showRendered then
                    focusEditorRef.current <- false
                    editorRef.current |> Option.iter (fun editor -> editor.focus ())
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
                    box rendering
                    box linkContext
                |]
            )

        let showEditor () =
            onModeChange |> Option.iter (fun f -> f ())
            rememberShowRendered (Some false)
            setEditorMounted true
            setShowRendered false
            focusEditorRef.current <- true

        // The rendered side shows the text in the editor, saved or not.
        let showRenderedView () =
            onModeChange |> Option.iter (fun f -> f ())
            rememberShowRendered (Some true)
            setMarkdownMessage None

            setRenderedText (
                editorRef.current
                |> Option.map (fun editor -> editor.getText ())
                |> Option.defaultValue savedText
            )

            setShowRendered true

        Html.div [
            prop.className "swt:size-full swt:min-w-0 swt:min-h-0 swt:flex swt:flex-col swt:bg-base-100"
            prop.children [
                Html.div [
                    prop.className
                        "swt:flex swt:flex-wrap swt:items-center swt:gap-2 swt:px-3 swt:py-2 swt:border-b swt:border-base-300"
                    prop.children [
                        Html.span [
                            prop.className "swt:font-mono swt:text-sm swt:truncate"
                            prop.title path
                            prop.text path
                        ]
                        Html.span [
                            prop.className (
                                if statusIsError then
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
                                    prop.disabled (isReadOnly || saving || not dirty)
                                    prop.title (Shortcuts.title (Shortcuts.isMac ()) "Save" Shortcuts.saveKey)
                                    prop.text "Save"
                                    prop.onClick (fun _ -> handle.save () |> ignore)
                                ]
                                match onClose with
                                | Some close ->
                                    EditorToolbar.ToolbarButton("Close file", "swt:fluent--dismiss-24-regular", close)
                                | None -> Html.none
                            ]
                        ]
                    ]
                ]
                // Every slot of this list renders something, at least Html.none, and the editor has its own key. A
                // slot that drops out shifts the index keys of the slots after it, and React then remounts the editor
                // from the last saved text, which loses the unsaved edits.
                if showRendered then
                    // The rendered side has nothing to edit, so it has no toolbar.
                    Html.none
                elif isMarkdown then
                    MarkdownToolbar.MarkdownToolbar(
                        surface,
                        history,
                        disabled = isReadOnly,
                        ?disabledReason = readOnlyReason,
                        ?message = markdownMessage,
                        setMessage = setMarkdownMessage,
                        isOntologyReferenceOpen = ontologyReferenceOpen,
                        setOntologyReferenceOpen = setOntologyReferenceOpen
                    )
                else
                    EditorToolbar.EditorToolbar(
                        children =
                            EditorToolbar.HistoryButtons(
                                history,
                                stepHistory (fun editor -> editor.undo ()),
                                stepHistory (fun editor -> editor.redo ()),
                                disabled = isReadOnly,
                                ?disabledReason = readOnlyReason
                            )
                    )
                match readOnlyReason with
                | Some reason -> TextDocument.Notice("status", "swt:alert-info", reason)
                | None -> Html.none
                match lineEndings with
                | Some text when not isReadOnly -> TextDocument.Notice("status", "swt:alert-info", text)
                | _ -> Html.none
                React.Fragment [
                    for notice in defaultArg notices [] do
                        TextDocument.Notice(
                            "alert",
                            "swt:alert-warning",
                            notice.Message,
                            actions = notice.Actions,
                            key = notice.Key
                        )
                ]
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
                                documentUri,
                                // The last saved text, so a remount after a switch keeps a save that happened in
                                // between.
                                savedText,
                                fileName = path,
                                readOnly = isReadOnly,
                                ?schemaValidator = schemaValidator,
                                ?extraLint = linkLint,
                                onSave = onSave,
                                ?onDirtyChange = onDirtyChange,
                                ?keyBindings = (if isMarkdown then Some markdownKeyBindings else None),
                                ?completions = pathCompletion,
                                onChange = (fun () -> setMarkdownMessage None),
                                onHistoryChange = setHistory,
                                handleRef = editorRef
                            )
                        ]
                    ]
                else
                    Html.none
                renderedElement
            ]
        ]
