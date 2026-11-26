namespace Modals

open Feliz
open Model
open Messages
open Arcitisa.Components.Shared

open ARCtrl
open FileImport
open Fable.React.Helpers

type ModalElements =

    static member Button(text: string, onClickAction, buttonInput, ?isDisabled: bool, ?className: string) =
        let isDisabled = defaultArg isDisabled false

        //Daisy.button.a [
        Html.button [
            prop.className [
                "swt:btn swt:btn-primary"
                if isDisabled then
                    "swt:btn-error"
                if className.IsSome then
                    className.Value
            ]
            prop.disabled isDisabled
            prop.onClick (fun _ -> onClickAction buttonInput)
            prop.text text
        ]

    static member RadioPlugin(radioGroup: string, txt: string, isChecked, onChange: bool -> unit, ?isDisabled: bool) =
        let isDisabled = defaultArg isDisabled false

        //Daisy.fieldset [
        Html.div [
            prop.className "swt:fieldset"
            prop.children [
                //Daisy.label [
                Html.label [
                    prop.className [
                        "swt:label swt:cursor-pointer swt:transition-colors"
                        if isDisabled then
                            "swt:!cursor-not-allowed"
                        else
                            "swt:hover:bg-base-300"
                    ]
                    prop.children [
                        //Daisy.radio [
                        Html.input [
                            prop.className "swt:radio swt:radio-xs"
                            prop.type'.radio
                            prop.disabled isDisabled
                            prop.name radioGroup
                            prop.isChecked isChecked
                            prop.onChange onChange
                        ]
                        Html.span [ prop.className "swt:text-sm"; prop.text txt ]
                    ]
                ]
            ]
        ]

    static member Box(title: string, icon: ReactElement, content: ReactElement, ?className: string list) =
        Html.div [
            prop.className [
                "swt:rounded-sm swt:shadow-sm swt:p-2 swt:flex swt:flex-col swt:gap-2 swt:border-3"
                if className.IsSome then
                    className.Value |> String.concat " "
            ]
            prop.children [
                Html.h3 [
                    prop.className "swt:font-semibold swt:gap-2 swt:flex swt:flex-row swt:items-center"
                    prop.children [ icon; Html.span title ]
                ]
                content
            ]
        ]

    static member SelectorButton<'a when 'a: equality>
        (targetselector: 'a, selector: 'a, setSelector: 'a -> unit, ?isDisabled)
        =
        //Daisy.button.button [
        Html.button [
            prop.className [
                "swt:btn swt:join-item"
                if (targetselector = selector) then
                    "swt:btn-primary"
            ]
            if isDisabled.IsSome then
                prop.disabled isDisabled.Value
            prop.style [ style.flexGrow 1 ]
            prop.onClick (fun _ -> setSelector targetselector)
            prop.text (string targetselector)
        ]
