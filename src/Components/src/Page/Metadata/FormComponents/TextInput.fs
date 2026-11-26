namespace Arcitisa.Components.Page.Metadata.FormComponents

open Fable.Core
open Browser.Types
open Arcitisa.Components
open Feliz
open Arcitisa.Components.Primitive.LayoutComponents

[<Erase; Mangle(false)>]
type TextInput =

    [<ReactComponent>]
    static member TextInput
        (
            value: string,
            setValue: string -> unit,
            ?label: string,
            ?placeholder: string,
            ?isArea: bool,
            ?disabled: bool,
            ?classes: string,
            ?isJoin: bool,
            ?rmv: MouseEvent -> unit,
            ?validator: string -> Result<unit, string>,
            ?trailingAction: string -> ReactElement,
            ?onDraftChange: string -> unit
        ) =
        let isArea = defaultArg isArea false
        let disabled = defaultArg disabled false
        let isJoin = defaultArg isJoin false
        let startedChange = React.useRef false
        let tempValue, setTempValue = React.useState value
        let debouncedValue = React.useDebounce (tempValue, 300)
        let validationError, setValidationError = React.useState (None: string option)

        React.useEffect (
            (fun () ->
                if startedChange.current then
                    match validator with
                    | Some validate ->
                        match validate debouncedValue with
                        | Ok() ->
                            setValidationError None
                            setValue debouncedValue
                        | Error msg -> setValidationError (Some msg)
                    | None ->
                        setValidationError None
                        setValue debouncedValue

                    startedChange.current <- false
            ),
            [| box debouncedValue |]
        )

        React.useEffect ((fun () -> setTempValue value), [| box value |])

        // setValue only sees the text after the debounce. This reports every draft, including the initial one
        // and drafts reset from an outside value change, so a parent always holds what the field shows.
        React.useEffect ((fun () -> onDraftChange |> Option.iter (fun notify -> notify tempValue)), [| box tempValue |])

        let handleChange (text: string) =
            setTempValue text
            startedChange.current <- true

        let inputClasses = [
            if isJoin then
                "swt:join-item"
            "swt:input swt:input-bordered swt:w-full"
            if trailingAction.IsSome then
                "swt:min-w-0"
            if validationError.IsSome then
                "swt:input-error"
            if classes.IsSome then
                classes.Value
        ]

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
                if isArea then
                    Html.textarea [
                        prop.className [
                            "swt:textarea swt:textarea-bordered swt:w-full"
                            if validationError.IsSome then
                                "swt:textarea-error"
                            if classes.IsSome then
                                classes.Value
                        ]
                        prop.disabled disabled
                        prop.readOnly disabled
                        prop.valueOrDefault tempValue
                        prop.onChange handleChange
                        if placeholder.IsSome then
                            prop.placeholder placeholder.Value
                    ]
                else
                    Html.div [
                        prop.className "swt:flex swt:gap-2 swt:items-center swt:w-full"
                        prop.children [
                            Html.input [
                                prop.className inputClasses
                                prop.type'.text
                                prop.disabled disabled
                                prop.readOnly disabled
                                prop.valueOrDefault tempValue
                                prop.onChange handleChange
                                if placeholder.IsSome then
                                    prop.placeholder placeholder.Value
                            ]
                            if trailingAction.IsSome then
                                trailingAction.Value tempValue
                            if rmv.IsSome then
                                Helpers.deleteButton rmv.Value
                        ]
                    ]
                if validationError.IsSome then
                    Html.p [
                        prop.className "swt:text-error swt:text-sm swt:mt-1"
                        prop.text validationError.Value
                    ]
            ]
        ]

    [<ReactComponent>]
    static member CollectionOfStrings
        (values: ResizeArray<string>, setValues: ResizeArray<string> -> unit, ?label: string)
        =
        InputSequence.InputSequence(
            values,
            (fun () -> ""),
            setValues,
            (fun (value, setValue, remove) -> TextInput.TextInput(value, setValue, rmv = remove)),
            ?label = label
        )
