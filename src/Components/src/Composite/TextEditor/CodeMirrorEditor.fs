namespace Swate.Components.Composite.TextEditor

open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open Feliz

open Swate.Components.Composite.TextEditor.JsBindings.CodeMirror
open Swate.Components.Composite.TextEditor.Types
open Swate.Components.Composite.TextEditor.LanguageDetection
open Swate.Components.Composite.TextEditor.Theme

module private CodeMirrorEditorHelpers =

    let reconfigure (view: EditorView) (compartment: Compartment) (extension: Extension) =
        view.dispatch (TransactionSpec(effects = [| compartment.reconfigure extension |]))

    let readOnlyExtension (readOnly: bool) =
        if readOnly then
            EditorStateApi.readOnly.``of`` true
        else
            noExtension

    /// Prose wraps lines and gets the browser's spellcheck. Code keeps long lines and no spellcheck.
    let proseExtension (prose: bool) =
        if prose then
            extensions [|
                EditorViewApi.lineWrapping
                EditorViewApi.contentAttributes.``of`` (createObj [ "spellcheck" ==> "true"; "autocorrect" ==> "on" ])
            |]
        else
            EditorViewApi.contentAttributes.``of`` (createObj [ "spellcheck" ==> "false" ])

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

open CodeMirrorEditorHelpers

[<Erase; Mangle(false)>]
type CodeMirrorEditor =

    /// A CodeMirror 6 editor on one document. A new documentUri starts a new document from
    /// initialValue. Other changes of initialValue are ignored. language is an id from
    /// LanguageDetection. The editor loads that language on demand.
    /// lint runs 300 ms after the last change. Pass the same function instance across renders,
    /// a new instance lints again.
    /// keyBindings take precedence over the default keys. Pass the same array instance across renders,
    /// a new instance reconfigures the keys.
    [<ReactComponent>]
    static member CodeMirrorEditor
        (
            documentUri: string,
            initialValue: string,
            language: string,
            ?readOnly: bool,
            ?prose: bool,
            ?lint: string -> LintDiagnostic[],
            ?keyBindings: TextEditorKeyBinding[],
            ?onSave: TextEditorSnapshot -> JS.Promise<bool>,
            ?onDirtyChange: bool -> unit,
            ?onChange: unit -> unit,
            ?onDiagnostics: TextEditorDiagnostic[] -> unit,
            ?handleRef: IRefValue<TextEditorHandle option>,
            ?className: string,
            ?ariaLabel: string
        ) =
        let readOnly = defaultArg readOnly false
        let prose = defaultArg prose false

        let hostRef = React.useRef<HTMLElement option> None
        let viewRef = React.useRef<EditorView option> None
        let savedDocRef = React.useRef<Text option> None
        let dirtyRef = React.useRef false
        let runningSaveRef = React.useRef<(int * JS.Promise<bool>) option> None
        let generationRef = React.useRef 0
        let lineSeparatorRef = React.useRef "\n"
        let languageRef = React.useRef noExtension
        let compartmentsRef = React.useRef<Compartment[] option> None

        let compartments =
            match compartmentsRef.current with
            | Some c -> c
            | None ->
                let c = Array.init 5 (fun _ -> CompartmentApi.Create())
                compartmentsRef.current <- Some c
                c

        let languageSlot, readOnlySlot, proseSlot, lintSlot, keyBindingsSlot =
            compartments.[0], compartments.[1], compartments.[2], compartments.[3], compartments.[4]

        // The editor outlives single renders, so its callbacks read the latest props from refs.
        let onSaveRef = React.useRef onSave
        let onDirtyChangeRef = React.useRef onDirtyChange
        let onChangeRef = React.useRef onChange
        let onDiagnosticsRef = React.useRef onDiagnostics
        let readOnlyRef = React.useRef readOnly
        let proseRef = React.useRef prose
        let lintRef = React.useRef lint
        let keyBindingsRef = React.useRef keyBindings
        let appliedReadOnlyRef = React.useRef readOnly
        let appliedProseRef = React.useRef prose
        let appliedLintRef = React.useRef lint
        let appliedKeyBindingsRef = React.useRef keyBindings
        onSaveRef.current <- onSave
        onDirtyChangeRef.current <- onDirtyChange
        onChangeRef.current <- onChange
        onDiagnosticsRef.current <- onDiagnostics
        readOnlyRef.current <- readOnly
        proseRef.current <- prose
        lintRef.current <- lint
        keyBindingsRef.current <- keyBindings

        let report (diagnostics: TextEditorDiagnostic[]) =
            onDiagnosticsRef.current |> Option.iter (fun f -> f diagnostics)

        let lintExtension (lint: (string -> LintDiagnostic[]) option) =
            match lint with
            | None -> noExtension
            | Some lint ->
                extensions [|
                    linter (
                        (fun view -> promise {
                            let doc = view.state.doc
                            // Lint offsets must match document positions, which count a line break as one
                            // character, so lint reads doc.toString(), which joins lines with "\n".
                            let found = lint (doc.toString ())

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
            match viewRef.current, savedDocRef.current with
            | Some view, Some saved ->
                let dirty = not (view.state.doc.eq saved)

                if dirty <> dirtyRef.current then
                    dirtyRef.current <- dirty
                    onDirtyChangeRef.current |> Option.iter (fun f -> f dirty)
            | _ -> ()

        let textOf (state: EditorState) =
            state.doc.sliceString (0, state.doc.length, lineSeparatorRef.current)

        let save () : JS.Promise<bool> =
            match onSaveRef.current, viewRef.current with
            | Some onSave, Some view when not readOnlyRef.current ->
                let generation = generationRef.current

                match runningSaveRef.current with
                | Some(g, running) when g = generation -> running
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

                        match runningSaveRef.current with
                        | Some(g, _) when g = generation -> runningSaveRef.current <- None
                        | _ -> ()

                        // Another documentUri or replaceContent may have installed a newer document state by now.
                        // Its saved state stays.
                        if ok && generationRef.current = generation then
                            savedDocRef.current <- Some doc
                            evaluateDirty ()

                        return ok
                    }

                    runningSaveRef.current <- Some(generation, running)
                    running
            | _ -> promise { return false }

        let createState (text: string) : EditorState =
            // Every new document state starts a new generation. A save that finishes after a newer state
            // replaced its own leaves the saved state alone.
            generationRef.current <- generationRef.current + 1
            lineSeparatorRef.current <- detectLineSeparator text
            appliedReadOnlyRef.current <- readOnlyRef.current
            appliedProseRef.current <- proseRef.current
            appliedLintRef.current <- lintRef.current
            appliedKeyBindingsRef.current <- keyBindingsRef.current

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
                        EditorViewApi.contentAttributes.``of`` (
                            createObj [ "aria-label" ==> defaultArg ariaLabel "Text editor" ]
                        )
                        EditorViewApi.updateListener.``of`` (fun update ->
                            if update.docChanged then
                                evaluateDirty ()
                                onChangeRef.current |> Option.iter (fun f -> f ())
                        )
                        languageSlot.``of`` languageRef.current
                        readOnlySlot.``of`` (readOnlyExtension readOnlyRef.current)
                        proseSlot.``of`` (proseExtension proseRef.current)
                        lintSlot.``of`` (lintExtension lintRef.current)
                        keyBindingsSlot.``of`` (keyBindingsExtension keyBindingsRef.current)
                    |]
            |}

        let handle =
            { new TextEditorHandle with
                member _.isDirty() = dirtyRef.current
                member _.save() = save ()

                member _.getText() =
                    match viewRef.current with
                    | Some view -> textOf view.state
                    | None -> ""

                member _.getEditState() =
                    match viewRef.current with
                    | Some view -> editStateOf view.state
                    | None -> {|
                        text = ""
                        selection = {| anchor = 0; head = 0 |}
                      |}

                member _.applyEdit(edit) =
                    match viewRef.current with
                    | Some view -> applyEditTo view edit
                    | None -> false

                member _.replaceContent(text) =
                    match viewRef.current with
                    | Some view ->
                        let state = createState text
                        view.setState state
                        savedDocRef.current <- Some state.doc
                        evaluateDirty ()
                    | None -> ()

                member _.focus() =
                    viewRef.current |> Option.iter (fun v -> v.focus ())
            }

        React.useEffect (
            (fun () ->
                match hostRef.current with
                | Some host ->
                    let state = createState initialValue
                    let view = EditorViewApi.Create {| state = state; parent = host |}
                    viewRef.current <- Some view
                    savedDocRef.current <- Some state.doc
                    evaluateDirty ()
                    handleRef |> Option.iter (fun r -> r.current <- Some handle)

                    { new System.IDisposable with
                        member _.Dispose() =
                            view.destroy ()
                            viewRef.current <- None
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
                                languageRef.current <- loaded
                                viewRef.current |> Option.iter (fun v -> reconfigure v languageSlot loaded)
                        with _ ->
                            ()
                    }
                    |> ignore
                | None ->
                    languageRef.current <- noExtension
                    viewRef.current |> Option.iter (fun v -> reconfigure v languageSlot noExtension)

                { new System.IDisposable with
                    member _.Dispose() = cancelled.Value <- true
                }
            ),
            [| box language; box documentUri |]
        )

        // createState already applies the current values, so these effects only reconfigure on a later change.
        React.useEffect (
            (fun () ->
                match viewRef.current with
                | Some v when readOnly <> appliedReadOnlyRef.current ->
                    reconfigure v readOnlySlot (readOnlyExtension readOnly)
                    appliedReadOnlyRef.current <- readOnly
                | _ -> ()
            ),
            [| box readOnly |]
        )

        React.useEffect (
            (fun () ->
                match viewRef.current with
                | Some v when prose <> appliedProseRef.current ->
                    reconfigure v proseSlot (proseExtension prose)
                    appliedProseRef.current <- prose
                | _ -> ()
            ),
            [| box prose |]
        )

        React.useEffect (
            (fun () ->
                match viewRef.current with
                | Some v when not (obj.ReferenceEquals(lint, appliedLintRef.current)) ->
                    reconfigure v lintSlot (lintExtension lint)
                    appliedLintRef.current <- lint
                | _ -> ()

                if lint.IsNone then
                    report [||]
            ),
            [| box lint |]
        )

        React.useEffect (
            (fun () ->
                match viewRef.current with
                | Some v when not (obj.ReferenceEquals(keyBindings, appliedKeyBindingsRef.current)) ->
                    reconfigure v keyBindingsSlot (keyBindingsExtension keyBindings)
                    appliedKeyBindingsRef.current <- keyBindings
                | _ -> ()
            ),
            [| box keyBindings |]
        )

        Html.div [
            prop.className (defaultArg className "swt:size-full swt:min-h-0 swt:overflow-hidden")
            prop.ref hostRef
        ]
