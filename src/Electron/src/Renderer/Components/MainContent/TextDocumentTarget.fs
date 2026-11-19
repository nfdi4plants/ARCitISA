module Renderer.Components.MainContent.TextDocumentTargetView

open Fable.Core
open Feliz
open Swate.Components.Composite.TextEditor
open Swate.Components.Composite.TextEditor.Types
open Swate.Components.Composite.MarkdownText
open Swate.Components.Composite.MarkdownText.Editing
open Swate.Components.Composite.MarkdownText.Editing.Types
open Swate.Components.Composite.MarkdownText.Links
open Renderer.Components.Helper
open Swate.Electron.Shared
open Swate.Electron.Shared.FileIOTypes
open Renderer.Types
open Renderer.Context.LeaveGuardContext

module private TextDocumentTargetHelpers =

    /// Why the file on disk and the editor no longer agree.
    type DiskNotice =
        /// Another program changed the file while the editor has unsaved edits.
        | ChangedOnDisk
        /// A save was refused because the file changed since it was read.
        | SaveConflict
        /// A save was refused because the file is gone.
        | SaveConflictMissing
        /// The file was deleted, or freed to a large file pointer, while the editor has unsaved edits.
        | RemovedFromDisk

    let readOnlyMessage (reason: TextReadOnlyReason) =
        match reason with
        | TextReadOnlyReason.NotUtf8 ->
            "This file is not valid UTF-8. It opens read-only so a save cannot change its bytes."
        | TextReadOnlyReason.InvalidUtf16 ->
            "This file contains invalid UTF-16. It opens read-only so a save cannot change its bytes."
        | TextReadOnlyReason.TooLargeToEdit -> "This file is larger than 5 MB and opens read-only."
        | TextReadOnlyReason.SymbolicLink -> "This file is a link. It opens read-only."
        | TextReadOnlyReason.OutsideArc -> "This file lies outside the open ARC. It opens read-only."
        | TextReadOnlyReason.WriteProtected -> "This file is write-protected. It opens read-only."
        | TextReadOnlyReason.ChangedWhileReading -> "This file changed while it was read. Reload it to edit."

    /// File types with a rendered side next to the source.
    type Rendering =
        | RenderedMarkdown
        | RenderedHtml
        | RenderedSvg

    let renderingOf (fileType: FileContentType) =
        match fileType with
        | FileContentType.Markdown -> Some RenderedMarkdown
        | FileContentType.Html -> Some RenderedHtml
        | FileContentType.SVG -> Some RenderedSvg
        | _ -> None

    /// Markdown goes through the sanitizer of the Markdown preview. HTML runs in a sandboxed frame: scripts work,
    /// but without allow-same-origin the page gets an opaque origin and cannot reach the app. SVG is an image, so
    /// its scripts never run.
    let renderedView (linkContext: IMarkdownLinkContext option) (rendering: Rendering) (text: string) : ReactElement =
        match rendering with
        | RenderedMarkdown ->
            Html.div [
                prop.className "wmde-markdown-var swt:flex-1 swt:min-h-0 swt:overflow-auto"
                prop.children [
                    MarkdownView.MarkdownView(text, className = "swt:p-4", ?linkContext = linkContext)
                ]
            ]
        | RenderedHtml ->
            Html.iframe [
                prop.title "HTML preview"
                // A srcdoc page resolves URLs against the app's URL, so a link to #section loaded the app into the
                // frame. Against about:srcdoc, #section scrolls inside the page, and other relative URLs no longer
                // resolve at all, so they cannot reach the app's files. It also replaces a base URL the page sets.
                // A srcdoc page never renders in quirks mode, so markup before its doctype changes nothing.
                prop.custom ("srcDoc", """<base href="about:srcdoc">""" + text)
                prop.custom ("sandbox", "allow-scripts")
                prop.className "swt:flex-1 swt:min-h-0 swt:w-full swt:border-none swt:bg-white"
            ]
        | RenderedSvg ->
            Html.div [
                prop.className "swt:flex-1 swt:min-h-0 swt:overflow-auto swt:p-4 swt:flex swt:justify-center"
                prop.children [
                    Html.img [
                        prop.alt "SVG preview"
                        prop.src ("data:image/svg+xml;charset=utf-8," + JS.encodeURIComponent text)
                        prop.className "swt:max-w-full swt:self-start"
                    ]
                ]
            ]

    let noticeMessage (notice: DiskNotice) =
        match notice with
        | ChangedOnDisk -> "The file changed on disk since you opened it."
        | SaveConflict -> "The file changed on disk since you opened it. Your changes are not saved."
        | SaveConflictMissing -> "The file no longer exists on disk. Your changes are not saved."
        | RemovedFromDisk -> "The file is no longer available on disk. Save to write your version again."

open TextDocumentTargetHelpers

/// The text editor page for one file. It saves through saveTextFile with the hash it read, so a save never
/// overwrites a change made outside the app without asking.
[<ReactComponent(true)>]
let TextDocumentTarget (data: TextDocumentPageData) =
    let leaveGuard = useLeaveGuardCtx ()

    let expansionCtx =
        Renderer.Context.FileTreeExpansionContext.useFileTreeExpansionCtx ()

    let handleRef = React.useRef<TextEditorHandle option> None

    let baseHashRef =
        React.useRef (data.Info |> Option.map _.contentHash |> Option.defaultValue "")

    let lastSavedTextRef = React.useRef data.Content
    let overwriteNextRef = React.useRef false
    let savingCountRef = React.useRef 0
    let focusEditorRef = React.useRef false
    let missingOnDiskRef = React.useRef false
    let dirty, setDirty = React.useState false
    let saving, setSaving = React.useState false
    let error, setError = React.useState (None: string option)
    let notice, setNotice = React.useState (None: DiskNotice option)
    let linkError, setLinkError = React.useState (None: string option)
    let baseFolder = LinkResolution.baseFolderOf data.Path

    let linkContext =
        MarkdownLinkContext.useMarkdownLinkContext baseFolder (fun message -> setLinkError (Some message))

    // Markdown opens for writing, HTML and SVG open as they look. The editor mounts on first use and then stays
    // mounted while the rendered side shows, so cursor and undo history survive the switch.
    let rendering = renderingOf data.FileType

    let showRendered, setShowRendered =
        React.useState (
            match rendering with
            | Some RenderedHtml
            | Some RenderedSvg -> true
            | _ -> false
        )

    let editorMounted, setEditorMounted = React.useState (not showRendered)
    let renderedText, setRenderedText = React.useState data.Content

    let readOnlyText =
        match data.Info with
        | None -> Some "This file could not be read as text. It opens read-only."
        | Some info -> info.readOnly |> Option.map readOnlyMessage

    let isReadOnly = readOnlyText.IsSome
    let isMarkdown = rendering = Some RenderedMarkdown

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

    let markdownMessage, setMarkdownMessage = React.useState (None: string option)
    let ontologyReferenceOpen, setOntologyReferenceOpen = React.useState false

    let markdownKeyBindings =
        React.useMemo (
            (fun () -> MarkdownCommands.keyBindingsWith setMarkdownMessage (fun () -> setOntologyReferenceOpen true)),
            [||]
        )

    let onSave (snapshot: TextEditorSnapshot) : JS.Promise<bool> = promise {
        setSaving true
        savingCountRef.current <- savingCountRef.current + 1
        setError None

        let request: SaveTextFileRequest = {|
            path = data.Path
            content = snapshot.text
            encoding = data.Info |> Option.map _.encoding |> Option.defaultValue TextFileEncoding.Utf8
            // A file known to be missing is created, and the save is refused if one appeared meanwhile.
            expectedHash =
                if missingOnDiskRef.current then
                    None
                else
                    Some baseHashRef.current
            overwrite = overwriteNextRef.current
        |}

        overwriteNextRef.current <- false

        let! result = Api.ipcArcVaultApi.saveTextFile request |> Promise.catch (fun e -> Error e)

        setSaving false
        savingCountRef.current <- savingCountRef.current - 1

        match result with
        | Ok(SaveTextFileOutcome.Saved info) ->
            baseHashRef.current <- info.contentHash
            lastSavedTextRef.current <- snapshot.text
            missingOnDiskRef.current <- false
            // The tree sees the new file stamp as our own write and does not reload the page.
            expansionCtx.recordOwnWrite ()
            setNotice None
            return true
        | Ok(SaveTextFileOutcome.Conflict current) ->
            setNotice (Some(if current.IsSome then SaveConflict else SaveConflictMissing))
            return false
        | Error e ->
            setError (Some $"Saving failed: {e.Message}")
            return false
    }

    let onDirtyChange (value: bool) =
        setDirty value
        leaveGuard.setDirty value

    let save () =
        handleRef.current |> Option.iter (fun h -> h.save () |> ignore)

    let overwrite () =
        match handleRef.current with
        | Some h ->
            overwriteNextRef.current <- true
            // save calls no onSave for a read-only editor or while a save runs, so overwrite clears the flag itself.
            h.save () |> Promise.map (fun _ -> overwriteNextRef.current <- false) |> ignore
        | None -> ()

    let reloadFromDisk () =
        promise {
            let! result = Api.ipcArcVaultApi.openFile data.Path

            match result with
            | Ok opened ->
                match opened.text with
                | Some info ->
                    handleRef.current
                    |> Option.iter (fun handle -> handle.replaceContent opened.content.content)

                    setMarkdownMessage None

                    baseHashRef.current <- info.contentHash
                    lastSavedTextRef.current <- opened.content.content
                    missingOnDiskRef.current <- false
                    setRenderedText opened.content.content
                    setNotice None
                    setError None
                | None -> setError (Some "The file could not be read as text.")
            | Error e -> setError (Some $"Reloading failed: {e.Message}")
        }
        |> Promise.start

    // The same path opens again after a change on disk. A hash equal to the base is the echo of our own save.
    React.useEffect (
        (fun () ->
            match data.Info with
            | Some info when info.contentHash <> baseHashRef.current ->
                match handleRef.current with
                | Some handle when handle.isDirty () -> setNotice (Some ChangedOnDisk)
                | handle ->
                    // Without an editor (an HTML file that was only viewed) the rendered side still follows the disk.
                    handle |> Option.iter (fun h -> h.replaceContent data.Content)
                    setMarkdownMessage None
                    baseHashRef.current <- info.contentHash
                    lastSavedTextRef.current <- data.Content
                    setRenderedText data.Content
                    setNotice None
            | _ -> ()
        ),
        [| box data.Content; box data.Info |]
    )

    React.useEffect (
        (fun () ->
            let unregister =
                leaveGuard.register {
                    documentPath = data.Path
                    isDirty = fun () -> handleRef.current |> Option.exists (fun h -> h.isDirty ())
                    save =
                        fun () ->
                            match handleRef.current with
                            | Some h -> h.save ()
                            | None -> Promise.lift false
                    isSaving = fun () -> savingCountRef.current > 0
                    discard =
                        fun () ->
                            handleRef.current
                            |> Option.iter (fun h -> h.replaceContent lastSavedTextRef.current)

                            setMarkdownMessage None
                    onRemovedFromDisk =
                        fun () ->
                            missingOnDiskRef.current <- true
                            setNotice (Some RemovedFromDisk)
                }

            { new System.IDisposable with
                member _.Dispose() = unregister ()
            }
        ),
        [| box data.Path |]
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
                | Some current when showRendered -> renderedView linkContext current renderedText
                | _ -> Html.none
            ),
            [|
                box showRendered
                box renderedText
                box data.FileType
                box linkContext
            |]
        )

    let status, statusClass =
        match isReadOnly, saving, error with
        | true, _, _ -> "Read-only", "swt:opacity-70"
        | _, true, _ -> "Saving...", "swt:opacity-70"
        | _, _, Some message -> message, "swt:text-error"
        | _ when dirty -> "Unsaved changes", "swt:opacity-70"
        | _ -> "Saved", "swt:opacity-70"

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
            |> Option.defaultValue lastSavedTextRef.current
        )

        setShowRendered true

    let modeToggle =
        match rendering with
        | None -> Html.none
        | Some current ->
            // DaisyUI radio buttons: the checked one gets the primary colors, the label comes from aria-label
            // (drawn in ::after) and the icon is drawn in ::before. Only the unchecked one is a ghost button,
            // because .btn-ghost would also clear the background of the checked one.
            let modeRadio (label: string) (iconClass: string) (active: bool) (onSelect: unit -> unit) =
                Html.input [
                    prop.type'.radio
                    prop.key label
                    prop.name "text-document-mode"
                    prop.ariaLabel label
                    prop.isChecked active
                    prop.onCheckedChange (fun isChecked ->
                        if isChecked then
                            onSelect ()
                    )
                    prop.className [
                        "swt:join-item swt:btn swt:btn-sm swt:before:iconify"
                        iconClass
                        if not active then
                            "swt:btn-ghost"
                    ]
                ]

            let editRadio =
                modeRadio "Edit" "swt:before:fluent--edit-24-regular" (not showRendered) showEditor

            let renderedLabel = if current = RenderedMarkdown then "Preview" else "View"

            let renderedRadio =
                modeRadio renderedLabel "swt:before:fluent--eye-24-regular" showRendered showRenderedView

            Html.div [
                prop.role "radiogroup"
                prop.ariaLabel "Show the file as"
                prop.className "swt:join"
                prop.children (
                    if current = RenderedMarkdown then
                        [ editRadio; renderedRadio ]
                    else
                        [ renderedRadio; editRadio ]
                )
            ]

    let noticeButtons (current: DiskNotice) =
        match current with
        | RemovedFromDisk -> [ ("Save", save) ]
        | ChangedOnDisk
        | SaveConflict
        | SaveConflictMissing -> [
            "Overwrite", overwrite
            "Reload from disk", reloadFromDisk
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
                        prop.className $"swt:text-xs {statusClass}"
                        prop.text status
                    ]
                    Html.div [
                        prop.className "swt:ml-auto swt:flex swt:items-center swt:gap-2"
                        prop.children [
                            modeToggle
                            Html.button [
                                prop.type'.button
                                prop.className "swt:btn swt:btn-sm swt:btn-primary"
                                prop.disabled (isReadOnly || saving || not dirty)
                                prop.title (Shortcuts.title (Shortcuts.isMac ()) "Save" Shortcuts.saveKey)
                                prop.text "Save"
                                prop.onClick (fun _ -> save ())
                            ]
                        ]
                    ]
                ]
            ]
            if isMarkdown && not showRendered then
                Html.div [
                    prop.className "swt:px-3 swt:py-1 swt:border-b swt:border-base-300"
                    prop.children [
                        MarkdownToolbar.MarkdownToolbar(
                            surface,
                            disabled = isReadOnly,
                            ?disabledReason = readOnlyText,
                            ?message = markdownMessage,
                            setMessage = setMarkdownMessage,
                            isOntologyReferenceOpen = ontologyReferenceOpen,
                            setOntologyReferenceOpen = setOntologyReferenceOpen
                        )
                    ]
                ]
            match readOnlyText with
            | Some text ->
                Html.div [
                    prop.role "status"
                    prop.className "swt:alert swt:alert-info swt:rounded-none"
                    prop.text text
                ]
            | None -> Html.none
            match notice with
            | Some current ->
                Html.div [
                    prop.role "alert"
                    prop.className "swt:alert swt:alert-warning swt:rounded-none swt:flex swt:flex-wrap swt:gap-2"
                    prop.children [
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
                ]
            | None -> Html.none
            match linkError with
            | Some message ->
                Html.div [
                    prop.role "alert"
                    prop.className "swt:alert swt:alert-warning swt:rounded-none swt:flex swt:flex-wrap swt:gap-2"
                    prop.children [
                        Html.span [ prop.text message ]
                        Html.button [
                            prop.type'.button
                            prop.className "swt:btn swt:btn-sm"
                            prop.text "Dismiss"
                            prop.onClick (fun _ -> setLinkError None)
                        ]
                    ]
                ]
            | None -> Html.none
            if editorMounted then
                Html.div [
                    prop.className [
                        "swt:flex-1 swt:min-h-0"
                        if showRendered then
                            "swt:hidden"
                    ]
                    prop.children [
                        TextEditor.TextEditor(
                            TextDocumentHelper.documentUri data.Path,
                            // The last saved text, so a remount after a switch keeps a save that happened in between.
                            lastSavedTextRef.current,
                            fileName = data.Path,
                            readOnly = isReadOnly,
                            ?schemaValidator = TextDocumentSchemas.tryFindValidator data.Path,
                            ?extraLint = linkLint,
                            onSave = onSave,
                            onDirtyChange = onDirtyChange,
                            ?keyBindings = (if isMarkdown then Some markdownKeyBindings else None),
                            onChange = (fun () -> setMarkdownMessage None),
                            handleRef = handleRef
                        )
                    ]
                ]
            renderedElement
        ]
    ]
