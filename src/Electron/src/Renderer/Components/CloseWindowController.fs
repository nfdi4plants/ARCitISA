namespace Renderer.Components

open Feliz
open Fable.Core
open Arcitisa.Components
open Arcitisa.Components.Primitive.BaseModal
open Arcitisa.Components.Primitive.ErrorModal.Context
open Arcitisa.Components.Primitive.ErrorModal.Types
open Arcitisa.Electron.Shared.IPCTypes
open Arcitisa.Electron.Shared.IPCTypes.IPCTypesHelper

[<Erase; Mangle(false)>]
type CloseWindowController =

    [<ReactComponent>]
    static member CloseWindowController
        (
            ?onConfirmSave: unit -> JS.Promise<Result<unit, exn>>,
            ?onConfirmClose: unit -> unit,
            ?onCancelClose: unit -> unit
        ) : ReactElement =

        let modalIsOpen, setModalIsOpen = React.useState false
        let errorModal = useErrorModalCtx ()

        let enqueueCloseError (title: string) (saveError: exn) =
            errorModal.enqueue (ErrorModalRequest.create (saveError.Message, title = title))

        let resolveCloseRequest (decision: SaveBeforeQuitDecision) =
            Api.ipcArcVaultApi.resolveCloseRequest decision

        let handleCancel () =
            promise {
                match! resolveCloseRequest SaveBeforeQuitDecision.CancelClose with
                | Ok() ->
                    setModalIsOpen false
                    onCancelClose |> Option.iter (fun fn -> fn ())
                | Error resolveError -> enqueueCloseError "Could not cancel close request" resolveError
            }
            |> Promise.start

        let handleCloseWithoutSaving () =
            promise {
                match! resolveCloseRequest SaveBeforeQuitDecision.CloseWithoutSaving with
                | Ok() ->
                    setModalIsOpen false
                    onConfirmClose |> Option.iter (fun fn -> fn ())
                | Error resolveError -> enqueueCloseError "Could not close window" resolveError
            }
            |> Promise.start

        let saveBeforeClose () : JS.Promise<Result<unit, exn>> =
            if onConfirmSave.IsSome then
                onConfirmSave.Value()
            else
                promise { return Ok() }

        let handleSaveAndClose () =
            promise {
                let! saveResult = saveBeforeClose ()

                match saveResult with
                | Ok() ->
                    match! resolveCloseRequest SaveBeforeQuitDecision.SaveAndClose with
                    | Ok() -> setModalIsOpen false
                    | Error resolveError -> enqueueCloseError "Could not save and close window" resolveError
                | Error saveError ->
                    // Main keeps a close request pending until it hears back, and would ignore the next close.
                    let! _ = resolveCloseRequest SaveBeforeQuitDecision.CancelClose
                    setModalIsOpen false
                    enqueueCloseError "Save before close failed" saveError
            }
            |> Promise.start

        let saveBeforeQuitHandler: IMainSaveBeforeQuitApi = {
            // This IPC call is triggered by the ArcVaults window close event. It should open the modal to ask user what they want to do.
            requestSaveBeforeQuit = fun () -> setModalIsOpen true
        }

        Renderer.IpcReceiver.useProxyReceiver<IMainSaveBeforeQuitApi> ((fun () -> saveBeforeQuitHandler), [||])

        BaseModal.Modal(
            isOpen = modalIsOpen,
            setIsOpen =
                (fun isOpen ->
                    if not isOpen then
                        handleCancel ()
                ),
            header = Html.text "Close Window",
            description = Html.text "Do you want to save your changes before closing this window?",
            children = Html.none,
            footer =
                Html.div [
                    prop.className "swt:flex swt:gap-2 swt:w-full"
                    prop.children [
                        Html.button [
                            prop.className "swt:btn swt:btn-neutral"
                            prop.text "Cancel"
                            prop.onClick (fun _ -> handleCancel ())
                        ]
                        Html.button [
                            prop.className "swt:btn swt:ml-auto"
                            prop.text "Close"
                            prop.onClick (fun _ -> handleCloseWithoutSaving ())
                        ]
                        Html.button [
                            prop.className "swt:btn swt:btn-primary"
                            prop.text "Save and Close"
                            prop.onClick (fun _ -> handleSaveAndClose ())
                        ]
                    ]
                ]
        )
