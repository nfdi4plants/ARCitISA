module Renderer.Components.LeftSidebar.Main

open Fable.Core
open Feliz
open Renderer.Types
open Swate.Components.Primitive.ErrorModal.Context
open Swate.Components.Primitive.ErrorModal.Types
open Swate.Electron.Shared.FileIOTypes
open Renderer.Components.LeftSidebar.FileImportStatus

[<ReactComponent>]
let private FileImportStatusNotice () =
    let fileStateCtx = Renderer.Context.FileStateContext.useFileStateCtx ()
    let errorModal = useErrorModalCtx ()

    let cancelImport () =
        fileStateCtx.cancelFileImport ()
        |> Promise.map (
            Result.mapError (fun cancelError ->
                errorModal.enqueue (ErrorModalRequest.create (cancelError.Message, title = "Could not cancel import"))
            )
        )
        |> Promise.catch (fun cancelError ->
            errorModal.enqueue (ErrorModalRequest.create (cancelError.Message, title = "Could not cancel import"))
            Ok()
        )
        |> Promise.start

    match fileStateCtx.activeFileImport with
    | None -> Html.none
    | Some activeImport ->
        Html.div [
            prop.className
                "swt:fixed swt:inset-0 swt:z-50 swt:flex swt:items-center swt:justify-center swt:bg-base-100/20"
            prop.children [
                Html.span [
                    prop.className "swt:sr-only"
                    prop.role "status"
                    prop.custom ("aria-live", "polite")
                    prop.text (fileImportStepText activeImport fileStateCtx.isCancellingFileImport)
                ]
                Html.div [
                    prop.className
                        "swt:alert swt:alert-info swt:w-fit swt:max-w-md swt:shadow-lg swt:pointer-events-auto"
                    prop.children [
                        let statusText =
                            fileImportStatusText activeImport fileStateCtx.isCancellingFileImport

                        if showsFileImportProgressBar activeImport fileStateCtx.isCancellingFileImport then
                            Html.div [
                                prop.className "swt:flex swt:flex-col swt:gap-2 swt:min-w-0 swt:grow"
                                prop.children [
                                    Html.span [
                                        prop.custom ("aria-hidden", "true")
                                        prop.text statusText
                                    ]
                                    FileImportProgressBar activeImport.progress
                                ]
                            ]
                        else
                            Html.div [
                                prop.className "swt:contents"
                                prop.custom ("aria-hidden", "true")
                                prop.children [
                                    Swate.Components.Primitive.LoadingSpinner.LoadingSpinner.LoadingSpinner(
                                        text = statusText
                                    )
                                ]
                            ]

                        if
                            not fileStateCtx.isCancellingFileImport
                            && activeImport.phase = FileImportPhase.Copying
                        then
                            Html.button [
                                prop.className "swt:btn swt:btn-ghost swt:btn-xs swt:shrink-0 swt:gap-1 swt:normal-case"
                                prop.title "Cancel"
                                prop.onClick (fun _ -> cancelImport () |> ignore)
                                prop.children [
                                    Html.span [
                                        prop.className "swt:iconify swt:fluent--dismiss-circle-24-regular swt:size-4"
                                    ]
                                    Html.span [ prop.text "Cancel" ]
                                ]
                            ]
                    ]
                ]
            ]
        ]

/// This can be further reduced by using the actual contexts instead of passing down the states and setters as props, but this is good enough for now
[<ReactComponent>]
let Main (leftSidebarTarget: LeftSidebarPage) =
    Html.div [
        prop.className [
            "swt:box-border"
            "swt:flex"
            "swt:h-full"
            "swt:min-h-0"
            "swt:min-w-0"
            "swt:max-w-full"
            "swt:flex-col"
            "swt:overflow-hidden"
            match leftSidebarTarget with
            | LeftSidebarPage.FileExplorer -> "swt:p-4"
            // The git sidebar pads its own sections, so a wrapper padding would double the gap to the panel edge.
            | LeftSidebarPage.Git -> ()
        ]
        prop.children [|
            match leftSidebarTarget with
            | LeftSidebarPage.FileExplorer -> Renderer.Components.LeftSidebar.FileExplorer.Main.Main()
            | LeftSidebarPage.Git -> Git.GitSidebarPanel.Main()
            FileImportStatusNotice()
        |]
    ]
