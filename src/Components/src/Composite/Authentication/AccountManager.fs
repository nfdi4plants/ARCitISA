namespace Swate.Components.Composite.Authentication

open Fable.Core
open Feliz

open Types

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
            prop.key account.User.LocalSwateAccountId
            prop.testId $"AccountRow-{account.User.LocalSwateAccountId}"
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
                            prop.testId $"AccountProfileLink-{account.User.LocalSwateAccountId}"
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
                                                prop.testId $"RegenerateTokenLink-{account.User.LocalSwateAccountId}"
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
                                                    prop.testId $"RotateTokenButton-{account.User.LocalSwateAccountId}"
                                                    prop.className "swt:link swt:link-warning"
                                                    prop.onClick (fun e ->
                                                        e.stopPropagation ()
                                                        rotateToken account.User.LocalSwateAccountId
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
                                prop.testId $"UseAccountButton-{account.User.LocalSwateAccountId}"
                                prop.className "swt:btn swt:btn-xs swt:btn-ghost"
                                prop.title "Switch to this account"
                                prop.text "Use"
                                prop.onClick (fun _ -> switchFn account.User.LocalSwateAccountId)
                            ]
                        | _ -> ()
                        match askRemoval with
                        | Some ask ->
                            Html.button [
                                prop.testId $"RemoveAccountButton-{account.User.LocalSwateAccountId}"
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

    /// Asks whether the token of a removed account is deleted on its DataHUB as well.
    [<ReactComponent>]
    static member private RemovalPrompt(account: AccountSummary, onAnswer: bool -> unit, onCancel: unit -> unit) =
        Html.div [
            prop.testId "RemoveAccountPrompt"
            prop.className "swt:flex swt:flex-col swt:gap-2 swt:p-1.5"
            prop.children [
                Html.p [
                    prop.className "swt:text-sm"
                    prop.text
                        $"Do you want to delete the token of {account.User.Name} on {account.User.TargetDataHub} as well?"
                ]
                Html.div [
                    prop.className "swt:flex swt:flex-wrap swt:gap-1"
                    prop.children [
                        Html.button [
                            prop.testId "RemoveAccountDeleteToken"
                            prop.className "swt:btn swt:btn-sm swt:btn-error"
                            prop.text "Delete token"
                            prop.onClick (fun _ -> onAnswer true)
                        ]
                        Html.button [
                            prop.testId "RemoveAccountKeepToken"
                            prop.className "swt:btn swt:btn-sm"
                            prop.text "Keep token"
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
            ?onRemoveAccount: RemoveAccountRequest -> unit
        ) =
        let activeLocalSwateAccountId =
            accounts.ActiveAccount
            |> Option.map (fun account -> account.User.LocalSwateAccountId)

        // The account waiting for the answer, and whether a new token is generated for it afterwards.
        let pendingRemoval, setPendingRemoval =
            React.useState (None: (AccountSummary * bool) option)

        let askRemoval (account: AccountSummary) (regenerate: bool) =
            setPendingRemoval (Some(account, regenerate))

        let remove (account: AccountSummary) (regenerate: bool) (deleteTokenOnDataHub: bool) =
            setPendingRemoval None

            onRemoveAccount
            |> Option.iter (fun removeFn ->
                removeFn {
                    LocalSwateAccountId = account.User.LocalSwateAccountId
                    DeleteTokenOnDataHub = deleteTokenOnDataHub
                }
            )

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
                match pendingRemoval with
                | Some(account, regenerate) ->
                    AccountManager.RemovalPrompt(account, remove account regenerate, fun () -> setPendingRemoval None)
                | None ->
                    if accounts.StoredAccounts.Length > 1 then
                        Html.div [
                            prop.className "swt:text-xs swt:text-base-content/60 swt:px-1"
                            prop.textf "Accounts (%d)" accounts.StoredAccounts.Length
                        ]

                    for acct in accounts.StoredAccounts do
                        let isActive = activeLocalSwateAccountId = Some acct.User.LocalSwateAccountId

                        AccountManager.AccountRow(
                            acct,
                            isActive,
                            onRegenerateToken = onRegenerateToken,
                            ?askRemoval = (onRemoveAccount |> Option.map (fun _ -> askRemoval)),
                            ?onRotateToken = onRotateToken,
                            ?onSwitch = onSwitchAccount
                        )
            ]
        ]
