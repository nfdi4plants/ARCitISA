namespace Arcitisa.Components.Composite.MarkdownText

open Browser.Types
open Fable.Core
open Feliz

open Arcitisa.Components
open Arcitisa.Components.Composite.TextEditor
open Arcitisa.Components.Composite.TextEditor.Types
open Arcitisa.Components.Composite.MarkdownText.Types
open Arcitisa.Components.Composite.MarkdownText.Editing
open Arcitisa.Components.Composite.MarkdownText.Editing.Types
open Arcitisa.Components.Composite.MarkdownText.Links.Types
open Arcitisa.Components.Primitive.LayoutComponents

module private MarkdownTextEditorHelpers =

    let noHistory: TextEditorHistory = {| canUndo = false; canRedo = false |}

    let modes = [
        PreviewMode.Edit, "Edit"
        PreviewMode.Live, "Live"
        PreviewMode.Preview, "Preview"
    ]

open MarkdownTextEditorHelpers

[<Erase; Mangle(false)>]
type MarkdownTextEditor =

    /// Switches between the editor, the editor next to the preview, and the preview.
    [<ReactComponent>]
    static member private ModeToggle(activeMode: PreviewMode, disabled: bool, setMode: PreviewMode -> unit) =
        Html.div [
            prop.role "group"
            prop.ariaLabel "Show the text as"
            prop.className "swt:join"
            prop.children [
                for mode, label in modes do
                    let active = activeMode = mode

                    Html.button [
                        prop.key label
                        prop.type'.button
                        prop.className [
                            "swt:btn swt:btn-sm swt:join-item"
                            if active then "swt:btn-primary" else "swt:btn-ghost"
                        ]
                        prop.ariaPressed active
                        prop.disabled disabled
                        prop.text label
                        prop.onClick (fun _ -> setMode mode)
                    ]
            ]
        ]

    /// A Markdown field for forms: the Markdown toolbar over a TextEditor, with the modes Edit, Live (the editor next
    /// to the preview) and Preview. setValue hears the text 300 ms after the last change, once validator accepts it.
    /// A value from the host that is not the text this field last gave replaces the text and starts the undo history
    /// over. height is the height of the editor and of the preview in pixels. Links and images in the preview resolve
    /// through linkContext.
    [<ReactComponent(true)>]
    static member MarkdownTextEditor
        (
            value: string,
            setValue: string -> unit,
            height: int,
            ?label: string,
            ?placeholder: string,
            ?disabled: bool,
            ?classes: string,
            ?isJoin: bool,
            ?rmv: MouseEvent -> unit,
            ?validator: string -> Result<unit, string>,
            ?mode: PreviewMode,
            ?previewClassName: string,
            ?linkContext: IMarkdownLinkContext
        ) =
        let disabled = defaultArg disabled false
        let isJoin = defaultArg isJoin false
        let previewClassName = defaultArg previewClassName "swt:p-4 swt:min-h-full"

        // One document per field, so several fields on a page keep their own text and undo history.
        let fieldId = React.useId ()
        let documentUri = $"inmemory://markdown-input/{fieldId}.md"

        let handleRef = React.useRef<TextEditorHandle option> None

        let surface =
            React.useMemo ((fun () -> MarkdownSurface.ofTextEditor handleRef), [||])

        let text, setText = React.useState value
        let debouncedText = React.useDebounce (text, 300)

        let activeMode, setActiveMode =
            React.useState (defaultArg mode MarkdownOptions.defaults.Mode)

        let validationError, setValidationError = React.useState (None: string option)
        let message, setMessage = React.useState (None: string option)
        let ontologyReferenceOpen, setOntologyReferenceOpen = React.useState false
        let history, setHistory = React.useState noHistory

        // The text this field last gave to the host. value comes back with it, and that echo must not replace the
        // editor text, which may have changed again since.
        let emitted = React.useRef value
        let edited = React.useRef false

        let keyBindings =
            React.useMemo (
                (fun () -> MarkdownCommands.keyBindingsWith setMessage (fun () -> setOntologyReferenceOpen true)),
                [||]
            )

        React.useEffect (
            (fun () ->
                if edited.current then
                    edited.current <- false

                    match validator |> Option.map (fun validate -> validate debouncedText) with
                    | Some(Error reason) -> setValidationError (Some reason)
                    | _ ->
                        setValidationError None
                        emitted.current <- debouncedText
                        setValue debouncedText
            ),
            [| box debouncedText |]
        )

        React.useEffect (
            (fun () ->
                if value <> emitted.current then
                    emitted.current <- value
                    setText value

                    handleRef.current
                    |> Option.iter (fun handle ->
                        if handle.getText () <> value then
                            handle.replaceContent value
                    )
            ),
            [| box value |]
        )

        React.useEffect (
            (fun () ->
                match mode with
                | Some next -> setActiveMode next
                | None -> ()
            ),
            [| box mode |]
        )

        let onChange () =
            setMessage None

            handleRef.current
            |> Option.iter (fun handle ->
                edited.current <- true
                setText (handle.getText ())
            )

        let showEditor = activeMode <> PreviewMode.Preview
        let showPreview = activeMode <> PreviewMode.Edit

        let toolbarDisabledReason =
            if not disabled && not showEditor then
                Some "Switch to Edit or Live to change the text."
            else
                None

        Html.div [
            prop.className (
                if isJoin then
                    "swt:grow swt:join-item"
                else
                    "swt:fieldset swt:grow"
            )
            prop.children [
                if label.IsSome && not isJoin then
                    LayoutComponents.FieldTitle label.Value

                Html.div [
                    prop.className "swt:flex swt:gap-2 swt:items-start swt:w-full"
                    prop.children [
                        Html.div [
                            prop.className [
                                "swt:w-full swt:min-w-0 swt:overflow-hidden swt:rounded-field swt:border swt:border-base-300 swt:bg-base-100"
                                if validationError.IsSome then
                                    "swt:border-error"
                                if disabled then
                                    "swt:opacity-70"
                                if classes.IsSome then
                                    classes.Value
                            ]
                            prop.children [
                                MarkdownToolbar.MarkdownToolbar(
                                    surface,
                                    history,
                                    disabled = (disabled || not showEditor),
                                    ?disabledReason = toolbarDisabledReason,
                                    ?message = message,
                                    setMessage = setMessage,
                                    isOntologyReferenceOpen = ontologyReferenceOpen,
                                    setOntologyReferenceOpen = setOntologyReferenceOpen,
                                    trailing = MarkdownTextEditor.ModeToggle(activeMode, disabled, setActiveMode)
                                )
                                Html.div [
                                    prop.className [
                                        "swt:grid swt:grid-cols-1"
                                        if activeMode = PreviewMode.Live then
                                            "swt:lg:grid-cols-2"
                                    ]
                                    prop.children [
                                        // The editor stays mounted while the preview shows, so its undo history
                                        // and cursor survive the switch.
                                        Html.div [
                                            prop.key "editor"
                                            prop.className [
                                                "swt:min-w-0"
                                                if activeMode = PreviewMode.Live then
                                                    "swt:border-b swt:border-base-300 swt:lg:border-b-0 swt:lg:border-r"
                                                if not showEditor then
                                                    "swt:hidden"
                                            ]
                                            prop.style [ style.height height ]
                                            prop.children [
                                                TextEditor.TextEditor(
                                                    documentUri,
                                                    text,
                                                    fileName = defaultArg label "Markdown text",
                                                    language = "markdown",
                                                    readOnly = disabled,
                                                    keyBindings = keyBindings,
                                                    onChange = onChange,
                                                    onHistoryChange = setHistory,
                                                    handleRef = handleRef,
                                                    ?placeholder = placeholder
                                                )
                                            ]
                                        ]
                                        if showPreview then
                                            Html.div [
                                                prop.key "preview"
                                                prop.className "swt:min-w-0 swt:overflow-auto swt:bg-base-100"
                                                prop.style [ style.height height ]
                                                prop.children [
                                                    MarkdownTextViewer.MarkdownTextViewer(
                                                        text,
                                                        className = previewClassName,
                                                        ?linkContext = linkContext
                                                    )
                                                ]
                                            ]
                                    ]
                                ]
                            ]
                        ]

                        if rmv.IsSome then
                            Html.button [
                                prop.className "swt:btn swt:btn-error swt:grow-0"
                                prop.text "Delete"
                                prop.onClick rmv.Value
                            ]
                    ]
                ]

                if validationError.IsSome then
                    Html.p [
                        prop.className "swt:text-error swt:text-sm swt:mt-1"
                        prop.text validationError.Value
                    ]
            ]
        ]
