module Renderer.Components.VersionControlDependencyBanner

open Feliz
open Renderer.Context.VersionControlDependencyContext
open Swate.Components.Primitive.Banner.Types
open Swate.Electron.Shared.VersionControlTypes

type BannerContent = {
    Message: string
    /// A check runs already, so a second request would only queue another run.
    IsChecking: bool
}

/// The banner appears while the latest check found a problem. It stays during a new check, so the
/// warning does not flicker away and back when the user checks again. Nothing shows until a check has
/// found a problem. It names each problem in one line and leaves the full text to the settings.
let bannerContent (state: VersionControlDependencyStateDto option) : BannerContent option =
    state
    |> Option.bind (fun state ->
        stateProblemSummary state
        |> Option.map (fun message -> {
            Message = message
            IsChecking = state.IsChecking
        })
    )

/// The banner entry for Layout.Main, or None while there is nothing to show.
[<Hook>]
let useBannerEntry () : BannerEntry option =
    let dependencies = useVersionControlDependencyCtx ()
    let pageStateCtx = Renderer.Context.PageStateContext.usePageStateCtx ()

    bannerContent dependencies.state
    |> Option.map (fun content ->
        BannerEntry.create (
            "VersionControlDependencyBanner",
            content.Message,
            variant = BannerVariant.Warning,
            actions = [|
                BannerAction.create (
                    "Open settings",
                    (fun () -> pageStateCtx.setState (Some Renderer.Types.PageState.SettingsPage)),
                    icon = "swt:fluent--settings-24-regular"
                )
                BannerAction.create (
                    "Check again",
                    dependencies.recheck,
                    icon = "swt:fluent--arrow-sync-24-regular",
                    disabled = content.IsChecking
                )
            |]
        )
    )
