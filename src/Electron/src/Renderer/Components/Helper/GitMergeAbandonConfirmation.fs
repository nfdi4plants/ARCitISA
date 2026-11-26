module Renderer.Components.Helper.GitMergeAbandonConfirmation

open Fable.Core
open Feliz
open Arcitisa.Components.Primitive.BaseModal

[<ReactComponent>]
let Main (isBusy: bool, onAbandon: unit -> unit, testIdPrefix: string) =
    let isOpen, setIsOpen = React.useState false

    let close () = setIsOpen false

    let setModalOpen openState =
        if not openState && not isBusy then
            close ()

    let confirmAbandon () =
        if not isBusy then
            close ()
            onAbandon ()

    React.Fragment [
        Html.button [
            prop.testId $"{testIdPrefix}-abandon"
            prop.type'.button
            prop.className "swt:btn swt:btn-ghost"
            prop.disabled isBusy
            prop.text "Abandon merge"
            prop.onClick (fun _ ->
                if not isBusy then
                    setIsOpen true
            )
        ]

        BaseModal.Modal(
            isOpen = isOpen,
            setIsOpen = setModalOpen,
            header = Html.text "Abandon merge?",
            children =
                Html.p [
                    prop.testId $"{testIdPrefix}-abandon-message"
                    prop.text "Stop merging and go back to your last saved state? Nothing online changes."
                ],
            footer =
                Html.div [
                    prop.className "swt:flex swt:justify-end swt:gap-2 swt:w-full"
                    prop.children [
                        Html.button [
                            prop.testId $"{testIdPrefix}-keep-merging"
                            prop.type'.button
                            prop.className "swt:btn swt:btn-ghost"
                            prop.disabled isBusy
                            prop.text "Keep merging"
                            prop.onClick (fun _ -> close ())
                        ]
                        Html.button [
                            prop.testId $"{testIdPrefix}-confirm-abandon"
                            prop.type'.button
                            prop.className "swt:btn swt:btn-error"
                            prop.disabled isBusy
                            prop.text "Abandon merge"
                            prop.onClick (fun _ -> confirmAbandon ())
                        ]
                    ]
                ],
            debug = $"{testIdPrefix}-abandon-confirmation"
        )
    ]
