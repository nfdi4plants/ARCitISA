namespace Arcitisa.Components.Page.ChangelogSample

open Fable.Core
open Feliz
open Arcitisa.Components.Api.GitHubReleases
open Arcitisa.Components.Page.Changelog

module internal Fixtures =

    let loadFetch () = promise {
        do! Promise.sleep 750

        return [|
            {
                tag_name = "v1.1.0"
                name = Some "Version 1.1.0"
                body = Some "## Improvements\n\n- Faster startup\n- Improved release navigation"
                draft = false
                prerelease = false
            }
            {
                tag_name = "v1.0.0"
                name = Some "Version 1.0.0"
                body =
                    Some
                        "## Welcome\n\nThe first release.\n\n### Features\n\n- **Markdown** release notes\n- Version selection"
                draft = false
                prerelease = false
            }
        |]
    }

[<Erase; Mangle(false)>]
type internal ChangelogSample =

    [<ReactComponent(true)>]
    static member ChangelogSample(?currentRelease: string) =
        let repository, setRepository = React.useState ""
        let useGitHub, setUseGitHub = React.useState false

        let validRepository =
            System.Text.RegularExpressions.Regex.IsMatch(repository.Trim(), @"^[A-Za-z0-9-]+/[A-Za-z0-9_.-]+$")

        // Editing the repository returns to mock mode; enabling the toggle applies it.
        let activeRepository =
            if useGitHub && validRepository then
                Some(repository.Trim())
            else
                None

        let fetchReleases =
            React.useCallback (
                (fun () ->
                    match activeRepository with
                    | Some repository -> loadAll repository
                    | None -> Fixtures.loadFetch ()
                ),
                [| box activeRepository |]
            )

        Html.div [
            prop.className "swt:flex swt:flex-col swt:h-screen"
            prop.children [
                Html.div [
                    prop.className "swt:flex swt:flex-wrap swt:items-center swt:gap-4 swt:p-4 swt:bg-base-200"
                    prop.children [
                        Html.label [
                            prop.className "swt:flex swt:items-center swt:gap-2"
                            prop.children [
                                Html.span "GitHub repository"
                                Html.input [
                                    prop.className "swt:input swt:input-bordered"
                                    prop.type'.text
                                    prop.placeholder "owner/repo_name"
                                    prop.value repository
                                    prop.onChange (fun (value: string) ->
                                        setUseGitHub false
                                        setRepository value
                                    )
                                ]
                            ]
                        ]
                        Html.button [
                            prop.className "swt:btn swt:btn-primary"
                            prop.text "Try ARCitISA Repository"
                            prop.onClick (fun _ ->
                                Browser.Dom.console.log ("Trying ARCitISA Repository")
                                setUseGitHub true
                                setRepository "nfdi4plants/ARCitISA"
                            )
                        ]
                        Html.label [
                            prop.className "swt:flex swt:items-center swt:gap-2"
                            prop.children [
                                Html.input [
                                    prop.className "swt:toggle"
                                    prop.type'.checkbox
                                    prop.isChecked useGitHub
                                    prop.disabled (not validRepository)
                                    prop.onChange (fun (value: bool) -> setUseGitHub value)
                                ]
                                Html.span "Use GitHub releases"
                            ]
                        ]
                        Html.span (
                            if useGitHub then
                                "Live GitHub releases"
                            else
                                "Mock releases (750 ms delay)"
                        )
                    ]
                ]
                Changelog.Changelog(fetchReleases, defaultArg currentRelease "1.0.0")
            ]
        ]
