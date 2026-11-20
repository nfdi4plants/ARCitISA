module internal Swate.Components.Composite.Authentication.AccountManagerTests

open Fable.Core
open Vitest
open Swate.Components.Composite.Authentication.Types

/// Replaces window.open, which jsdom lacks, and records the opened addresses.
[<Emit("$0.spyOn(window, 'open').mockImplementation(() => null)")>]
let private stubWindowOpen (vi: obj) : obj = jsNative

[<Emit("$0.mock.calls.map(call => call[0])")>]
let private openedUrls (spy: obj) : string[] = jsNative

[<Emit("$0.mockRestore()")>]
let private restore (spy: obj) : unit = jsNative

Vitest.afterEach (fun () -> RTL.cleanup ())

let private account (id: string) (tokenStatus: TokenStatus) : AccountSummary = {
    User = {
        Id = 1
        LocalSwateAccountId = id
        Username = id
        Name = id
        Email = $"{id}@example.org"
        CommitEmail = None
        AvatarUrl = ""
        TargetDataHub = "https://gitlab.example.org"
    }
    DateAdded = "2026-01-01T00:00:00.0000000Z"
    TokenStatus = tokenStatus
    TokenExpiresOn = None
}

let private accounts: AuthStateDto = {
    ActiveAccount = Some(account "ok" TokenStatus.Ok)
    StoredAccounts = [|
        account "ok" TokenStatus.Ok
        account "broken" TokenStatus.Invalid
    |]
}

/// Renders the account list and records every removal and regeneration.
let private render () =
    let removals = ResizeArray<RemoveAccountsRequest>()
    let regenerated = ResizeArray<string>()

    RTL.render (
        AccountManager.Main(
            accounts,
            onRegenerateToken = (fun account -> regenerated.Add account.User.LocalSwateAccountId),
            onRemoveAccounts = removals.Add
        )
    )
    |> ignore

    removals, regenerated

let private click (testId: string) =
    RTL.fireEvent.click (RTL.screen.getByTestId testId)

let private present (testId: string) =
    (RTL.screen.queryByTestId testId).IsSome

Vitest.describe (
    "AccountManager removal prompt",
    fun () ->
        Vitest.test (
            "removing asks first and passes the answer on",
            fun () ->
                for answer, deleteToken in
                    [
                        "RemoveAccountDeleteToken", true
                        "RemoveAccountKeepToken", false
                    ] do
                    let removals, regenerated = render ()

                    click "RemoveAccountButton-ok"
                    Vitest.expect(removals.Count).toBe 0
                    // The question takes the place of the clicked account. The other accounts stay.
                    Vitest.expect(present "AccountRow-ok").toBe false
                    Vitest.expect(present "AccountRow-broken").toBe true

                    click answer

                    Vitest.expect(Array.ofSeq removals).toEqual [|
                        {
                            LocalSwateAccountIds = [| "ok" |]
                            DeleteTokensOnDataHub = deleteToken
                        }
                    |]

                    Vitest.expect(regenerated.Count).toBe 0
                    Vitest.expect(present "RemoveAccountPrompt").toBe false
                    RTL.cleanup ()
        )

        Vitest.test (
            "regenerating a token asks the same and opens GitLab and the sign-in after the answer",
            fun () ->
                let opened = stubWindowOpen Vitest.vi
                let removals, regenerated = render ()

                click "RegenerateTokenLink-broken"
                Vitest.expect(regenerated.Count).toBe 0
                Vitest.expect(openedUrls opened).toEqual [||]

                click "RemoveAccountDeleteToken"

                Vitest.expect(openedUrls opened).toEqual [|
                    Helper.GitLabUrls.prefillGitLabPATScopes "https://gitlab.example.org"
                |]

                restore opened

                Vitest.expect(Array.ofSeq removals).toEqual [|
                    {
                        LocalSwateAccountIds = [| "broken" |]
                        DeleteTokensOnDataHub = true
                    }
                |]

                Vitest.expect(Array.ofSeq regenerated).toEqual [| "broken" |]
        )

        Vitest.test (
            "cancel removes nothing, opens nothing and shows the accounts again",
            fun () ->
                let opened = stubWindowOpen Vitest.vi
                let removals, regenerated = render ()

                click "RegenerateTokenLink-broken"
                click "RemoveAccountCancel"

                Vitest.expect(removals.Count).toBe 0
                Vitest.expect(regenerated.Count).toBe 0
                Vitest.expect(openedUrls opened).toEqual [||]
                restore opened
                Vitest.expect(present "AccountRow-broken").toBe true
        )

        Vitest.test (
            "removing all logins asks once for all accounts and passes the answer on",
            fun () ->
                for answer, deleteTokens in
                    [
                        "RemoveAccountDeleteToken", true
                        "RemoveAccountKeepToken", false
                    ] do
                    let removals, _ = render ()

                    click "RemoveAllAccountsButton"
                    Vitest.expect(removals.Count).toBe 0

                    click answer

                    Vitest.expect(Array.ofSeq removals).toEqual [|
                        {
                            LocalSwateAccountIds = [| "ok"; "broken" |]
                            DeleteTokensOnDataHub = deleteTokens
                        }
                    |]

                    Vitest.expect(present "RemoveAccountPrompt").toBe false
                    RTL.cleanup ()
        )

        Vitest.test (
            "cancel on removing all logins removes nothing",
            fun () ->
                let removals, _ = render ()

                click "RemoveAllAccountsButton"
                click "RemoveAccountCancel"

                Vitest.expect(removals.Count).toBe 0
                Vitest.expect(present "RemoveAllAccountsButton").toBe true
        )
)
