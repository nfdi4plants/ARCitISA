module Renderer.Components.Helper.ArcViewHelper

open Fable.Core
open Renderer.Components.Helper.ArcViewSelection
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOHelper

let private loadViewResult (previewPath: string) : JS.Promise<Result<Renderer.Types.PageState, string>> = promise {
    let! result = Api.ipcArcVaultApi.openFile previewPath

    return
        result
        |> Result.map (fun opened -> Renderer.Types.PageState.fromFileContentDTO (opened.content, opened.revision))
        |> Result.mapError _.Message
}

let openView (path: string) : JS.Promise<Result<Renderer.Types.PageState, string>> = promise {
    let previewPath = resolveArcPreviewPath path
    let! result = loadViewResult previewPath
    return result |> Result.map (applyRequestedPathView path)
}
