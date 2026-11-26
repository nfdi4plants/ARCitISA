namespace Modals

open Fable.React
open Model
open Messages
open Arcitisa.Components.Shared
open Feliz
open Arcitisa.Components
open Arcitisa.Components.Primitive.Buttons

type InteropLogging =
    static member Main(model: DevState, dispatch) =
        let closeMsg = fun _ -> UpdateDisplayLogList [] |> DevMsg |> dispatch

        let logs = model.DisplayLogList

        //Daisy.modal.div [
        Html.div [
            prop.className "swt:modal swt:modal-open"
            prop.children [
                //Daisy.modalBackdrop [ prop.onClick closeMsg ]
                Html.div [
                    prop.className "swt:modal-backdrop"
                    prop.onClick closeMsg
                ]
                //Daisy.modalBox.div [
                Html.div [
                    prop.style [
                        style.width (length.percent 80)
                        style.maxHeight (length.percent 80)
                    ]
                    prop.className "swt:modal-box swt:flex flex-col swt:gap-4"
                    prop.children [
                        Html.div [
                            prop.className "swt:grow swt:flex swt:justify-end"
                            prop.children [ Buttons.DeleteButton(props = [ prop.onClick closeMsg ]) ]
                        ]
                        //Daisy.table [
                        Html.div [
                            prop.className "swt:table swt:table-xs"
                            prop.children [ Html.tbody (logs |> List.map LogItem.toTableRow) ]
                        ]
                        //Daisy.button.a [
                        Html.div [
                            prop.className "swt:btn swt:btn-warning swt:btn-wide swt:mx-auto"
                            prop.onClick closeMsg
                            prop.text "Continue"
                        ]
                    ]
                ]
            ]
        ]
