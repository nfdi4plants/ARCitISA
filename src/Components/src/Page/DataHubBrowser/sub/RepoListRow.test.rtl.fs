module internal Arcitisa.Components.Tests.DataHubBrowser.RepoListRow

open Feliz
open Arcitisa.Components.Page.DataHubBrowser.sub
open Arcitisa.Components.Page.MockData.DataHub
open Vitest

Vitest.afterEach (fun () -> RTL.cleanup ())

Vitest.test (
    "shows repository initials when its avatar image fails",
    fun () ->
        let project = {
            mostStarred.[0] with
                id = 42
                name = "Plant Atlas"
                avatar_url = Some "https://example.invalid/broken.png"
        }

        RTL.render (Html.ul [ RepoListRow.RepoListRow(project) ]) |> ignore

        RTL.screen.getByTestId "GitLabRepoRow-42" |> ignore
        Vitest.expect(RTL.screen.queryByTestId "GitLabRepoAvatarInitials-42" |> Option.isNone).toBe true

        let avatar = RTL.screen.getByTestId "GitLabRepoAvatarImage-42"
        RTL.fireEvent.custom ("error", avatar)

        let fallback = RTL.screen.getByTestId "GitLabRepoAvatarInitials-42"
        Vitest.expect(fallback.textContent).toBe "PLA"
)
