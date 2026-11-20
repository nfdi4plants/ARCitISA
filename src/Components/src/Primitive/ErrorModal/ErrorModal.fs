namespace Swate.Components.Primitive.ErrorModal

open Feliz
open Fable.Core
open Swate.Components
open Swate.Components.Primitive.BaseModal
open Swate.Components.Primitive.ErrorModal.Types

[<Erase; Mangle(false)>]
type ErrorModal =

    [<ReactComponent>]
    static member ActionButton(action: ErrorModalAction) =
        let className =
            match action.Style with
            | ErrorModalActionStyle.Primary -> "swt:btn-primary"
            | ErrorModalActionStyle.Error -> "swt:btn-error"
            | ErrorModalActionStyle.Neutral -> "swt:btn-neutral"

        Html.button [
            prop.className $"swt:btn {className}"
            prop.onClick (fun _ -> action.OnClick())
            prop.children [
                if action.IconClassName.IsSome then
                    Html.i [
                        prop.className [ "swt:iconify"; action.IconClassName.Value ]
                    ]
                Html.span action.Label
            ]
        ]

    [<ReactComponent>]
    static member MessageBlock(message: string) =
        Html.div [
            prop.className "swt:whitespace-pre-wrap"
            prop.children (
                message.Split(Swate.Components.ClipboardContract.Contract.LineBreaks, System.StringSplitOptions.None)
                |> Array.collect (fun line -> [| Html.text line; Html.br [] |])
            )
        ]

    [<ReactComponent>]
    static member DetailsBlock(details: string) =
        Html.details [
            prop.className "swt:collapse swt:collapse-arrow swt:border swt:border-base-300 swt:bg-base-200"
            prop.children [
                Html.summary [
                    prop.className "swt:collapse-title swt:min-h-0 swt:py-3 swt:font-medium"
                    prop.text "Technical details"
                ]
                Html.div [
                    prop.className "swt:collapse-content"
                    prop.children [
                        Html.pre [
                            prop.className "swt:whitespace-pre-wrap swt:text-sm swt:max-h-80 swt:overflow-auto"
                            prop.text details
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member BodyBlock(request: ErrorModalRequest) =

        let details =
            match request.Details with
            | None -> Html.none
            | Some details -> ErrorModal.DetailsBlock(details)

        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-3"
            prop.children [ ErrorModal.MessageBlock request.Message; details ]
        ]

    [<ReactComponent(true)>]
    static member Modal
        (
            isOpen: bool,
            setIsOpen: bool -> unit,
            title: string,
            children: ReactElement,
            ?headerAdornment: ReactElement,
            ?description: ReactElement,
            ?modalActions: ReactElement,
            ?footer: ReactElement,
            ?initialFocusRef: IRefValue<option<Browser.Types.HTMLElement>>,
            ?returnFocusRef: IRefValue<option<Browser.Types.HTMLElement>>,
            ?debug: string,
            ?className: string
        ) : ReactElement =
        BaseModal.Modal(
            isOpen = isOpen,
            setIsOpen = setIsOpen,
            header =
                Html.div [
                    prop.className "swt:flex swt:items-center swt:gap-2"
                    prop.children [
                        Html.i [
                            prop.className "swt:iconify swt:fluent--error-circle-24-filled swt:size-6"
                        ]
                        Html.span title
                        if headerAdornment.IsSome then
                            headerAdornment.Value
                    ]
                ],
            children = children,
            ?description = description,
            ?modalActions = modalActions,
            ?footer = footer,
            ?initialFocusRef = initialFocusRef,
            ?returnFocusRef = returnFocusRef,
            ?debug = debug,
            ?className = className
        )
