module Renderer.Components.MainContent.ArcSyncNotice

open Feliz

/// A short notice above the page while external changes load, after a failed load, and after a load replaced unsaved
/// edits. While a load runs, only the running notice shows.
[<ReactComponent>]
let ArcSyncNotice
    (isSyncing: bool, failedMessage: string option, replacedUnsaved: string[], dismissReplaced: unit -> unit)
    =
    if isSyncing then
        Html.div [
            prop.testId "arc-sync-running"
            prop.role "status"
            prop.className "swt:alert swt:mx-1 swt:px-2 swt:py-1 swt:text-xs"
            prop.children [
                Html.i [
                    prop.className "swt:iconify swt:fluent--arrow-sync-24-regular swt:size-4 swt:shrink-0"
                ]
                Html.span [ prop.text "External changes detected, waiting…" ]
            ]
        ]
    else
        React.Fragment [
            match failedMessage with
            | Some message ->
                Html.div [
                    prop.testId "arc-sync-failed"
                    prop.role "status"
                    prop.className
                        "swt:alert swt:alert-warning swt:mx-1 swt:px-2 swt:py-1 swt:text-xs swt:max-h-24 swt:overflow-y-auto swt:shrink-0"
                    prop.children [
                        Html.i [
                            prop.className "swt:iconify swt:fluent--warning-24-regular swt:size-4 swt:shrink-0"
                        ]
                        Html.span [
                            prop.text (
                                "Could not load external changes, so memory is behind the files on disk. "
                                + message
                            )
                        ]
                    ]
                ]
            | None -> ()

            if replacedUnsaved.Length > 0 then
                Html.div [
                    prop.testId "arc-sync-replaced"
                    prop.role "status"
                    prop.className
                        "swt:alert swt:alert-warning swt:mx-1 swt:px-2 swt:py-1 swt:text-xs swt:max-h-24 swt:overflow-y-auto swt:shrink-0"
                    prop.children [
                        Html.i [
                            prop.className "swt:iconify swt:fluent--warning-24-regular swt:size-4 swt:shrink-0"
                        ]
                        Html.span [
                            prop.text (
                                "External changes replaced unsaved edits in: "
                                + String.concat ", " replacedUnsaved
                            )
                        ]
                        Html.button [
                            prop.className "swt:btn swt:btn-ghost swt:btn-xs swt:btn-square"
                            prop.ariaLabel "Dismiss"
                            prop.onClick (fun _ -> dismissReplaced ())
                            prop.children [
                                Html.i [
                                    prop.className "swt:iconify swt:fluent--dismiss-24-regular swt:size-4"
                                ]
                            ]
                        ]
                    ]
                ]
        ]

/// React 19 passes the boolean inert property to the DOM, which blocks pointer and keyboard input in the subtree.
let private inertWhile (isInert: bool) : IReactProperty = prop.custom ("inert", isInert)

/// Shows the sync notice above the page and blocks input while a sync runs. The structure is the same in every state,
/// so a change of the sync state does not unmount the page. Main stays a memo component with props only.
[<ReactComponent>]
let ArcSyncArea (children: ReactElement) =
    let sync = Renderer.Context.ArcSyncContext.useArcSyncCtx ()

    Html.div [
        prop.className "swt:size-full swt:min-w-0 swt:min-h-0 swt:flex swt:flex-col"
        prop.children [
            ArcSyncNotice(
                sync.state.IsSyncing,
                sync.state.FailedMessage,
                sync.state.ReplacedUnsaved,
                sync.dismissReplaced
            )
            Html.div [
                prop.className "swt:flex-1 swt:min-w-0 swt:min-h-0"
                inertWhile sync.state.IsSyncing
                prop.children [ children ]
            ]
        ]
    ]
