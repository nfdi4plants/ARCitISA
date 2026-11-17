/// The leave guard of the window: the controller behind LeaveGuardCtx and its Save / Discard / Cancel dialog.
module Renderer.Components.LeaveGuard

open Browser.Types
open Fable.Core
open Feliz
open Swate.Components.Primitive.BaseModal
open Swate.Electron.Shared
open Renderer.Context.LeaveGuardContext

module private LeaveGuardHelpers =

    /// A beforeunload handler that sets returnValue cancels a reload in Electron. Main then gets
    /// 'will-prevent-unload' and decides (see ArcVault.OnCloseWindow).
    [<Emit("$0.returnValue = false")>]
    let blockUnload (event: Event) : unit = jsNative

    let notSavedForClose =
        exn
            "The text file was not saved. The message at the top of its page says why. The window stays open so you can resolve it."

open LeaveGuardHelpers

type private DialogState = {
    FileName: string
    Saving: bool
    SaveFailed: bool
    SaveRunning: bool
}

/// Returns the controller for LeaveGuardCtx and the dialog element to render once in the window.
[<Hook>]
let useLeaveGuard () : LeaveGuardController * ReactElement =
    let registrationRef = React.useRef<LeaveGuardRegistration option> None
    let dirtyRef = React.useRef false
    let closeSaveRunningRef = React.useRef false
    let pushChainRef = React.useRef<JS.Promise<unit>> (Promise.lift ())
    let resolverRef = React.useRef<(bool -> unit) option> None
    let dialog, setDialog = React.useState (None: DialogState option)

    let controller =
        React.useMemo (
            (fun () ->
                // Main decides about closing the window from this flag. Pushes are chained so they arrive in order.
                let pushDirty (value: bool) : JS.Promise<unit> =
                    let next =
                        pushChainRef.current
                        |> Promise.bind (fun () ->
                            Api.ipcArcVaultApi.setHasUnsavedTextChanges value
                            |> Promise.map ignore
                            |> Promise.catch ignore
                        )

                    pushChainRef.current <- next
                    next

                let setDirty (value: bool) =
                    if value <> dirtyRef.current then
                        dirtyRef.current <- value
                        pushDirty value |> ignore

                let finish (result: bool) =
                    let resolve = resolverRef.current
                    resolverRef.current <- None
                    setDialog None
                    resolve |> Option.iter (fun r -> r result)

                let confirmLeave () : JS.Promise<bool> =
                    match registrationRef.current with
                    | Some registration when dirtyRef.current ->
                        if resolverRef.current.IsSome then
                            Promise.lift false
                        else
                            Promise.create (fun resolve _ ->
                                resolverRef.current <- Some resolve

                                setDialog (
                                    Some {
                                        FileName =
                                            Swate.Components.Shared.PathHelpers.getFileName registration.documentPath
                                        Saving = false
                                        SaveFailed = false
                                        SaveRunning = false
                                    }
                                )
                            )
                    | _ -> Promise.lift true

                let isRegistered (path: string) =
                    registrationRef.current
                    |> Option.exists (fun r -> TextDocumentHelper.isSameDocumentPath r.documentPath path)

                let saveForClose () : JS.Promise<Result<unit, exn>> =
                    match registrationRef.current with
                    | Some registration when dirtyRef.current ->
                        closeSaveRunningRef.current <- true

                        promise {
                            let! saved = registration.save () |> Promise.catch (fun _ -> false)

                            if saved && not (registration.isDirty ()) then
                                dirtyRef.current <- false
                                do! pushDirty false
                                closeSaveRunningRef.current <- false
                                return Ok()
                            else
                                closeSaveRunningRef.current <- false
                                return Error notSavedForClose
                        }
                    | _ -> Promise.lift (Ok())

                {
                    register =
                        fun registration ->
                            registrationRef.current <- Some registration
                            setDirty false

                            fun () ->
                                match registrationRef.current with
                                | Some current when obj.ReferenceEquals(current, registration) ->
                                    registrationRef.current <- None
                                    setDirty false
                                | _ -> ()
                    setDirty = setDirty
                    isDirty = fun () -> dirtyRef.current
                    confirmLeave = confirmLeave
                    isDirtyDocument = fun path -> dirtyRef.current && isRegistered path
                    documentPath = fun () -> registrationRef.current |> Option.map _.documentPath
                    notifyRemoved =
                        fun path ->
                            if isRegistered path then
                                registrationRef.current |> Option.iter (fun r -> r.onRemovedFromDisk ())
                    saveForClose = saveForClose
                },
                finish
            ),
            [||]
        )

    let controller, finish = controller

    // Reload (View > Reload, Ctrl+R) skips the close flow, so the window itself refuses to unload while text is
    // unsaved or a close save runs.
    React.useEffect (
        (fun () ->
            let handler (event: Event) =
                if dirtyRef.current || closeSaveRunningRef.current then
                    blockUnload event

            Browser.Dom.window.addEventListener ("beforeunload", handler)

            { new System.IDisposable with
                member _.Dispose() =
                    Browser.Dom.window.removeEventListener ("beforeunload", handler)
            }
        ),
        [||]
    )

    let save () =
        match registrationRef.current, dialog with
        | Some registration, Some state when not state.Saving ->
            setDialog (
                Some {
                    state with
                        Saving = true
                        SaveFailed = false
                        SaveRunning = false
                }
            )

            promise {
                let! saved = registration.save () |> Promise.catch (fun _ -> false)

                if saved && not (registration.isDirty ()) then
                    finish true
                else
                    setDialog (
                        Some {
                            state with
                                Saving = false
                                SaveFailed = true
                                SaveRunning = false
                        }
                    )
            }
            |> Promise.start
        | _ -> ()

    let discard () =
        match registrationRef.current, dialog with
        | Some registration, Some state when registration.isSaving () ->
            setDialog (Some { state with SaveRunning = true })
        | _ ->
            registrationRef.current |> Option.iter (fun r -> r.discard ())
            controller.setDirty false
            finish true

    let saving = dialog |> Option.exists _.Saving

    let element =
        BaseModal.Modal(
            isOpen = dialog.IsSome,
            setIsOpen =
                (fun isOpen ->
                    if not isOpen && not saving then
                        finish false
                ),
            header = Html.text "Unsaved changes",
            // BaseModal renders the description inside a <p>, so it gets text only. Block content goes in children.
            description =
                Html.text
                    $"""{dialog |> Option.map _.FileName |> Option.defaultValue "The file"} has unsaved changes.""",
            children =
                Html.div [
                    if dialog |> Option.exists _.SaveFailed then
                        Html.p [
                            prop.role "alert"
                            prop.className "swt:text-error"
                            prop.text
                                "Saving failed. The changes are still unsaved. The message at the top of the text page says why."
                        ]
                    if dialog |> Option.exists _.SaveRunning then
                        Html.p [
                            prop.role "alert"
                            prop.className "swt:text-error"
                            prop.text "A save is still running. Wait until it finishes, then choose again."
                        ]
                ],
            footer =
                Html.div [
                    prop.className "swt:flex swt:gap-2 swt:w-full"
                    prop.children [
                        Html.button [
                            prop.className "swt:btn swt:btn-neutral"
                            prop.text "Cancel"
                            prop.disabled saving
                            prop.onClick (fun _ -> finish false)
                        ]
                        Html.button [
                            prop.className "swt:btn swt:ml-auto"
                            prop.text "Discard"
                            prop.disabled saving
                            prop.onClick (fun _ -> discard ())
                        ]
                        Html.button [
                            prop.className "swt:btn swt:btn-primary"
                            prop.text (if saving then "Saving..." else "Save")
                            prop.disabled saving
                            prop.onClick (fun _ -> save ())
                        ]
                    ]
                ]
        )

    controller, element
