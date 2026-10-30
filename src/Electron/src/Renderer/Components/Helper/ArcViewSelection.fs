module Renderer.Components.Helper.ArcViewSelection

open Swate.Components.Shared
open Swate.Electron.Shared.FileIOHelper

// DUCK TAPE: Keep this pure transformation outside ArcViewHelper so its unit test does not eagerly initialize
// every Electron IPC proxy through Api.fs. The preferred follow-up is an injected `openViewWith openFile path`
// core function with `openView` as a thin Electron adapter; Api.fs can then be split by domain separately.
let applyRequestedPathView (requestedPath: string) (pageState: Renderer.Types.PageState) =
    match PathHelpers.getNameFromPath requestedPath, pageState with
    | requestedFileName, Renderer.Types.PageState.ArcFilePage(arcFile, _) when
        PathHelpers.pathsEqual requestedFileName ARCtrl.ArcPathHelper.DatamapFileName
        ->
        Renderer.Types.PageState.ArcFilePage(arcFile, Some Swate.Components.Page.ArcFileEditor.Types.ActiveView.DataMap)
    | _ -> pageState
