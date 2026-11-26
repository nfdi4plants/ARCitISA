namespace Arcitisa.Components.Composite.TextEditor

open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open Feliz

open Arcitisa.Components.Composite.TextEditor.JsBindings.CodeMirror
open Arcitisa.Components.Composite.TextEditor.Types
open Arcitisa.Components.Composite.TextEditor.LanguageDetection
open Arcitisa.Components.Composite.TextEditor.Theme
open Arcitisa.Components.Util

module private CodeMirrorEditorHelpers =

    let reconfigure (view: EditorView) (compartment: Compartment) (extension: Extension) =
        view.dispatch (TransactionSpec(effects = [| compartment.reconfigure extension |]))

    let readOnlyExtension (readOnly: bool) =
        if readOnly then
            EditorStateApi.readOnly.``of`` true
        else
            noExtension

    /// Prose wraps lines and gets the browser's spellcheck, except on the syntax nodes that the prose rules of the
    /// language name. Code keeps long lines and no spellcheck.
    let proseExtension (prose: bool) (rules: ProseRules option) =
        if prose then
            let skipped =
                match rules with
                | Some rules -> ProseSpellcheck.extension rules
                | None -> noExtension

            extensions [|
                EditorViewApi.lineWrapping
                EditorViewApi.contentAttributes.``of`` (createObj [ "spellcheck" ==> "true"; "autocorrect" ==> "on" ])
                skipped
            |]
        else
            EditorViewApi.contentAttributes.``of`` (createObj [ "spellcheck" ==> "false" ])

    let ariaLabelExtension (label: string) =
        EditorViewApi.contentAttributes.``of`` (createObj [ "aria-label" ==> label ])

    let placeholderExtension (text: string option) =
        match text with
        | Some text when text <> "" -> placeholder text
        | _ -> noExtension

    let languageDescription (id: string) =
        languages |> Array.tryFind (fun d -> languageId d = id)

    let editStateOf (state: EditorState) : TextEditState =
        let main = state.selection.main

        {|
            text = state.doc.toString ()
            selection = {|
                anchor = main.anchor
                head = main.head
            |}
        |}

    /// One transaction is one undo step. Isolating it keeps the history from joining it with typing just
    /// before or after.
    let applyEditTo (view: EditorView) (edit: TextEdit) : bool =
        if view.state.readOnly then
            false
        elif
            edit.changes.Length = 0
            && edit.selection.anchor = view.state.selection.main.anchor
            && edit.selection.head = view.state.selection.main.head
        then
            // A refused key returns such an edit. Dispatching it would split the undo group of the typing around it.
            true
        else
            view.dispatch (
                TransactionSpec(
                    changes = box edit.changes,
                    selection = box edit.selection,
                    scrollIntoView = true,
                    userEvent = "input.format",
                    annotations = [| isolateHistory.``of`` "full" |]
                )
            )

            true

    /// Prec.high puts these keys ahead of basicSetup's defaults, so for example Mod-i no longer selects
    /// the parent syntax node.
    let keyBindingsExtension (bindings: TextEditorKeyBinding[] option) : Extension =
        match bindings with
        | None -> noExtension
        | Some b when b.Length = 0 -> noExtension
        | Some b ->
            Prec.high (
                keymap.``of`` (
                    b
                    |> Array.map (fun binding -> {|
                        key = binding.key
                        run =
                            fun (view: EditorView) ->
                                if view.state.readOnly then
                                    false
                                else
                                    match binding.run (editStateOf view.state) with
                                    | Some edit -> applyEditTo view edit
                                    | None -> false
                        preventDefault = false
                    |})
                )
            )

    let completionsExtension (completions: TextCompletionSource option) : Extension =
        match completions with
        | Some source -> TextCompletion.extension source
        | None -> noExtension

    /// The props of one render, as the editor reads them after that render.
    type EditorProps = {
        ReadOnly: bool
        Prose: bool
        /// The prose rules of the language, None for a code language.
        ProseRules: ProseRules option
        Lint: (string -> JS.Promise<LintDiagnostic[]>) option
        KeyBindings: TextEditorKeyBinding[] option
        Completions: TextCompletionSource option
        AriaLabel: string
        Placeholder: string option
        OnSave: (TextEditorSnapshot -> JS.Promise<bool>) option
        OnDirtyChange: (bool -> unit) option
        OnChange: (unit -> unit) option
        OnHistoryChange: (TextEditorHistory -> unit) option
        OnDiagnostics: (TextEditorDiagnostic[] -> unit) option
    }

    /// Owns the CodeMirror view of one editor and the state that outlives a render. The view's callbacks and the
    /// handle read every prop through latest, which the component sets on each render, so none of them keeps the
    /// props of an earlier render. A prop that lives in the editor state gets a compartment, and Sync reconfigures
    /// it when the prop changes.
    type EditorController(latest: IRefValue<EditorProps>) =
        let languageSlot = CompartmentApi.Create()
        let readOnlySlot = CompartmentApi.Create()
        let proseSlot = CompartmentApi.Create()
        let lintSlot = CompartmentApi.Create()
        let keyBindingsSlot = CompartmentApi.Create()
        let completionsSlot = CompartmentApi.Create()
        let ariaLabelSlot = CompartmentApi.Create()
        let placeholderSlot = CompartmentApi.Create()

        let mutable view: EditorView option = None
        let mutable savedDoc: Text option = None
        let mutable dirty = false
        let mutable runningSave: (int * JS.Promise<bool>) option = None
        let mutable generation = 0
        let mutable lineSeparator = "\n"
        let mutable language = noExtension

        let mutable history: TextEditorHistory = {| canUndo = false; canRedo = false |}

        /// The props that the compartments of the current state were built from.
        let mutable applied = latest.current

        let report (diagnostics: TextEditorDiagnostic[]) =
            latest.current.OnDiagnostics |> Option.iter (fun f -> f diagnostics)

        let lintExtension (lint: (string -> JS.Promise<LintDiagnostic[]>) option) =
            match lint with
            | None -> noExtension
            | Some lint ->
                extensions [|
                    linter (
                        (fun view -> promise {
                            let doc = view.state.doc
                            // Lint offsets must match document positions, which count a line break as one
                            // character, so lint reads doc.toString(), which joins lines with "\n".
                            let! found = lint (doc.toString ())

                            // A newer change makes the result stale, and the editor then lints again.
                            if obj.ReferenceEquals(view.state.doc, doc) then
                                found
                                |> Array.map (fun d ->
                                    let line = doc.lineAt d.from

                                    {|
                                        from = d.from
                                        ``to`` = d.``to``
                                        line = line.number
                                        column = d.from - line.from + 1
                                        severity = d.severity
                                        message = d.message
                                        source = d.source
                                    |}
                                )
                                |> report

                            return found
                        }),
                        {| delay = 300 |}
                    )
                    lintGutter ()
                |]

        let evaluateDirty () =
            match view, savedDoc with
            | Some view, Some saved ->
                let isDirty = not (view.state.doc.eq saved)

                if isDirty <> dirty then
                    dirty <- isDirty
                    latest.current.OnDirtyChange |> Option.iter (fun f -> f isDirty)
            | _ -> ()

        // Runs after every update, so it reports only a change.
        let reportHistory (state: EditorState) =
            let next: TextEditorHistory = {|
                canUndo = undoDepth state > 0
                canRedo = redoDepth state > 0
            |}

            if next <> history then
                history <- next
                latest.current.OnHistoryChange |> Option.iter (fun f -> f next)

        let textOf (state: EditorState) =
            state.doc.sliceString (0, state.doc.length, lineSeparator)

        let save () : JS.Promise<bool> =
            match latest.current.OnSave, view with
            | Some onSave, Some view when not latest.current.ReadOnly ->
                let started = generation

                match runningSave with
                | Some(g, running) when g = started -> running
                | _ ->
                    let state = view.state
                    let doc = state.doc

                    let running = promise {
                        let! ok = promise {
                            try
                                // The saved text joins lines with the separator the file had when it was opened,
                                // so a CRLF file stays CRLF and pasted lines follow it.
                                return! onSave {| text = textOf state |}
                            with _ ->
                                return false
                        }

                        match runningSave with
                        | Some(g, _) when g = started -> runningSave <- None
                        | _ -> ()

                        // Another documentUri or replaceContent may have installed a newer document state by now.
                        // Its saved state stays.
                        if ok && generation = started then
                            savedDoc <- Some doc
                            evaluateDirty ()

                        return ok
                    }

                    runningSave <- Some(started, running)
                    running
            | _ -> promise { return false }

        let createState (text: string) : EditorState =
            // Every new document state starts a new generation. A save that finishes after a newer state
            // replaced its own leaves the saved state alone.
            generation <- generation + 1
            lineSeparator <- detectLineSeparator text
            let props = latest.current
            applied <- props

            EditorStateApi.create {|
                doc = text
                extensions =
                    extensions [|
                        Prec.highest (
                            keymap.``of`` [|
                                {|
                                    key = Shortcuts.saveKey
                                    run =
                                        fun _ ->
                                            save () |> ignore
                                            true
                                    preventDefault = true
                                |}
                            |]
                        )
                        basicSetup
                        keymap.``of`` [| unbox<KeyBinding> indentWithTab |]
                        daisyTheme
                        ariaLabelSlot.``of`` (ariaLabelExtension props.AriaLabel)
                        placeholderSlot.``of`` (placeholderExtension props.Placeholder)
                        EditorViewApi.updateListener.``of`` (fun update ->
                            if update.docChanged then
                                evaluateDirty ()
                                latest.current.OnChange |> Option.iter (fun f -> f ())

                            reportHistory update.state
                        )
                        languageSlot.``of`` language
                        readOnlySlot.``of`` (readOnlyExtension props.ReadOnly)
                        proseSlot.``of`` (proseExtension props.Prose props.ProseRules)
                        lintSlot.``of`` (lintExtension props.Lint)
                        keyBindingsSlot.``of`` (keyBindingsExtension props.KeyBindings)
                        completionsSlot.``of`` (completionsExtension props.Completions)
                    |]
            |}

        /// Stays the same object for the life of the editor, so a host may keep it.
        member val Handle: TextEditorHandle =
            { new TextEditorHandle with
                member _.isDirty() = dirty
                member _.save() = save ()

                member _.getText() =
                    match view with
                    | Some view -> textOf view.state
                    | None -> ""

                member _.getEditState() =
                    match view with
                    | Some view -> editStateOf view.state
                    | None -> {|
                        text = ""
                        selection = {| anchor = 0; head = 0 |}
                      |}

                member _.applyEdit(edit) =
                    match view with
                    | Some view -> applyEditTo view edit
                    | None -> false

                member _.undo() =
                    match view with
                    | Some view -> undo view
                    | None -> false

                member _.redo() =
                    match view with
                    | Some view -> redo view
                    | None -> false

                member _.replaceContent(text) =
                    match view with
                    | Some view ->
                        let state = createState text
                        view.setState state
                        savedDoc <- Some state.doc
                        evaluateDirty ()
                        // setState runs no update listener, and the new document has no history.
                        reportHistory state
                    | None -> ()

                member _.focus() =
                    view |> Option.iter (fun v -> v.focus ())
            }

        /// Creates the view in host with a new document state of text.
        member _.Mount(host: HTMLElement, text: string) =
            let state = createState text
            view <- Some(EditorViewApi.Create {| state = state; parent = host |})
            savedDoc <- Some state.doc
            evaluateDirty ()
            reportHistory state

        member _.Unmount() =
            view |> Option.iter (fun v -> v.destroy ())
            view <- None

        /// The loaded language also goes into the next document state.
        member _.SetLanguage(extension: Extension) =
            language <- extension
            view |> Option.iter (fun v -> reconfigure v languageSlot extension)

        /// Reconfigures the compartments whose props changed since the current state was built or last synced.
        member _.Sync() =
            match view with
            | Some v ->
                let props = latest.current

                if props.ReadOnly <> applied.ReadOnly then
                    reconfigure v readOnlySlot (readOnlyExtension props.ReadOnly)

                if props.Prose <> applied.Prose || props.ProseRules <> applied.ProseRules then
                    reconfigure v proseSlot (proseExtension props.Prose props.ProseRules)

                if not (obj.ReferenceEquals(props.Lint, applied.Lint)) then
                    reconfigure v lintSlot (lintExtension props.Lint)

                if not (obj.ReferenceEquals(props.KeyBindings, applied.KeyBindings)) then
                    reconfigure v keyBindingsSlot (keyBindingsExtension props.KeyBindings)

                if not (obj.ReferenceEquals(props.Completions, applied.Completions)) then
                    reconfigure v completionsSlot (completionsExtension props.Completions)

                if props.AriaLabel <> applied.AriaLabel then
                    reconfigure v ariaLabelSlot (ariaLabelExtension props.AriaLabel)

                if props.Placeholder <> applied.Placeholder then
                    reconfigure v placeholderSlot (placeholderExtension props.Placeholder)

                applied <- props
            | None -> ()

        member _.Report(diagnostics: TextEditorDiagnostic[]) = report diagnostics

open CodeMirrorEditorHelpers

[<Erase; Mangle(false)>]
type CodeMirrorEditor =

    /// A CodeMirror 6 editor on one document. A new documentUri starts a new document from
    /// initialValue. Other changes of initialValue are ignored. language is an id from
    /// LanguageDetection. The editor loads that language on demand. prose wraps lines and turns the browser's
    /// spellcheck on, except on the syntax nodes that the prose rules of the language name.
    /// lint runs 300 ms after the last change and may take longer. Its result reaches onDiagnostics only while the
    /// document it read is still the current one. Pass the same function instance across renders,
    /// a new instance lints again.
    /// keyBindings take precedence over the default keys. Pass the same array instance across renders,
    /// a new instance reconfigures the keys.
    /// completions adds a completion source next to those of the language. Pass the same function instance across
    /// renders, a new instance reconfigures the completions.
    /// onHistoryChange reports when undo or redo becomes possible or impossible, also when a new document starts
    /// the history over. placeholder shows while the document is empty.
    [<ReactComponent>]
    static member CodeMirrorEditor
        (
            documentUri: string,
            initialValue: string,
            language: string,
            ?readOnly: bool,
            ?prose: bool,
            ?lint: string -> JS.Promise<LintDiagnostic[]>,
            ?keyBindings: TextEditorKeyBinding[],
            ?completions: TextCompletionSource,
            ?onSave: TextEditorSnapshot -> JS.Promise<bool>,
            ?onDirtyChange: bool -> unit,
            ?onChange: unit -> unit,
            ?onHistoryChange: TextEditorHistory -> unit,
            ?onDiagnostics: TextEditorDiagnostic[] -> unit,
            ?handleRef: IRefValue<TextEditorHandle option>,
            ?className: string,
            ?ariaLabel: string,
            ?placeholder: string
        ) =
        let props: EditorProps = {
            ReadOnly = defaultArg readOnly false
            Prose = defaultArg prose false
            ProseRules = proseOf registry language
            Lint = lint
            KeyBindings = keyBindings
            Completions = completions
            AriaLabel = defaultArg ariaLabel "Text editor"
            Placeholder = placeholder
            OnSave = onSave
            OnDirtyChange = onDirtyChange
            OnChange = onChange
            OnHistoryChange = onHistoryChange
            OnDiagnostics = onDiagnostics
        }

        let hostRef = React.useRef<HTMLElement option> None
        // The editor outlives single renders, so it reads the props of the latest render from here.
        let latest = React.useRef props
        latest.current <- props
        let controllerRef = React.useRef<EditorController option> None

        let controller =
            match controllerRef.current with
            | Some c -> c
            | None ->
                let c = EditorController latest
                controllerRef.current <- Some c
                c

        React.useEffect (
            (fun () ->
                match hostRef.current with
                | Some host ->
                    controller.Mount(host, initialValue)
                    handleRef |> Option.iter (fun r -> r.current <- Some controller.Handle)

                    { new System.IDisposable with
                        member _.Dispose() =
                            controller.Unmount()
                            handleRef |> Option.iter (fun r -> r.current <- None)
                    }
                | None ->
                    { new System.IDisposable with
                        member _.Dispose() = ()
                    }
            ),
            [| box documentUri |]
        )

        React.useEffect (
            (fun () ->
                let cancelled = ref false

                match languageDescription language with
                | Some description ->
                    promise {
                        try
                            let! loaded = description.load ()

                            if not cancelled.Value then
                                controller.SetLanguage loaded
                        with _ ->
                            ()
                    }
                    |> ignore
                | None -> controller.SetLanguage noExtension

                { new System.IDisposable with
                    member _.Dispose() = cancelled.Value <- true
                }
            ),
            [| box language; box documentUri |]
        )

        // A new document state takes the latest props, so this only reconfigures on a later change.
        React.useEffect (
            (fun () -> controller.Sync()),
            [|
                box props.ReadOnly
                box props.Prose
                box props.ProseRules
                box lint
                box keyBindings
                box completions
                box props.AriaLabel
                box props.Placeholder
            |]
        )

        React.useEffect (
            (fun () ->
                if lint.IsNone then
                    controller.Report [||]
            ),
            [| box lint |]
        )

        Html.div [
            prop.className (defaultArg className "swt:size-full swt:min-h-0 swt:overflow-hidden")
            prop.ref hostRef
        ]
