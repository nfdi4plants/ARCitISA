module Renderer.Components.MainContent.Types


open Arcitisa.Components.Shared
open Arcitisa.Electron.Shared
open Renderer

type ArcTargetProps = {
    AppState: ArcRootPath
    PageState: PageState option
}
