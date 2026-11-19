namespace Swate.Components.Composite.MarkdownText

open System
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open Feliz

open Swate.Components
open Swate.Components.Composite.MarkdownText.Types
open Swate.Components.Composite.MarkdownText.JsBindings
open Swate.Components.Composite.MarkdownText.Links.Types
open Swate.Components.Composite.MarkdownText.Plugins
open Swate.Components.Primitive.LayoutComponents

[<RequireQualifiedAccess>]
module private MarkdownCommands =

    let private bold: ICommand = ReactMDEditor.commands?bold |> unbox<ICommand>
    let private italic: ICommand = ReactMDEditor.commands?italic |> unbox<ICommand>

    let private strikethrough: ICommand =
        ReactMDEditor.commands?strikethrough |> unbox<ICommand>

    let private link: ICommand = ReactMDEditor.commands?link |> unbox<ICommand>
    let private quote: ICommand = ReactMDEditor.commands?quote |> unbox<ICommand>
    let private code: ICommand = ReactMDEditor.commands?code |> unbox<ICommand>

    let private codeBlock: ICommand =
        ReactMDEditor.commands?codeBlock |> unbox<ICommand>

    let private unorderedListCommand: ICommand =
        ReactMDEditor.commands?unorderedListCommand |> unbox<ICommand>

    let private orderedListCommand: ICommand =
        ReactMDEditor.commands?orderedListCommand |> unbox<ICommand>

    let private checkedListCommand: ICommand =
        ReactMDEditor.commands?checkedListCommand |> unbox<ICommand>

    let defaultToolbarGroups: ICommand[][] = [|
        [| bold; italic; strikethrough |]
        [| link; quote; code; codeBlock |]
        [|
            unorderedListCommand
            orderedListCommand
            checkedListCommand
        |]
    |]

    let toolbarGroupsWithPlugins (pluginCommands: ICommand[]) =
        if pluginCommands.Length = 0 then
            defaultToolbarGroups
        else
            Array.append defaultToolbarGroups [| pluginCommands |]

    let shortcutCommands (pluginCommands: ICommand[]) =
        toolbarGroupsWithPlugins pluginCommands |> Array.concat

[<Erase; Mangle(false)>]
type TextInputWithMarkdown =

    [<ReactComponent(true)>]
    static member TextInputWithMarkdown
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
            ?plugins: MarkdownToolbarPlugin list,
            ?filePickerAdapter: MarkdownFilePickerAdapter,
            ?linkContext: IMarkdownLinkContext
        ) =
        let disabled = defaultArg disabled false
        let isJoin = defaultArg isJoin false
        let previewClassName = defaultArg previewClassName "swt:p-4 swt:h-full"
        let defaultMode = defaultArg mode MarkdownOptions.defaults.Mode

        let startedChange = React.useRef false
        let tempValue, setTempValue = React.useState value
        let activeMode, setActiveMode = React.useState defaultMode
        let debouncedValue = React.useDebounce (tempValue, 300)
        let validationError, setValidationError = React.useState (None: string option)

        let activePrompt, setActivePrompt =
            React.useState (None: MarkdownPromptPlugin option)

        let promptInput, setPromptInput = React.useState ""
        let promptError, setPromptError = React.useState (None: string option)

        let promptFiles, setPromptFiles =
            React.useStateWithUpdater ([]: MarkdownPromptFile list)

        let promptFileDropActive, setPromptFileDropActive = React.useState false

        let textareaRef = React.useElementRef ()
        let promptInputRef = React.useInputRef ()
        let promptFileInputRef = React.useInputRef ()
        let commandOrchestratorRef = React.useRef<TextAreaCommandOrchestrator option> None
        let promptSelectionRef = React.useRef (None: (int * int) option)
        let isMountedRef = React.useRef true

        React.useEffectOnce (fun _ ->
            { new System.IDisposable with
                member _.Dispose() = isMountedRef.current <- false
            }
        )

        React.useEffect (
            (fun () ->
                if startedChange.current then
                    match validator with
                    | Some validate ->
                        match validate debouncedValue with
                        | Ok() ->
                            setValidationError None
                            setValue debouncedValue
                        | Error message -> setValidationError (Some message)
                    | None ->
                        setValidationError None
                        setValue debouncedValue

                    startedChange.current <- false
            ),
            [| box debouncedValue |]
        )

        React.useEffect ((fun () -> setTempValue value), [| box value |])

        React.useEffect (
            (fun () ->
                match mode with
                | Some value -> setActiveMode value
                | None -> ()
            ),
            [| box mode |]
        )

        React.useEffect (
            (fun () ->
                if activePrompt.IsNone then
                    match promptSelectionRef.current, textareaRef.current with
                    | Some(startIndex, endIndex), Some element ->
                        let textarea = element :?> HTMLTextAreaElement
                        textarea.focus ()
                        textarea?setSelectionRange (startIndex, endIndex)
                        promptSelectionRef.current <- None
                    | _ -> ()
            ),
            [| box activePrompt; box tempValue |]
        )

        let tryGetTextarea () =
            textareaRef.current
            |> Option.map (fun element -> element :?> HTMLTextAreaElement)

        let ensureCommandOrchestrator () =
            match commandOrchestratorRef.current, tryGetTextarea () with
            | Some orchestrator, Some textarea when obj.ReferenceEquals(orchestrator.textArea, textarea) ->
                Some orchestrator
            | _, Some textarea ->
                let orchestrator = TextAreaCommandOrchestrator(textarea)
                commandOrchestratorRef.current <- Some orchestrator
                Some orchestrator
            | _ -> None

        let syncTextFromTextarea () =
            match tryGetTextarea () with
            | Some textarea when textarea.value <> tempValue ->
                setTempValue textarea.value
                startedChange.current <- true
            | _ -> ()

        let handleTextChange =
            fun (text: string) ->
                setTempValue text
                startedChange.current <- true

        let getSelectionOrEnd () =
            match tryGetTextarea () with
            | Some textarea -> textarea.selectionStart, textarea.selectionEnd
            | None -> tempValue.Length, tempValue.Length

        let appendPromptFilesAndClearError (files: MarkdownPromptFile list) =
            if isMountedRef.current then
                setPromptFiles (fun currentFiles ->
                    let combined = currentFiles @ files
                    PluginTextInputHelpers.normalizePromptFiles activePrompt combined
                )

                if promptError.IsSome then
                    setPromptError None

        let applyPickedPromptFiles (files: MarkdownPromptFile list) =
            let accepted, rejected =
                PluginTextInputHelpers.partitionFilesByAccept activePrompt files

            if not (List.isEmpty accepted) then
                appendPromptFilesAndClearError accepted

            if not (List.isEmpty rejected) && isMountedRef.current then
                setPromptError (Some(PluginTextInputHelpers.rejectedFilesMessage activePrompt rejected))

        let removePromptFileAtIndex (indexToRemove: int) =
            setPromptFiles (fun currentFiles ->
                currentFiles
                |> List.indexed
                |> List.choose (fun (index, file) -> if index = indexToRemove then None else Some file)
            )

        let triggerPromptFileSelection () =
            promise {
                match filePickerAdapter with
                | Some adapter ->
                    // Preferred substitution point for runtime-specific file pickers.
                    let! files = adapter.PickFiles()
                    applyPickedPromptFiles files
                | None ->
                    // Built-in fallback: standard browser file input dialog.
                    promptFileInputRef.current |> Option.iter (fun input -> input.click ())
            }
            |> Promise.catch (fun err ->
                if isMountedRef.current then
                    setPromptError (Some $"File selection failed: {string err}")
            )
            |> Promise.start

        let handlePromptFileChange =
            fun (files: File list) ->
                let selected = files |> List.map PluginTextInputHelpers.toPromptFile
                applyPickedPromptFiles selected

                // Reset the input value so selecting the same file triggers onChange.
                promptFileInputRef.current |> Option.iter (fun input -> input.value <- "")

        let handlePromptDrop =
            fun (e: DragEvent) ->
                e.preventDefault ()
                e.stopPropagation ()
                setPromptFileDropActive false

                // Built-in fallback: use dropped browser File objects directly.
                let files =
                    if isNull e.dataTransfer || isNull e.dataTransfer.files then
                        []
                    else
                        [
                            for i in 0 .. int e.dataTransfer.files.length - 1 do
                                let file = e.dataTransfer.files.item i

                                if not (isNull file) then
                                    yield PluginTextInputHelpers.toPromptFile file
                        ]

                if List.isEmpty files then
                    setPromptError (Some "No files were dropped.")
                else
                    applyPickedPromptFiles files

        let openPromptDialog (prompt: MarkdownPromptPlugin) =
            let startIndex, endIndex = getSelectionOrEnd ()
            promptSelectionRef.current <- Some(startIndex, endIndex)
            setPromptInput ""
            setPromptError None
            setPromptFiles (fun _ -> [])
            setPromptFileDropActive false
            setActivePrompt (Some prompt)

        let submitPromptDialog () =
            match activePrompt with
            | None -> ()
            | Some prompt ->
                let applyPromptResult (nextValue: string) ((nextSelectionStart, nextSelectionEnd): int * int) =
                    if isMountedRef.current then
                        setTempValue nextValue
                        startedChange.current <- true
                        promptSelectionRef.current <- Some(nextSelectionStart, nextSelectionEnd)
                        setActivePrompt None
                        setPromptInput ""
                        setPromptError None
                        setPromptFiles (fun _ -> [])
                        setPromptFileDropActive false

                match PluginTextInputHelpers.activePromptInputMode activePrompt with
                | MarkdownPromptInputMode.Text ->
                    match prompt.Validate promptInput with
                    | Error message -> setPromptError (Some message)
                    | Ok() ->
                        let startIndex, endIndex =
                            match promptSelectionRef.current with
                            | Some(startIndex, endIndex) -> startIndex, endIndex
                            | None -> getSelectionOrEnd ()

                        let nextValue, selection = prompt.Apply tempValue startIndex endIndex promptInput

                        applyPromptResult nextValue selection

                | MarkdownPromptInputMode.File ->
                    let selectedFiles =
                        promptFiles |> PluginTextInputHelpers.normalizePromptFiles activePrompt

                    if List.isEmpty selectedFiles then
                        setPromptError (Some "Select at least one file.")
                    else
                        match prompt.ApplyFiles with
                        | None -> setPromptError (Some "This plugin does not support file input.")
                        | Some applyFiles ->
                            promise {
                                let mutable resolvedFiles: (MarkdownPromptFile * string) list = []

                                for file in selectedFiles do
                                    let! resolvedPath =
                                        PluginTextInputHelpers.resolvePromptFilePath filePickerAdapter file

                                    resolvedFiles <- (file, resolvedPath) :: resolvedFiles

                                return List.rev resolvedFiles
                            }
                            |> Promise.map (fun resolvedFiles ->
                                if isMountedRef.current then
                                    let startIndex, endIndex =
                                        match promptSelectionRef.current with
                                        | Some(startIndex, endIndex) -> startIndex, endIndex
                                        | None -> getSelectionOrEnd ()

                                    let nextValue, selection = applyFiles tempValue startIndex endIndex resolvedFiles

                                    applyPromptResult nextValue selection
                            )
                            |> Promise.catch (fun err ->
                                if isMountedRef.current then
                                    setPromptError (Some $"Could not resolve file paths: {string err}")
                            )
                            |> Promise.start

        let closePromptDialog (_: bool) =
            setActivePrompt None
            setPromptError None
            setPromptInput ""
            setPromptFiles (fun _ -> [])
            setPromptFileDropActive false

        let activePlugins = PluginRegistry.activePlugins plugins
        let pluginCommands = PluginRegistry.activeCommands plugins
        let toolbarGroups = MarkdownCommands.toolbarGroupsWithPlugins pluginCommands
        let shortcutCommands = MarkdownCommands.shortcutCommands pluginCommands

        let commandAriaLabel (command: ICommand) =
            let fallback =
                if String.IsNullOrWhiteSpace command.name then
                    "Markdown command"
                else
                    command.name

            if isNullOrUndefined command.buttonProps then
                fallback
            else
                let ariaLabel: obj = command.buttonProps?``aria-label``

                if isNullOrUndefined ariaLabel then
                    fallback
                else
                    string ariaLabel

        let runEditorCommand (command: ICommand) =
            ensureCommandOrchestrator ()
            |> Option.iter (fun orchestrator ->
                orchestrator.executeCommand command
                syncTextFromTextarea ()
            )

        let executeCommand (command: ICommand) =
            fun (_: MouseEvent) ->
                match PluginTextInputHelpers.tryFindPluginForCommand activePlugins command with
                | Some plugin ->
                    match plugin.Prompt with
                    | Some prompt -> openPromptDialog prompt
                    | None -> runEditorCommand command
                | None -> runEditorCommand command

        let handleTextAreaKeyDown =
            fun (e: KeyboardEvent) ->
                match ensureCommandOrchestrator (), tryGetTextarea () with
                | Some orchestrator, Some textarea ->
                    let before = textarea.value
                    ReactMDEditor.handleKeyDown (e, tabSize = 2, defaultTabEnable = false)
                    ReactMDEditor.shortcuts (e, shortcutCommands, orchestrator)

                    if textarea.value <> before then
                        syncTextFromTextarea ()
                | _ -> ()

        let toolbarCommandDisabled = disabled || activeMode = PreviewMode.Preview

        let modeButton (targetMode: PreviewMode, label: string) =
            Html.button [
                prop.type'.button
                prop.className [
                    "swt:btn swt:btn-xs swt:join-item"
                    if activeMode = targetMode then
                        "swt:btn-primary"
                    else
                        "swt:btn-ghost"
                ]
                prop.text label
                prop.disabled disabled
                prop.onClick (fun _ -> setActiveMode targetMode)
            ]



        let editorWrapperClasses = [
            "wmde-markdown-var swt:w-full swt:max-w-none swt:p-0 swt:overflow-hidden swt:min-h-0 swt:rounded-field swt:border swt:border-base-300 swt:bg-base-100"
            if validationError.IsSome then
                "swt:border-error"
            if disabled then
                "swt:opacity-70"
            if classes.IsSome then
                classes.Value
        ]

        let promptViewModel = PluginTextInputHelpers.promptViewModel activePrompt

        let handlePromptInputChange =
            fun (text: string) ->
                setPromptInput text

                if promptError.IsSome then
                    setPromptError None

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
                            prop.className editorWrapperClasses
                            prop.children [
                                Html.div [
                                    prop.className
                                        "swt:flex swt:flex-wrap swt:items-center swt:gap-2 swt:px-2 swt:py-1 swt:border-b swt:border-base-300 swt:bg-base-100"
                                    prop.children [
                                        Html.div [
                                            prop.className "swt:flex swt:flex-wrap swt:items-center swt:gap-1"
                                            prop.children [
                                                for groupIndex, group in toolbarGroups |> Array.indexed do
                                                    if groupIndex > 0 then
                                                        Html.div [
                                                            prop.key $"toolbar-separator-{groupIndex}"
                                                            prop.className "swt:mx-1 swt:h-5 swt:w-px swt:bg-base-300"
                                                        ]

                                                    Html.div [
                                                        prop.key $"toolbar-group-{groupIndex}"
                                                        prop.className "swt:flex swt:items-center swt:gap-1"
                                                        prop.children [
                                                            for commandIndex, command in group |> Array.indexed do
                                                                let label = commandAriaLabel command

                                                                Html.button [
                                                                    prop.key
                                                                        $"toolbar-command-{groupIndex}-{commandIndex}-{command.keyCommand}-{command.name}"
                                                                    prop.type'.button
                                                                    prop.className "swt:btn swt:btn-sm swt:btn-ghost"
                                                                    prop.ariaLabel label
                                                                    prop.title label
                                                                    prop.disabled toolbarCommandDisabled
                                                                    prop.onClick (executeCommand command)
                                                                    prop.children [ command.icon ]
                                                                ]
                                                        ]
                                                    ]
                                            ]
                                        ]

                                        Html.div [
                                            prop.className "swt:join swt:ml-auto"
                                            prop.children [
                                                modeButton (PreviewMode.Edit, "Edit")
                                                modeButton (PreviewMode.Live, "Live")
                                                modeButton (PreviewMode.Preview, "Preview")
                                            ]
                                        ]
                                    ]
                                ]

                                Html.div [
                                    prop.className [
                                        if activeMode = PreviewMode.Live then
                                            "swt:grid swt:grid-cols-1 swt:lg:grid-cols-2"
                                        else
                                            "swt:block"
                                    ]
                                    prop.children [
                                        if activeMode <> PreviewMode.Preview then
                                            Html.div [
                                                prop.className [
                                                    "swt:min-w-0"
                                                    if activeMode = PreviewMode.Live then
                                                        "swt:border-b swt:border-base-300 swt:lg:border-b-0 swt:lg:border-r"
                                                    if height = 560 then
                                                        "swt:h-full"

                                                ]
                                                prop.children [
                                                    Html.textarea [
                                                        prop.ref textareaRef
                                                        prop.className [
                                                            "swt:w-full swt:border-0 swt:swt:bg-transparent swt:resize-none swt:px-3 swt:py-2 swt:focus:outline-hidden"
                                                            if height = 560 then
                                                                "swt:h-full"
                                                        ]
                                                        // if (defaultMode = PreviewMode.Edit) then
                                                        //     prop.style [ style.height height ]

                                                        prop.style [ style.height height ]
                                                        prop.disabled disabled
                                                        prop.readOnly disabled
                                                        prop.value tempValue
                                                        prop.onChange handleTextChange
                                                        prop.onKeyDown handleTextAreaKeyDown
                                                        if placeholder.IsSome then
                                                            prop.placeholder placeholder.Value
                                                    ]
                                                ]
                                            ]

                                        if activeMode <> PreviewMode.Edit then
                                            Html.div [
                                                prop.className
                                                    "swt:min-w-0 swt:h-full swt:overflow-auto swt:bg-base-100"
                                                prop.children [
                                                    MarkdownView.MarkdownView(
                                                        tempValue,
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

                MarkdownPluginPromptModal.View(
                    {
                        IsOpen = activePrompt.IsSome
                        SetIsOpen = closePromptDialog
                        PromptViewModel = promptViewModel
                        PromptInput = promptInput
                        PromptError = promptError
                        PromptFiles = promptFiles
                        PromptFileDropActive = promptFileDropActive
                        PromptInputRef = promptInputRef
                        PromptFileInputRef = promptFileInputRef
                        SetPromptFileDropActive = setPromptFileDropActive
                        OnPromptInputChange = handlePromptInputChange
                        OnPromptFileChange = handlePromptFileChange
                        OnTriggerPromptFileSelection = triggerPromptFileSelection
                        OnPromptDrop = handlePromptDrop
                        OnRemovePromptFileAtIndex = removePromptFileAtIndex
                        OnSubmitPromptDialog = submitPromptDialog
                    }
                )
            ]
        ]

[<Erase; Mangle(false)>]
type TextInputWithMarkdownEntry =

    [<ReactComponent>]
    static member Entry() =
        let entryInitialValue =
            """# Markdown Notes

Use this editor for protocol notes and checklists.

```mermaid
flowchart TD
  A[Draft] --> B{Review}
  B -->|Approved| C[Execute]
  B -->|Changes| A
```
"""

        let value, setValue = React.useState entryInitialValue

        TextInputWithMarkdown.TextInputWithMarkdown(value, setValue, placeholder = "Write markdown...", height = 360)
