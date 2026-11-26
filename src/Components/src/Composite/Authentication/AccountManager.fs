namespace Arcitisa.Components.Composite.Authentication

open Fable.Core
open Feliz

open Types

/// The removal waiting for an answer. Regenerate means a new token is generated for the account afterwards.
[<RequireQualifiedAccess>]
type internal PendingRemoval =
    | Account of localArcitisaAccountId: string * regenerate: bool
    | AllAccounts

[<Erase; Mangle(false)>]
type AccountManager =

    [<ReactComponent>]
    static member private AccountRow
        (
            account: AccountSummary,
            isActive: bool,
            onRegenerateToken: AccountSummary -> unit,
            ?askRemoval: AccountSummary -> bool -> unit,
            ?onRotateToken: string -> unit,
            ?onSwitch: string -> unit
        ) =

        let isInvalid = account.TokenStatus = TokenStatus.Invalid
        let isExpiring = account.TokenStatus = TokenStatus.Expiring

        Html.div [
            prop.key account.User.LocalArcitisaAccountId
            prop.testId $"AccountRow-{account.User.LocalArcitisaAccountId}"
            prop.className [
                "swt:flex swt:items-center swt:justify-between swt:gap-2 swt:p-1.5 swt:rounded swt:text-sm"
                if isActive then
                    "swt:bg-primary/10"
                if isInvalid then
                    "swt:ring-1 swt:ring-error/40"
                elif isExpiring then
                    "swt:ring-1 swt:ring-warning/40"
            ]
            prop.children [
                Html.div [
                    prop.className "swt:flex swt:items-center swt:gap-2 swt:overflow-hidden"
                    prop.children [
                        Html.a [
                            prop.testId $"AccountProfileLink-{account.User.LocalArcitisaAccountId}"
                            prop.className "swt:shrink-0 swt:btn swt:btn-square swt:btn-ghost"
                            prop.href (Helper.GitLabUrls.profileUrl account.User)
                            prop.target.blank
                            prop.rel "noopener noreferrer"
                            prop.title "Open user profile"
                            prop.children [
                                Html.img [
                                    prop.className "swt:w-6 swt:h-6 swt:rounded-full swt:shrink-0"
                                    prop.src account.User.AvatarUrl
                                    prop.alt account.User.Name
                                ]
                            ]
                        ]
                        Html.div [
                            prop.className "swt:flex swt:flex-col swt:overflow-hidden"
                            prop.children [
                                Html.span [
                                    prop.className "swt:truncate swt:text-sm"
                                    prop.text account.User.Name
                                ]
                                Html.span [
                                    prop.className "swt:truncate swt:text-xs swt:text-base-content/60"
                                    prop.text account.User.TargetDataHub
                                ]
                                match account.TokenExpiresOn with
                                | Some expiresOn ->
                                    Html.span [
                                        prop.className "swt:truncate swt:text-xs swt:text-base-content/70"
                                        prop.text $"Token expires on {expiresOn}"
                                    ]
                                | None -> ()

                                if isInvalid then
                                    Html.div [
                                        prop.className "swt:flex swt:items-center swt:gap-1 swt:text-xs swt:text-error"
                                        prop.children [
                                            Html.span [
                                                prop.className "swt:iconify swt:fluent--warning-24-regular swt:size-4"
                                            ]
                                            Html.span [ prop.text "Token invalid" ]
                                            Html.a [
                                                prop.testId $"RegenerateTokenLink-{account.User.LocalArcitisaAccountId}"
                                                prop.className "swt:link swt:link-error"
                                                prop.href (
                                                    Helper.GitLabUrls.prefillGitLabPATScopes account.User.TargetDataHub
                                                )
                                                prop.onClick (fun e ->
                                                    e.stopPropagation ()

                                                    // With a removal to ask about, GitLab opens after the answer.
                                                    match askRemoval with
                                                    | Some ask ->
                                                        e.preventDefault ()
                                                        ask account true
                                                    | None -> onRegenerateToken account
                                                )
                                                prop.title
                                                    "Regenerate token with correct scopes. You will have to add the account again after regenerating."
                                                prop.ariaLabel
                                                    "Regenerate token with correct scopes. You will have to add the account again after regenerating."
                                                prop.target.blank
                                                prop.rel "noopener noreferrer"
                                                prop.text "Regenerate token"
                                            ]
                                        ]
                                    ]
                                elif isExpiring then
                                    Html.div [
                                        prop.className
                                            "swt:flex swt:items-center swt:gap-1 swt:text-xs swt:text-warning"
                                        prop.children [
                                            Html.span [
                                                prop.className "swt:iconify swt:fluent--warning-24-regular swt:size-4"
                                            ]
                                            Html.span [ prop.text "Token expiring" ]
                                            match onRotateToken with
                                            | Some rotateToken ->
                                                Html.button [
                                                    prop.title "Refresh token to extend expiration."
                                                    prop.ariaLabel "Refresh token to extend expiration."
                                                    prop.testId
                                                        $"RotateTokenButton-{account.User.LocalArcitisaAccountId}"
                                                    prop.className "swt:link swt:link-warning"
                                                    prop.onClick (fun e ->
                                                        e.stopPropagation ()
                                                        rotateToken account.User.LocalArcitisaAccountId
                                                    )
                                                    prop.text "Refresh token"
                                                ]
                                            | None -> ()
                                        ]
                                    ]
                            ]
                        ]
                    ]
                ]
                Html.div [
                    prop.className "swt:flex swt:gap-0.5 swt:shrink-0"
                    prop.children [
                        match onSwitch with
                        | Some switchFn when not isActive ->
                            Html.button [
                                prop.testId $"UseAccountButton-{account.User.LocalArcitisaAccountId}"
                                prop.className "swt:btn swt:btn-xs swt:btn-ghost"
                                prop.title "Switch to this account"
                                prop.text "Use"
                                prop.onClick (fun _ -> switchFn account.User.LocalArcitisaAccountId)
                            ]
                        | _ -> ()
                        match askRemoval with
                        | Some ask ->
                            Html.button [
                                prop.testId $"RemoveAccountButton-{account.User.LocalArcitisaAccountId}"
                                prop.className "swt:btn swt:btn-xs swt:btn-ghost swt:text-error"
                                prop.title "Remove this account"
                                prop.text "✕"
                                prop.onClick (fun _ -> ask account false)
                            ]
                        | None -> ()
                    ]
                ]
            ]
        ]

    /// Asks whether the tokens of the accounts to remove are deleted on their DataHUBs as well. It takes the place
    /// of what was clicked, so the question shows up where the user looks.
    [<ReactComponent>]
    static member private RemovalPrompt
        (removedAccounts: AccountSummary[], onAnswer: bool -> unit, onCancel: unit -> unit)
        =
        let question, tokens =
            match removedAccounts with
            | [| account |] ->
                $"Do you want to delete the token of {account.User.Name} on {account.User.TargetDataHub} as well?",
                "token"
            | _ ->
                $"Do you want to delete the tokens of all {removedAccounts.Length} accounts on their DataHUBs as well?",
                "tokens"

        Html.div [
            prop.testId "RemoveAccountPrompt"
            prop.className "swt:flex swt:flex-col swt:gap-2 swt:p-1.5 swt:rounded swt:ring-1 swt:ring-error/40"
            prop.children [
                Html.p [ prop.className "swt:text-sm"; prop.text question ]
                Html.div [
                    prop.className "swt:flex swt:flex-wrap swt:gap-1"
                    prop.children [
                        Html.button [
                            prop.testId "RemoveAccountDeleteToken"
                            prop.className "swt:btn swt:btn-sm swt:btn-error"
                            prop.text $"Delete {tokens}"
                            prop.onClick (fun _ -> onAnswer true)
                        ]
                        Html.button [
                            prop.testId "RemoveAccountKeepToken"
                            prop.className "swt:btn swt:btn-sm"
                            prop.text $"Keep {tokens}"
                            prop.onClick (fun _ -> onAnswer false)
                        ]
                        Html.button [
                            prop.testId "RemoveAccountCancel"
                            prop.className "swt:btn swt:btn-sm swt:btn-ghost"
                            prop.text "Cancel"
                            prop.onClick (fun _ -> onCancel ())
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent(true)>]
    static member Main
        (
            accounts: AuthStateDto,
            onRegenerateToken: AccountSummary -> unit,
            ?onRotateToken: string -> unit,
            ?onSwitchAccount: string -> unit,
            ?onRemoveAccounts: RemoveAccountsRequest -> unit
        ) =
        let activeLocalArcitisaAccountId =
            accounts.ActiveAccount
            |> Option.map (fun account -> account.User.LocalArcitisaAccountId)

        let pendingRemoval, setPendingRemoval = React.useState (None: PendingRemoval option)

        let askRemoval (account: AccountSummary) (regenerate: bool) =
            setPendingRemoval (Some(PendingRemoval.Account(account.User.LocalArcitisaAccountId, regenerate)))

        let cancel () = setPendingRemoval None

        let removeAccounts (removedAccounts: AccountSummary[]) (deleteTokensOnDataHub: bool) =
            setPendingRemoval None

            onRemoveAccounts
            |> Option.iter (fun removeFn ->
                removeFn {
                    LocalArcitisaAccountIds = removedAccounts |> Array.map _.User.LocalArcitisaAccountId
                    DeleteTokensOnDataHub = deleteTokensOnDataHub
                }
            )

        let removeAccount (account: AccountSummary) (regenerate: bool) (deleteTokenOnDataHub: bool) =
            removeAccounts [| account |] deleteTokenOnDataHub

            if regenerate then
                Browser.Dom.window.``open`` (
                    Helper.GitLabUrls.prefillGitLabPATScopes account.User.TargetDataHub,
                    "_blank"
                )
                |> ignore

                onRegenerateToken account

        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-1 swt:mt-1 swt:pt-1 swt:border-t swt:border-base-content/10"
            prop.children [
                if accounts.StoredAccounts.Length > 1 then
                    Html.div [
                        prop.className "swt:text-xs swt:text-base-content/60 swt:px-1"
                        prop.textf "Accounts (%d)" accounts.StoredAccounts.Length
                    ]

                for acct in accounts.StoredAccounts do
                    match pendingRemoval with
                    | Some(PendingRemoval.Account(localArcitisaAccountId, regenerate)) when
                        localArcitisaAccountId = acct.User.LocalArcitisaAccountId
                        ->
                        AccountManager.RemovalPrompt([| acct |], removeAccount acct regenerate, cancel)
                    | _ ->
                        let isActive = activeLocalArcitisaAccountId = Some acct.User.LocalArcitisaAccountId

                        AccountManager.AccountRow(
                            acct,
                            isActive,
                            onRegenerateToken = onRegenerateToken,
                            ?askRemoval = (onRemoveAccounts |> Option.map (fun _ -> askRemoval)),
                            ?onRotateToken = onRotateToken,
                            ?onSwitch = onSwitchAccount
                        )

                if onRemoveAccounts.IsSome then
                    match pendingRemoval with
                    | Some PendingRemoval.AllAccounts ->
                        AccountManager.RemovalPrompt(
                            accounts.StoredAccounts,
                            removeAccounts accounts.StoredAccounts,
                            cancel
                        )
                    | _ ->
                        Html.button [
                            prop.testId "RemoveAllAccountsButton"
                            prop.className "swt:btn swt:btn-error swt:btn-outline swt:btn-sm swt:w-full swt:mt-1"
                            prop.text "Remove all logins"
                            prop.onClick (fun _ -> setPendingRemoval (Some PendingRemoval.AllAccounts))
                        ]
            ]
        ]
