module Renderer.Components.MainContent.GitMergeConflictTarget

open Fable.Core
open Feliz
open Renderer.Types
open Arcitisa.Electron.Shared.VersionControlTypes

[<ReactComponent>]
let Main (mergeData: VersionControlConflictPage) =

    let gitStateCtx = Renderer.Context.GitStateContext.useGitStateCtx ()

    let isConfirmingCurrentPath =
        gitStateCtx.state.MergeResolutionPendingPath = Some mergeData.Path

    let isBusy = gitStateCtx.state.BusyOperation.IsSome

    // The request carries the handle and the workspace token the page was loaded with,
    // so the main process refuses the resolution when either moved on since.
    let confirmMergeResolution resolvedContent =
        if isBusy then
            ()
        else
            gitStateCtx.confirmMergeResolution {
                Path = mergeData.Path
                Handle = mergeData.Handle
                WorkspaceVersion = mergeData.WorkspaceVersion
                Resolution = ConflictResolutionDto.SupplyResolvedContent resolvedContent
            }

    Html.div [
        prop.className "swt:relative swt:h-full swt:w-full swt:min-h-0"
        prop.children [
            if isConfirmingCurrentPath then
                Html.div [
                    prop.className
                        "swt:border-b swt:border-base-content/10 swt:bg-base-200/70 swt:px-4 swt:py-2 swt:text-sm swt:text-base-content/70"
                    prop.text "Applying merge resolution..."
                ]

            Arcitisa.Components.Page.GitMergeConflictViewer.Viewer(
                mergeConflictContent = mergeData.ConflictContent,
                currentTitle = mergeData.Path,
                resolvedTitle = mergeData.Path,
                onConfirmMerge = confirmMergeResolution,
                confirmDisabled = isBusy,
                testIdPrefix = "renderer-git-merge"
            )
            Html.div [
                prop.className "swt:absolute swt:right-4 swt:top-2 swt:z-10"
                prop.children [
                    Renderer.Components.Helper.GitMergeAbandonConfirmation.Main(
                        isBusy,
                        gitStateCtx.abandonMerge,
                        "renderer-git-merge"
                    )
                ]
            ]
        ]
    ]
