module ElectronCore.AuthServiceTests

open System
open Fable.Core
open Main.Auth
open Arcitisa.Electron.Shared.AuthTypes
open Vitest
open ElectronCore.TestHelpers

[<Emit("new Response($0 == null ? null : JSON.stringify($0), { status: $1 })")>]
let private response (body: obj) (status: int) : obj = jsNative

[<Emit("($0 && $0.method) || 'GET'")>]
let private methodOf (init: obj) : string = jsNative

/// A GitLab stand-in behind the global fetch. Token checks and rotations wait in `Waiting` while held.
type private FakeGitLab = {
    HoldChecks: bool ref
    HoldRotation: bool ref
    Waiting: ResizeArray<unit -> unit>
    /// The display name the user endpoint answers with.
    UserName: string ref
    /// The status a token deletion gets, by DataHUB host.
    DeleteStatus: (string -> int) ref
}

let private tokenAnswer (token: string option) =
    response
        {|
            id = 1
            name = "arcitisa-electron"
            revoked = false
            active = true
            created_at = null
            expires_at = null
            last_used_at = null
            scopes = [| "api" |]
            user_id = 1
            description = null
            token = Option.toObj token
        |}
        200

let private stubGitLab () : FakeGitLab =
    let gitLab = {
        HoldChecks = ref false
        HoldRotation = ref false
        Waiting = ResizeArray()
        UserName = ref "E2E User"
        DeleteStatus = ref (fun _ -> 204)
    }

    let answerOrWait (hold: bool ref) (answer: unit -> obj) =
        if hold.Value then
            Promise.create (fun resolve _ -> gitLab.Waiting.Add(fun () -> resolve (answer ())))
        else
            Promise.lift (answer ())

    let fetch (url: string) (init: obj) : JS.Promise<obj> =
        let host = Uri(url).Host

        if url.EndsWith "/api/v4/user" then
            Promise.lift (
                response
                    {|
                        id = 1
                        username = host
                        name = gitLab.UserName.Value
                        email = $"{host}@example.org"
                        commit_email = null
                        avatar_url = null
                    |}
                    200
            )
        elif url.EndsWith "/api/v4/personal_access_tokens/self/rotate" then
            answerOrWait gitLab.HoldRotation (fun () -> tokenAnswer (Some "rotated-token"))
        elif url.EndsWith "/api/v4/personal_access_tokens/self" && methodOf init = "DELETE" then
            Promise.lift (response null (gitLab.DeleteStatus.Value host))
        elif url.EndsWith "/api/v4/personal_access_tokens/self" then
            answerOrWait gitLab.HoldChecks (fun () -> tokenAnswer None)
        else
            Promise.lift (response null 404)

    Vitest.vi.stubGlobal ("fetch", box (Func<string, obj, JS.Promise<obj>>(fetch)))
    gitLab

/// Waits until a held call arrives.
let private untilWaiting (gitLab: FakeGitLab) = promise {
    while gitLab.Waiting.Count = 0 do
        do! Promise.sleep 5
}

/// Stops holding and answers every waiting call.
let private releaseAll (gitLab: FakeGitLab) =
    gitLab.HoldChecks.Value <- false
    gitLab.HoldRotation.Value <- false
    let waiting = Array.ofSeq gitLab.Waiting
    gitLab.Waiting.Clear()

    for release in waiting do
        release ()

/// Runs the test over an empty credential store in a temporary folder and removes what it signed in.
let private withAuthStore (testBody: unit -> JS.Promise<unit>) : JS.Promise<unit> = promise {
    let! rootPath = createTempDirectoryAsync "arcitisa-auth-service-"
    Vitest.vi.stubEnv ("ARCITISA_TEST_USER_DATA", Some rootPath)

    let cleanUp () = promise {
        AuthService.listAccounts ()
        |> Array.map _.User.LocalArcitisaAccountId
        |> AuthService.removeAccounts
        |> ignore

        Vitest.vi.unstubAllGlobals ()
        Vitest.vi.unstubAllEnvs ()
        do! removeDirectoryAsync rootPath
    }

    try
        do! testBody ()
        do! cleanUp ()
    with error ->
        do! cleanUp ()
        return raise error
}

/// Signs in to the DataHUB and returns the local id of the account.
let private signIn (baseUrl: string) = promise {
    let! result =
        AuthService.signIn {
            GitLabBaseUrl = baseUrl
            PersonalAccessToken = $"token-for-{baseUrl}"
        }

    if not result.Success then
        failwith $"Sign-in to {baseUrl} failed: {result.Message}"

    return
        (AuthService.listAccounts ()
         |> Array.find (fun account -> account.User.TargetDataHub = baseUrl))
            .User.LocalArcitisaAccountId
}

let private storedTokens () =
    SecureAuthStore.loadAll ()
    |> List.map (fun credential -> credential.Metadata.TargetDataHub, credential.Token)
    |> Array.ofList

Vitest.describe (
    "AuthService",
    fun () ->
        Vitest.test (
            "an account removed while its token is checked stays removed, and the others get the result",
            fun () ->
                withAuthStore (fun () -> promise {
                    let gitLab = stubGitLab ()
                    let! _ = signIn "https://kept.example.org"
                    let! removedId = signIn "https://removed.example.org"

                    gitLab.HoldChecks.Value <- true
                    let check = AuthService.revalidate ()
                    do! untilWaiting gitLab

                    AuthService.removeAccounts [ removedId ] |> ignore
                    gitLab.UserName.Value <- "Checked User"
                    releaseAll gitLab
                    let! _ = check

                    let accounts = AuthService.listAccounts ()
                    Vitest.expect(accounts |> Array.map _.User.TargetDataHub).toEqual [| "https://kept.example.org" |]
                    Vitest.expect(accounts |> Array.map _.User.Name).toEqual [| "Checked User" |]

                    Vitest.expect(storedTokens () |> Array.map fst).toEqual [| "https://kept.example.org" |]
                })
        )

        Vitest.test (
            "a token rotated while it is checked stays the new token",
            fun () ->
                withAuthStore (fun () -> promise {
                    let gitLab = stubGitLab ()
                    let! accountId = signIn "https://gitlab.example.org"

                    gitLab.HoldChecks.Value <- true
                    let check = AuthService.revalidate ()
                    do! untilWaiting gitLab

                    let! rotated = AuthService.rotatePersonalAccessToken accountId
                    Vitest.expect(Result.isOk rotated).toBe true

                    releaseAll gitLab
                    let! _ = check

                    Vitest.expect(AuthService.tryGetTokenForAccount accountId).toEqual (Some "rotated-token")
                    Vitest.expect(storedTokens ()).toEqual [| "https://gitlab.example.org", "rotated-token" |]
                })
        )

        Vitest.test (
            "an account removed while its token is rotated stays removed",
            fun () ->
                withAuthStore (fun () -> promise {
                    let gitLab = stubGitLab ()
                    let! accountId = signIn "https://gitlab.example.org"

                    gitLab.HoldRotation.Value <- true
                    let rotation = AuthService.rotatePersonalAccessToken accountId
                    do! untilWaiting gitLab

                    AuthService.removeAccounts [ accountId ] |> ignore
                    releaseAll gitLab
                    let! _ = rotation

                    Vitest.expect(AuthService.listAccounts ()).toEqual [||]
                    Vitest.expect(storedTokens ()).toEqual [||]
                })
        )

        Vitest.test (
            "removing accounts clears only them and their stored tokens and hands back their tokens",
            fun () ->
                withAuthStore (fun () -> promise {
                    let _ = stubGitLab ()
                    let! firstId = signIn "https://first.example.org"
                    let! secondId = signIn "https://second.example.org"
                    let! keptId = signIn "https://kept.example.org"

                    let removed = AuthService.removeAccounts [ firstId; secondId ]

                    Vitest
                        .expect(
                            removed
                            |> List.map (fun (user, token) -> user.TargetDataHub, token)
                            |> List.sort
                        )
                        .toEqual
                        [
                            "https://first.example.org", "token-for-https://first.example.org"
                            "https://second.example.org", "token-for-https://second.example.org"
                        ]

                    Vitest.expect(AuthService.listAccounts () |> Array.map _.User.LocalArcitisaAccountId).toEqual [|
                        keptId
                    |]

                    Vitest.expect(storedTokens () |> Array.map fst).toEqual [| "https://kept.example.org" |]

                    Vitest
                        .expect(
                            (AuthService.getState ()).ActiveAccount
                            |> Option.map _.User.LocalArcitisaAccountId
                        )
                        .toEqual (Some keptId)
                })
        )

        Vitest.test (
            "a token the DataHUB deleted or no longer accepts counts as deleted",
            fun () ->
                withAuthStore (fun () -> promise {
                    let gitLab = stubGitLab ()
                    let! accountId = signIn "https://gitlab.example.org"
                    let removed = AuthService.removeAccounts [ accountId ]

                    for status in [ 204; 401 ] do
                        gitLab.DeleteStatus.Value <- fun _ -> status
                        let! outcome = AuthService.deleteTokens removed
                        Vitest.expect(outcome).toEqual TokenDeletionOutcome.Deleted
                })
        )

        Vitest.test (
            "any other answer reports that the token was not deleted, naming only the DataHUBs that refused",
            fun () ->
                withAuthStore (fun () -> promise {
                    let gitLab = stubGitLab ()
                    let! deletedId = signIn "https://deleted.example.org"
                    let! refusedId = signIn "https://refused.example.org"
                    let removed = AuthService.removeAccounts [ deletedId; refusedId ]

                    for status in [ 403; 404; 500 ] do
                        gitLab.DeleteStatus.Value <- fun host -> if host = "refused.example.org" then status else 204
                        let! outcome = AuthService.deleteTokens removed

                        match outcome with
                        | TokenDeletionOutcome.Failed reason ->
                            Vitest.expect(reason).toContain "https://refused.example.org"
                            Vitest.expect(reason.Contains "https://deleted.example.org").toBe false
                        | other -> failwith $"Status {status} gave {other}."
                })
        )
)
