module Renderer.Components.MainContent.ArcSyncNotice

open Feliz
open Renderer.Context.ArcSyncContext
open Swate.Components.Primitive.Banner.Types

/// Entries for the window's layout banner slot: a failed load, and the files whose unsaved edits a load replaced.
/// A running load has no banner, because a banner that comes and goes with every short load makes the window jump.
/// The wait cursor over the page and the save button show it instead.
let bannerEntries (state: ArcSyncState) (dismissReplaced: unit -> unit) : BannerEntry[] = [|
    match state.FailedMessage with
    | Some message ->
        BannerEntry.create (
            "arc-sync-failed",
            "Could not load external changes, so memory is behind the files on disk. "
            + message,
            variant = BannerVariant.Warning
        )
    | None -> ()

    if state.ReplacedUnsaved.Length > 0 then
        BannerEntry.create (
            "arc-sync-replaced",
            "External changes replaced unsaved edits in: "
            + String.concat ", " state.ReplacedUnsaved,
            variant = BannerVariant.Warning,
            onClose = dismissReplaced
        )
|]

[<Hook>]
let useBannerEntries () =
    let sync = useArcSyncCtx ()
    bannerEntries sync.state sync.dismissReplaced

/// React 19 passes the boolean inert property to the DOM, which blocks pointer and keyboard input in the subtree.
let private inertWhile (isInert: bool) : IReactProperty = prop.custom ("inert", isInert)

/// Blocks input to the page while a load runs and shows the wait cursor over it. The structure is the same in every
/// state, so a change of the sync state does not unmount the page. Nothing on screen announces the load, so a status
/// that only screen readers see does.
[<ReactComponent>]
let ArcSyncArea (children: ReactElement) =
    let sync = useArcSyncCtx ()
    let isSyncing = sync.state.IsSyncing

    // The pointer passes through inert content, so the cursor sits on the wrapper around it.
    Html.div [
        prop.testId "arc-sync-area"
        prop.className [
            "swt:size-full swt:min-w-0 swt:min-h-0"
            if isSyncing then
                "swt:cursor-wait"
        ]
        prop.children [
            Html.span [
                prop.testId "arc-sync-status"
                prop.role "status"
                prop.className "swt:sr-only"
                prop.text (if isSyncing then "Loading external changes…" else "")
            ]
            Html.div [
                prop.className "swt:size-full swt:min-w-0 swt:min-h-0"
                inertWhile isSyncing
                prop.children [ children ]
            ]
        ]
    ]
