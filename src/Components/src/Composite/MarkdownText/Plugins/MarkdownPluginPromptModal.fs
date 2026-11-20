namespace Swate.Components.Composite.MarkdownText.Plugins

open Browser.Types
open Feliz

open Swate.Components
open Swate.Components.Primitive.BaseModal

[<RequireQualifiedAccess>]
module MarkdownPluginPromptModal =

    type ViewProps = {
        IsOpen: bool
        SetIsOpen: bool -> unit
        PromptViewModel: PluginTextInputHelpers.PromptViewModel
        PromptInput: string
        PromptError: string option
        PromptFiles: MarkdownPromptFile list
        PromptFileDropActive: bool
        PromptInputRef: IRefValue<option<HTMLInputElement>>
        PromptFileInputRef: IRefValue<option<HTMLInputElement>>
        SetPromptFileDropActive: bool -> unit
        OnPromptInputChange: string -> unit
        OnPromptFileChange: File list -> unit
        OnTriggerPromptFileSelection: unit -> unit
        OnPromptDrop: DragEvent -> unit
        OnRemovePromptFileAtIndex: int -> unit
        OnSubmitPromptDialog: unit -> unit
    }

    [<ReactComponent>]
    let View (props: ViewProps) =
        let isOpen = props.IsOpen
        let setIsOpen = props.SetIsOpen
        let promptViewModel = props.PromptViewModel
        let promptInput = props.PromptInput
        let promptError = props.PromptError
        let promptFiles = props.PromptFiles
        let promptFileDropActive = props.PromptFileDropActive
        let promptInputRef = props.PromptInputRef
        let promptFileInputRef = props.PromptFileInputRef
        let setPromptFileDropActive = props.SetPromptFileDropActive
        let onPromptInputChange = props.OnPromptInputChange
        let onPromptFileChange = props.OnPromptFileChange
        let onTriggerPromptFileSelection = props.OnTriggerPromptFileSelection
        let onPromptDrop = props.OnPromptDrop
        let onRemovePromptFileAtIndex = props.OnRemovePromptFileAtIndex
        let onSubmitPromptDialog = props.OnSubmitPromptDialog

        let isFilePrompt = promptViewModel.InputMode = MarkdownPromptInputMode.File

        let promptDescription = promptViewModel.Description |> Option.map Html.text

        let promptFileKey (file: MarkdownPromptFile) =
            let hostPath = file.HostPath |> Option.defaultValue ""
            let mimeType = file.MimeType |> Option.defaultValue ""
            $"{hostPath}|{file.Name}|{mimeType}"

        BaseModal.Modal(
            isOpen = isOpen,
            setIsOpen = setIsOpen,
            header = Html.text promptViewModel.Title,
            ?description = promptDescription,
            children =
                Html.div [
                    prop.className "swt:flex swt:flex-col swt:gap-2"
                    prop.children [
                        if isFilePrompt then
                            Html.input [
                                prop.testId "markdown-plugin-file-input"
                                prop.ref promptFileInputRef
                                prop.type'.file
                                prop.className "swt:hidden"
                                prop.multiple promptViewModel.AllowMultipleFiles
                                if promptViewModel.AcceptTypes.IsSome then
                                    prop.accept promptViewModel.AcceptTypes.Value
                                prop.onChange onPromptFileChange
                            ]

                            let fileDropButtonText = "Drop files here or click to upload"

                            Html.button [
                                prop.type'.button
                                prop.testId "markdown-plugin-file-dropzone"
                                prop.className [
                                    "swt:btn swt:btn-outline swt:w-full swt:h-auto swt:min-h-0 swt:py-3 swt:border-2 swt:border-dashed swt:rounded-box swt:text-sm swt:text-center swt:normal-case"
                                    if promptFileDropActive then
                                        "swt:border-primary swt:bg-primary/10"
                                    else
                                        "swt:border-base-300"
                                ]
                                prop.onClick (fun _ -> onTriggerPromptFileSelection ())
                                prop.onDragEnter (fun (e: DragEvent) ->
                                    e.preventDefault ()
                                    e.stopPropagation ()
                                    setPromptFileDropActive true
                                )
                                prop.onDragLeave (fun (e: DragEvent) ->
                                    e.preventDefault ()
                                    e.stopPropagation ()
                                    setPromptFileDropActive false
                                )
                                prop.onDragOver (fun (e: DragEvent) ->
                                    e.preventDefault ()
                                    e.stopPropagation ()
                                )
                                prop.onDrop onPromptDrop
                                prop.text fileDropButtonText
                            ]

                            if List.isEmpty promptFiles then
                                Html.div [
                                    prop.className "swt:text-xs swt:opacity-70"
                                    prop.text "No files selected."
                                ]
                            else
                                Html.ul [
                                    prop.className "swt:flex swt:flex-col swt:gap-1"
                                    prop.children [
                                        for index, file in promptFiles |> List.indexed do
                                            Html.li [
                                                prop.key $"prompt-file-{promptFileKey file}"
                                                prop.className "swt:flex swt:items-center swt:gap-2"
                                                prop.children [
                                                    Html.span [
                                                        prop.className "swt:grow swt:text-sm swt:truncate swt:text-left"
                                                        prop.title file.Name
                                                        prop.text file.Name
                                                    ]
                                                    Html.button [
                                                        prop.type'.button
                                                        prop.className "swt:btn swt:btn-xs swt:btn-ghost"
                                                        prop.ariaLabel $"Remove {file.Name}"
                                                        prop.text "x"
                                                        prop.onClick (fun _ -> onRemovePromptFileAtIndex index)
                                                    ]
                                                ]
                                            ]
                                    ]
                                ]
                        else
                            Html.input [
                                prop.ref promptInputRef
                                prop.className [
                                    "swt:input swt:input-bordered swt:w-full"
                                    if promptError.IsSome then
                                        "swt:input-error"
                                ]
                                prop.placeholder promptViewModel.Placeholder
                                prop.value promptInput
                                prop.onChange onPromptInputChange
                                prop.onKeyDown (fun (e: KeyboardEvent) ->
                                    if e.key = "Enter" then
                                        e.preventDefault ()
                                        onSubmitPromptDialog ()
                                )
                            ]
                        if promptError.IsSome then
                            Html.p [
                                prop.className "swt:text-error swt:text-sm"
                                prop.text promptError.Value
                            ]
                    ]
                ],
            footer =
                React.Fragment [
                    Html.button [
                        prop.className "swt:btn"
                        prop.text "Cancel"
                        prop.onClick (fun _ -> setIsOpen false)
                    ]
                    Html.button [
                        prop.className "swt:btn swt:btn-primary swt:ml-auto"
                        prop.text promptViewModel.SubmitButtonText
                        prop.onClick (fun _ -> onSubmitPromptDialog ())
                    ]
                ],
            // A file prompt has no text input. The modal then focuses the drop zone, not the hidden file input.
            ?initialFocusRef = (if isFilePrompt then None else Some(unbox promptInputRef)),
            className = "swt:bg-base-100 swt:text-base-content"
        )
