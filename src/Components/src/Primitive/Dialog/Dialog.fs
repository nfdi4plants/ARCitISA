namespace Swate.Components.Primitive.Dialog

open Feliz
open Fable.Core
open Swate.Components.Primitive.BaseModal

[<Erase; Mangle(false)>]
type Dialog =

    [<ReactComponent>]
    static member Dialog
        (
            isOpen: bool,
            setIsOpen: bool -> unit,
            header: ReactElement,
            children: ReactElement,
            ?description: ReactElement,
            ?footer: ReactElement,
            ?debug: string,
            ?className: string
        ) =
        BaseModal.Modal(
            isOpen = isOpen,
            setIsOpen = setIsOpen,
            header = header,
            children = children,
            ?description = description,
            ?footer = footer,
            ?debug = debug,
            ?className = className
        )

    [<ReactComponent>]
    static member StringSubmissionDialog
        (
            isOpen: bool,
            title: string,
            description: string,
            fieldLabel: string,
            initialValue: string,
            close: unit -> unit,
            submit: string -> unit,
            validate: string -> Result<string, string>,
            submitLabel: string,
            ?validationMessage: string,
            ?isBusy: bool,
            ?busyLabel: string,
            ?debug: string
        ) =

        let value, setValue = React.useState initialValue
        let isBusy = defaultArg isBusy false
        let busyLabel = defaultArg busyLabel submitLabel

        React.useEffect ((fun () -> setValue initialValue), [| box initialValue; box isOpen; box title |])

        let setIsOpen isOpen =
            if not isOpen then
                close ()

        let validationResult = validate value
        let isValid = validationResult |> Result.isOk

        // The validator's own error text wins, so the user sees why this input is rejected.
        let shownValidationMessage =
            match validationResult with
            | Error errorText when not (System.String.IsNullOrWhiteSpace errorText) -> errorText
            | _ -> defaultArg validationMessage ""

        let submitIfValid () =
            match validationResult with
            | Ok normalizedValue when not isBusy -> submit normalizedValue
            | _ -> ()

        let footer =
            Html.div [
                prop.className "swt:flex swt:gap-2 swt:justify-end swt:w-full"
                prop.children [
                    Html.button [
                        prop.className "swt:btn swt:btn-ghost"
                        prop.disabled isBusy
                        prop.onClick (fun _ -> close ())
                        prop.text "Cancel"
                    ]
                    Html.button [
                        prop.className "swt:btn swt:btn-primary"
                        prop.disabled ((not isValid) || isBusy)
                        prop.onClick (fun _ -> submitIfValid ())
                        prop.text (if isBusy then busyLabel else submitLabel)
                    ]
                ]
            ]

        let content =
            Html.fieldSet [
                prop.className "swt:fieldset"
                prop.children [
                    Html.legend [
                        prop.className "swt:fieldset-legend"
                        prop.text fieldLabel
                    ]
                    Html.label [
                        prop.className "swt:input swt:w-full"
                        prop.children [
                            Html.input [
                                prop.autoFocus true
                                prop.disabled isBusy
                                prop.value value
                                prop.onChange setValue
                                prop.onKeyDown (key.enter, fun _ -> submitIfValid ())
                            ]
                        ]
                    ]
                    Html.p [
                        prop.hidden isValid
                        prop.className "swt:text-error swt:text-sm"
                        prop.text shownValidationMessage
                    ]
                ]
            ]

        Dialog.Dialog(
            isOpen = isOpen,
            setIsOpen = setIsOpen,
            header = Html.text title,
            description = Html.text description,
            children = content,
            footer = footer,
            ?debug = debug
        )
