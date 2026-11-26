module Renderer.Components.InitState

open Browser.Dom
open Feliz
open Renderer.Components.Helper.ArcVaultHelper
open Arcitisa.Components
open Arcitisa.Components.Primitive.BaseModal
open Arcitisa.Components.Primitive.CardGrid
open Arcitisa.Components.Primitive.ErrorModal.Context

[<ReactComponent>]
let CreateNewArcModalContent (close: unit -> unit) =

    let isValid, setIsValid = React.useState (true)
    let temp, setTemp = React.useState ("")
    let initGit, setInitGit = React.useState (true)
    let isBusy, setIsBusy = React.useState (false)
    let errorModal = useErrorModalCtx ()
    let appStateCtx = Renderer.Context.AppStateContext.useAppStateCtx ()

    let onCreateArcError =
        createErrorModalCallback errorModal.enqueue "Could not create ARC" appStateCtx

    let handleSubmit =
        fun () ->
            if isValid && not isBusy then
                setIsBusy true

                promise {
                    let! _ = createArc onCreateArcError temp initGit
                    ()
                }
                |> Promise.catch (fun ex -> console.warn ($"Error during ARC creation: {ex.Message}"))
                |> Promise.start

            close ()

    React.Fragment [
        Html.fieldSet [
            prop.className "swt:fieldset"
            prop.children [
                Html.legend [
                    prop.className "swt:fieldset-legend"
                    prop.text "ARC Identifier"
                ]
                Html.label [
                    prop.className "swt:input swt:w-full"
                    prop.children [
                        Html.input [
                            prop.type'.text
                            prop.required true
                            prop.disabled isBusy
                            prop.onKeyDown (key.enter, fun _ -> handleSubmit ())
                            prop.onChange (fun (v: string) ->
                                if System.String.IsNullOrEmpty v then
                                    setIsValid true
                                else
                                    let isValid = ARCtrl.Helper.Identifier.tryCheckValidCharacters v
                                    setIsValid isValid

                                setTemp v
                            )
                        ]
                    ]
                ]
                Html.p [
                    prop.hidden (isValid)
                    prop.className "swt:text-error"
                    prop.text
                        "New identifier contains forbidden characters! Allowed characters are: letters, digits, underscore (_), dash (-) and whitespace ( )."
                ]
            ]
        ]
        Html.fieldSet [
            prop.className "swt:fieldset swt:mt-4"
            prop.children [
                Html.label [
                    prop.className "swt:label swt:cursor-pointer swt:justify-start swt:gap-2"
                    prop.children [
                        Html.input [
                            prop.type'.checkbox
                            prop.className "swt:checkbox"
                            prop.isChecked initGit
                            prop.onCheckedChange setInitGit
                            prop.disabled isBusy
                            prop.testId "CreateNewArcInitGitCheckbox"
                        ]
                        Html.span [
                            prop.className "swt:label-text"
                            prop.text "Initialize Git Repository"
                        ]
                    ]
                ]
            ]
        ]
        Html.button [
            prop.className "swt:btn swt:mt-4"
            prop.disabled (not isValid || isBusy)
            prop.onClick (fun _ -> handleSubmit ())
            prop.text (if isBusy then "Creating..." else "Create new ARC")
        ]
    ]

[<ReactComponent>]
let InitState () =

    let modalIsOpen, setModalIsOpen = React.useState (false)
    let pageStateCtx = Renderer.Context.PageStateContext.usePageStateCtx ()
    let errorModal = useErrorModalCtx ()
    let appStateCtx = Renderer.Context.AppStateContext.useAppStateCtx ()

    let onOpenArcError =
        createErrorModalCallback errorModal.enqueue "Could not open ARC" appStateCtx

    React.Fragment [
        BaseModal.BaseModal(modalIsOpen, setModalIsOpen, CreateNewArcModalContent(fun () -> setModalIsOpen false))
        CardGrid.CardGrid(
            React.Fragment [
                CardGrid.CardGridButton(
                    Html.i [
                        prop.className "swt:iconify swt:fluent--folder-open-24-filled"
                    ],
                    "Open ARC",
                    "Open a locally existing ARC!",
                    (fun _ -> openArc onOpenArcError |> Promise.start)
                )
                CardGrid.CardGridButton(
                    Html.i [
                        prop.className "swt:iconify swt:fluent--folder-open-24-filled"
                    ],
                    "New ARC",
                    "Create a new ARC!",
                    (fun _ -> setModalIsOpen true)
                )
                CardGrid.CardGridButton(
                    Html.i [
                        prop.className "swt:iconify swt:fluent--cloud-beaker-24-filled swt:size-5"
                    ],
                    "Download ARC",
                    "Download an existing ARC from DataHUB!",
                    (fun _ -> pageStateCtx.setState (Some Renderer.Types.PageState.DataHubBrowser))
                )
            ],
            gridClassName = "swt:grid swt:grid-cols-2"
        )
    ]
