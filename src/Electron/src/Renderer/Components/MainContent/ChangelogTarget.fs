module Renderer.Components.MainContent.ChangelogTarget

open Feliz

let private fetchReleases () =
    Arcitisa.Components.Api.GitHubReleases.loadAll Arcitisa.Electron.Shared.IPCTypes.arcitisaReleaseRepository

[<ReactComponent(true)>]
let ChangelogTarget (version: string) =
    let pageStateCtx = Renderer.Context.PageStateContext.usePageStateCtx ()

    Html.div [
        prop.className "swt:flex swt:size-full swt:min-h-0 swt:min-w-0 swt:flex-col"
        prop.children [
            Html.div [
                prop.className
                    "swt:flex swt:items-center swt:justify-end swt:shrink-0 swt:border-b swt:border-base-content/10 swt:bg-base-100 swt:px-4 swt:py-2"
                prop.children [
                    Html.button [
                        prop.type'.button
                        prop.className "swt:btn swt:btn-ghost swt:btn-sm swt:gap-2 swt:normal-case"
                        prop.ariaLabel "Close changelog"
                        prop.onClick (fun _ -> pageStateCtx.setState None)
                        prop.children [
                            Html.span [
                                prop.className "swt:iconify swt:fluent--dismiss-24-regular swt:size-4"
                                prop.ariaHidden true
                            ]
                            Html.span "Close"
                        ]
                    ]
                ]
            ]
            Html.div [
                prop.className "swt:flex swt:flex-1 swt:min-h-0 swt:min-w-0"
                prop.children [
                    Arcitisa.Components.Page.Changelog.Changelog.Changelog(fetchReleases, version)
                ]
            ]
        ]
    ]
