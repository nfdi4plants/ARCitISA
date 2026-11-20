namespace Swate.Components.Composite.Authentication

open Fable.Core
open Feliz
open Swate.Components
open Swate.Components.Primitive.BaseModal
open Swate.Components.Primitive.Dropdown
open Types
open Helper


[<Erase; Mangle(false)>]
type Authentication =

    [<ReactComponent>]
    static member private AvatarPlaceholder() =
        Html.div [
            prop.className [ "swt:avatar swt:avatar-placeholder swt:size-full" ]
            prop.children [
                Html.div [
                    prop.className "swt:bg-neutral swt:text-neutral-content swt:rounded-full swt:p-2"
                    prop.children [
                        Html.span [
                            prop.className "swt:size-full swt:iconify swt:fluent--person-12-filled"
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member private UserAvatarIcon(userInformation: AuthUserDto) =
        Html.img [
            prop.className "swt:w-8 swt:h-8 swt:rounded-full"
            prop.src userInformation.AvatarUrl
            prop.alt userInformation.Name
        ]


    [<ReactComponent>]
    static member private CustomDataHubRadioItem(activeDataHub, radioid, setDataHub: DataHubInformation -> unit) =

        let customDataHub, setCustomDataHub = React.useState "https://"

        Html.label [
            prop.className "swt:label swt:gap-2"
            prop.children [
                Html.input [
                    prop.testId "CustomDataHubRadio"
                    prop.type'.radio
                    prop.name radioid
                    prop.className "swt:radio swt:radio-sm"
                    prop.isChecked (activeDataHub.Url = customDataHub)
                    prop.onChange (fun (_: bool) -> setDataHub (DataHubInformation.FromUrl customDataHub))
                ]
                Html.input [
                    prop.testId "CustomDataHubInput"
                    prop.className "swt:input swt:input-xs"
                    prop.value customDataHub
                    prop.placeholder "https://"
                    prop.onChange (fun value ->
                        setCustomDataHub value
                        setDataHub (DataHubInformation.FromUrl value)
                    )
                ]
            ]
        ]

    [<ReactComponent>]
    static member private DataHubRadioItem(activeDataHub, radioid, dataHub, setDataHub, ?radioClassName) =
        Html.label [
            prop.className "swt:label swt:gap-2"
            prop.children [
                Html.input [
                    prop.testId ($"DataHubRadio-{dataHub.Name}")
                    prop.type'.radio
                    prop.name radioid
                    prop.id dataHub.Url
                    prop.className (Option.defaultValue "swt:radio swt:radio-sm" radioClassName)
                    prop.isChecked (activeDataHub.Url = dataHub.Url)
                    prop.onChange (fun (_: bool) -> setDataHub dataHub)
                ]
                Html.span [ prop.text dataHub.Name; prop.title dataHub.Url ]
            ]
        ]

    [<ReactComponent>]
    static member private DataHubSelect
        (
            dataHub: DataHubInformation,
            dataHubs: DataHubCollection,
            setDataHub: DataHubInformation -> unit,
            ?onManageDataHubs: unit -> unit
        ) =

        let radioid = React.useId ()

        Html.fieldSet [
            prop.className "swt:fieldset"
            prop.children [
                Html.legend [
                    prop.className "swt:fieldset-legend swt:flex swt:w-full swt:items-center swt:justify-between"
                    prop.children [
                        Html.span "Select DataHUB"
                        match onManageDataHubs with
                        | Some onManage ->
                            Html.button [
                                prop.testId "ManageDataHubsButton"
                                prop.type'.button
                                prop.className "swt:btn swt:btn-ghost swt:btn-xs swt:btn-square"
                                prop.title "Manage DataHUBs in the settings"
                                prop.ariaLabel "Manage DataHUBs in the settings"
                                prop.onClick (fun _ -> onManage ())
                                prop.children [
                                    Html.span [
                                        prop.className "swt:iconify swt:fluent--settings-24-regular swt:size-4"
                                    ]
                                ]
                            ]
                        | None -> ()
                    ]
                ]
                Html.div [
                    prop.className
                        "swt:flex swt:flex-col swt:w-full swt:cursor-not-allowed swt:gap-0.5 swt:p-2 swt:rounded swt:border swt:border-dashed swt:border-base-content"
                    prop.children [
                        Html.p [
                            prop.className "swt:text-base-content swt:text-lg swt:font-semibold"
                            prop.text dataHub.Name
                        ]
                        Html.p [
                            prop.className "swt:text-xs swt:text-base-content/70"
                            prop.text (dataHub.GetDescription())
                        ]
                        Html.a [
                            prop.href dataHub.Url
                            prop.target "_blank"
                            prop.rel "noopener noreferrer"
                            prop.className "swt:link swt:link-info"
                            prop.text dataHub.Url
                        ]
                    ]
                ]
                Html.div [
                    prop.className "swt:label"
                    prop.children [
                        Html.details [
                            prop.className "swt:collapse swt:w-full swt:collapse-arrow"
                            prop.children [
                                Html.summary [
                                    prop.testId "DataHubOptionsToggle"
                                    prop.className "swt:collapse-title swt:p-1"
                                    prop.children [ Html.span "Other options" ]
                                ]
                                Html.div [
                                    prop.className "swt:collapse-content swt:text-sm swt:flex swt:flex-col swt:gap-0.5"
                                    prop.children [
                                        Authentication.DataHubRadioItem(
                                            dataHub,
                                            radioid,
                                            dataHubs.Default,
                                            setDataHub,
                                            radioClassName = "swt:radio swt:radio-sm swt:radio-primary"
                                        )
                                        for dataHubI in dataHubs.Supported do
                                            Authentication.DataHubRadioItem(dataHub, radioid, dataHubI, setDataHub)
                                        Authentication.CustomDataHubRadioItem(dataHub, radioid, setDataHub)
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member private NotAuthenticatedView
        (
            onSignIn: SignInInformation -> unit,
            setError: exn option -> unit,
            dataHubs: DataHubCollection,
            ?initialDataHubUrl,
            ?onManageDataHubs: unit -> unit
        ) =

        let initialDataHubUrl = defaultArg initialDataHubUrl dataHubs.Default
        let dataHubUrl, setDataHubUrl = React.useState initialDataHubUrl
        let pat, setPat = React.useState ""

        Html.div [
            prop.className "swt:flex swt:flex-col swt:p-4 swt:w-md swt:gap-2"
            prop.children [
                Html.h1 [
                    prop.text "DataHUB Authentication"
                    prop.className "swt:text-center swt:text-3xl swt:font-bold swt:mb-2"
                ]
                Html.p [
                    prop.text "Sign in to your DataHUB account to access your ARCs."
                    prop.className "swt:text-center swt:text-sm swt:text-base-content/80"
                ]
                Html.fieldSet [
                    prop.className "swt:fieldset"
                    prop.children [
                        Html.label [
                            prop.className "swt:fieldset-legend"
                            prop.text "Personal Access Token"
                        ]
                        Html.input [
                            prop.testId "PersonalAccessTokenInput"
                            prop.className "swt:input swt:w-full"
                            prop.placeholder "Personal Access token"
                            prop.type'.password
                            prop.value pat
                            prop.onChange (fun value -> setPat value)
                        ]
                        Html.p [
                            prop.className "swt:text-xs swt:text-base-content/60 swt:mt-1"
                            prop.text
                                "New to access tokens? ARCitISA can open GitLab with everything preconfigured for you. Then copy the token and paste it here!"
                        ]
                    ]
                ]
                Authentication.DataHubSelect(dataHubUrl, dataHubs, setDataHubUrl, ?onManageDataHubs = onManageDataHubs)
                Html.a [
                    prop.testId "GeneratePatLink"
                    prop.className "swt:btn swt:btn-soft swt:btn-info swt:px-1"
                    prop.children [
                        Html.span [
                            prop.className "swt:flex swt:items-center swt:gap-2"
                            prop.children [
                                Html.text "Generate Personal Access Token"
                                Html.i [
                                    prop.className "swt:iconify swt:fluent--open-20-regular swt:size-4"
                                ]
                            ]
                        ]
                    ]
                    prop.href (Helper.GitLabUrls.prefillGitLabPATScopes dataHubUrl.Url)
                    prop.target.blank
                    prop.rel "noopener noreferrer"
                ]
                Html.button [
                    prop.testId "SignInButton"
                    prop.className "swt:btn swt:btn-primary swt:btn-lg swt:w-full"
                    prop.text "Sign In"
                    prop.disabled (
                        System.String.IsNullOrWhiteSpace pat
                        || System.String.IsNullOrWhiteSpace dataHubUrl.Url
                    )
                    prop.onClick (fun _ ->
                        onSignIn {
                            GitLabBaseUrl = dataHubUrl.Url
                            PersonalAccessToken = pat
                            OnErrorCallback = fun ex -> setError (Some ex)
                        }
                    )
                ]
                Html.p [
                    prop.className "swt:text-sm swt:text-center swt:text-base-content/80"
                    prop.text "Don't have an account? Sign up on your DataHUB instance to create one."
                ]
            ]
        ]

    [<ReactComponent>]
    static member AddAnotherAccountBtn(onAddAccount: unit -> unit) =
        Html.button [
            prop.testId "AddAnotherAccountButton"
            prop.className "swt:btn swt:btn-sm swt:btn-ghost swt:w-full swt:gap-1"
            prop.onClick (fun _ -> onAddAccount ())
            prop.text "Add another account"
        ]

    [<ReactComponent>]
    static member UserAvatar
        (
            accounts: AuthStateDto,
            onSignIn: SignInInformation -> unit,
            ?isLoading: bool,
            ?dropdownClassName: string,
            ?onRotateToken: string -> unit,
            ?onSwitchAccount: string -> unit,
            ?onRemoveAccounts: RemoveAccountsRequest -> unit,
            ?dataHubUrls: string[],
            ?onManageDataHubs: unit -> unit
        ) =

        let isOpen, setIsOpen = React.useState false
        let error, setError = React.useState (None: exn option)
        let showAddAccount, setShowAddAccount = React.useState false

        let initialDataHubUrl, setInitialDataHubUrl =
            React.useState (None: DataHubInformation option)

        let activeUser = accounts.ActiveUser()

        let dataHubs =
            React.useMemo (
                (fun () -> Helper.DataHubCollection.FromUrls(defaultArg dataHubUrls Helper.Default_DataHub_Urls)),
                [| box dataHubUrls |]
            )

        let onSignIn =
            fun signInInfo ->
                setError None
                setIsOpen false
                setInitialDataHubUrl None
                setShowAddAccount false
                onSignIn signInInfo

        // The settings open in the main window, so the menu closes first.
        let onManageDataHubs =
            onManageDataHubs
            |> Option.map (fun onManage ->
                fun () ->
                    setIsOpen false
                    setShowAddAccount false
                    onManage ()
            )

        let ToggleContent =
            React.useMemo (
                (fun () ->
                    match isLoading, activeUser with
                    | Some true, _ ->
                        Html.span [
                            prop.className "swt:loading swt:loading-spinner swt:loading-sm"
                        ]
                    | _, Some userInformation -> Authentication.UserAvatarIcon(userInformation)
                    | _, None -> Authentication.AvatarPlaceholder()
                ),
                [| box activeUser; box isLoading; box error |]
            )

        let onRegenerateToken (account: AccountSummary) =
            setShowAddAccount true

            (Some >> setInitialDataHubUrl) {
                Name = account.User.TargetDataHub
                Url = account.User.TargetDataHub
                Description =
                    Some
                        "A browser window with prefilled scopes for regenerating your token will open. Please generate a new token and use it to sign in again."
            }

        let content =
            React.useMemo (
                (fun () ->
                    match activeUser with
                    | Some user ->
                        if showAddAccount then
                            Html.div [
                                prop.className "swt:flex swt:flex-col swt:gap-2"
                                prop.children [
                                    Html.button [
                                        prop.testId "AddAccountBackButton"
                                        prop.className "swt:btn swt:btn-sm swt:btn-ghost swt:self-start"
                                        prop.onClick (fun _ -> setShowAddAccount false)
                                        prop.text "\u2190 Back to account"
                                    ]
                                    Authentication.NotAuthenticatedView(
                                        onSignIn,
                                        setError,
                                        dataHubs,
                                        ?initialDataHubUrl = initialDataHubUrl,
                                        ?onManageDataHubs = onManageDataHubs
                                    )
                                ]
                            ]
                        else
                            Html.div [
                                prop.className "swt:flex swt:flex-col swt:gap-2"
                                prop.children [
                                    AccountManager.Main(
                                        accounts,
                                        onRegenerateToken = onRegenerateToken,
                                        ?onRotateToken = onRotateToken,
                                        ?onSwitchAccount = onSwitchAccount,
                                        ?onRemoveAccounts = onRemoveAccounts
                                    )
                                    Authentication.AddAnotherAccountBtn(fun () -> setShowAddAccount true)
                                ]
                            ]
                    | None ->
                        Authentication.NotAuthenticatedView(
                            onSignIn,
                            setError,
                            dataHubs,
                            ?onManageDataHubs = onManageDataHubs
                        )
                ),
                [|
                    box activeUser
                    box error
                    box showAddAccount
                    box accounts
                    box initialDataHubUrl
                    box dataHubs
                |]
            )

        React.Fragment [
            BaseModal.ErrorModalObsolete(
                error.IsSome,
                (fun _ -> setError None),
                error
                |> Option.map _.Message
                |> Option.defaultValue "An unknown error occurred."
            )
            Dropdown.Main(
                isOpen,
                setIsOpen,
                Html.div [
                    prop.className "swt:indicator"
                    prop.children [
                        match activeUser, accounts.ActiveAccount with
                        | Some _, Some activeAccount ->
                            match activeAccount.TokenStatus with
                            | TokenStatus.Invalid ->
                                Html.span [
                                    prop.testId "TokenInvalidIndicator"
                                    prop.className "swt:indicator-item swt:indicator-bottom"
                                    prop.ariaLabel
                                        "Your token is invalid. Please update your token or remove the account."
                                    prop.title "Your token is invalid. Please update your token or remove the account."
                                    prop.children [
                                        Html.i [
                                            prop.ariaHidden true
                                            prop.className "swt:iconify swt:fluent--warning-12-filled swt:text-error"
                                        ]
                                    ]
                                ]
                            | TokenStatus.Expiring ->
                                Html.span [
                                    prop.testId "TokenExpiringIndicator"
                                    prop.className "swt:indicator-item swt:indicator-bottom"
                                    prop.ariaLabel "Your token is expiring soon. Rotate it to avoid interruption."
                                    prop.title (
                                        activeAccount.TokenExpiresOn
                                        |> Option.map (fun expiresOn ->
                                            $"Your token expires on {expiresOn}. Rotate it soon to avoid interruption."
                                        )
                                        |> Option.defaultValue
                                            "Your token is expiring soon. Rotate it to avoid interruption."
                                    )
                                    prop.children [
                                        Html.i [
                                            prop.ariaHidden true
                                            prop.className "swt:iconify swt:fluent--warning-12-filled swt:text-warning"
                                        ]
                                    ]
                                ]
                            | TokenStatus.Ok -> Html.none
                        | _ -> Html.none
                        Html.button [
                            prop.testId "UserButtonToggle"
                            prop.onClick (fun _ -> setIsOpen (not isOpen))
                            prop.className "swt:btn swt:btn-circle swt:btn-sm"
                            prop.children [ ToggleContent ]
                        ]
                    ]
                ],
                Html.div [ prop.testId "UserDropdownContent"; prop.children content ],
                contentClassName =
                    "swt:flex swt:flex-col swt:min-w-xs swt:gap-2 swt:border-base-content swt:border-1 swt:p-2 swt:shadow-lg swt:rounded swt:bg-base-100 swt:top-[110%]",
                closeOnClick = false,
                ?dropdownClassName = dropdownClassName
            )
        ]

    static member ExmpUserInformation = {
        Id = 1
        LocalSwateAccountId = "acc-1"
        Username = "john-doe"
        Name = "John Doe"
        Email = "john-doe@mail.com"
        CommitEmail = None
        AvatarUrl = "https://www.gravatar.com/avatar/00000000000000000000000000000000?d=mp&f=y"
        TargetDataHub = Helper.Default_DataHub_Url
    }

    static member User = {
        Types.AuthStateDto.Empty with
            ActiveAccount =
                Some {
                    User = Authentication.ExmpUserInformation
                    DateAdded = "2026-01-01T00:00:00.0000000Z"
                    TokenStatus = TokenStatus.Ok
                    TokenExpiresOn = Some "2026-02-01"
                }
            StoredAccounts = [|
                {
                    User = Authentication.ExmpUserInformation
                    DateAdded = "2026-01-01T00:00:00.0000000Z"
                    TokenStatus = TokenStatus.Ok
                    TokenExpiresOn = Some "2026-02-01"
                }
            |]
    }

    [<ReactComponent(true)>]
    static member Entry(?user, ?dataHubUrls: string[], ?onManageDataHubs: unit -> unit) =
        let accounts, setAccounts = React.useState (defaultArg user AuthStateDto.Empty)

        let dataHubUrls, setDataHubUrls =
            React.useState (defaultArg dataHubUrls Helper.Default_DataHub_Urls)

        let isLoading, setIsLoading = React.useState false

        let onSignIn =
            fun (signInInfo: SignInInformation) ->
                promise {
                    setIsLoading true
                    do! Promise.sleep 1000
                    // Testing, ignore for story tests
                    // let! userInformation = GitLabAPI.getUserAPIRequest signInInfo
                    // Here should be try get user information from the DataHUB using the provided signInInfo and handle possible errors by calling signInInfo.OnErrorCallback with the error message. For now we just simulate a successful sign in with example user information and a delay.
                    let activeUser = {
                        Authentication.ExmpUserInformation with
                            TargetDataHub = signInInfo.GitLabBaseUrl
                    }

                    Helper.DataHubCollection.TryAddUrl(signInInfo.GitLabBaseUrl, dataHubUrls)
                    |> Option.iter setDataHubUrls

                    setAccounts {
                        ActiveAccount =
                            Some {
                                User = activeUser
                                DateAdded = "2026-01-01T00:00:00.0000000Z"
                                TokenStatus = TokenStatus.Ok
                                TokenExpiresOn = Some "2026-02-01"
                            }
                        StoredAccounts = [|
                            {
                                User = activeUser
                                DateAdded = "2026-01-01T00:00:00.0000000Z"
                                TokenStatus = TokenStatus.Ok
                                TokenExpiresOn = Some "2026-02-01"
                            }
                            {
                                User = {
                                    Id = 2
                                    LocalSwateAccountId = "acc-2"
                                    Username = "max-mustermann"
                                    Name = "Max Mustermann"
                                    Email = "max@example.org"
                                    CommitEmail = None
                                    AvatarUrl =
                                        "https://www.gravatar.com/avatar/22222222222222222222222222222222?d=mp&f=y"
                                    TargetDataHub = "https://datahub.rz.rptu.de/"
                                }
                                DateAdded = "2026-01-02T00:00:00.0000000Z"
                                TokenStatus = TokenStatus.Expiring
                                TokenExpiresOn = Some "2026-01-15"
                            }
                            {
                                User = {
                                    Id = 3
                                    LocalSwateAccountId = "acc-3"
                                    Username = "mr-lazy"
                                    Name = "Mr Lazy"
                                    Email = "lazy@example.org"
                                    CommitEmail = None
                                    AvatarUrl =
                                        "https://www.gravatar.com/avatar/33333333333333333333333333333333?d=mp&f=y"
                                    TargetDataHub = "https://git.nfdi4plants.org/"
                                }
                                DateAdded = "2022-01-02T00:00:00.0000000Z"
                                TokenStatus = TokenStatus.Invalid
                                TokenExpiresOn = Some "2022-02-01"
                            }
                        |]
                    }

                    setIsLoading false
                }
                |> Promise.start

        let deletedTokens, setDeletedTokens = React.useState 0

        let onSwitchAccount (localSwateAccountId: string) =
            let nextActive =
                accounts.StoredAccounts
                |> Array.tryFind (fun account -> account.User.LocalSwateAccountId = localSwateAccountId)
                |> Option.orElse accounts.ActiveAccount
                |> Option.orElse (accounts.StoredAccounts |> Array.tryHead)

            let next = {
                ActiveAccount = nextActive
                StoredAccounts = accounts.StoredAccounts
            }

            setAccounts next

        let onRemoveAccounts (request: RemoveAccountsRequest) =
            let isRemoved (localSwateAccountId: string) =
                request.LocalSwateAccountIds |> Array.contains localSwateAccountId

            if request.DeleteTokensOnDataHub then
                setDeletedTokens (deletedTokens + request.LocalSwateAccountIds.Length)

            let filteredAccounts =
                accounts.StoredAccounts
                |> Array.filter (fun account -> not (isRemoved account.User.LocalSwateAccountId))

            let nextActive =
                match accounts.ActiveAccount with
                | Some activeAccount when not (isRemoved activeAccount.User.LocalSwateAccountId) ->
                    filteredAccounts
                    |> Array.tryFind (fun account ->
                        account.User.LocalSwateAccountId = activeAccount.User.LocalSwateAccountId
                    )
                | _ -> filteredAccounts |> Array.tryHead

            let next = {
                ActiveAccount = nextActive
                StoredAccounts = filteredAccounts
            }

            setAccounts next

        let onRotateToken (localSwateAccountId: string) =
            // For testing, we just set the token status to Ok and update the expiration date. In a real implementation, this should trigger the token rotation flow and update the account information accordingly.
            let nextAccounts =
                accounts.StoredAccounts
                |> Array.map (fun account ->
                    if account.User.LocalSwateAccountId = localSwateAccountId then
                        {
                            account with
                                TokenStatus = TokenStatus.Ok
                                TokenExpiresOn = Some "2026-03-01"
                        }
                    else
                        account
                )

            let nextActive =
                match accounts.ActiveAccount with
                | Some activeAccount when activeAccount.User.LocalSwateAccountId = localSwateAccountId ->
                    nextAccounts
                    |> Array.tryFind (fun account ->
                        account.User.LocalSwateAccountId = activeAccount.User.LocalSwateAccountId
                    )
                | other -> other

            setAccounts {
                ActiveAccount = nextActive
                StoredAccounts = nextAccounts
            }

        Html.div [
            prop.className "swt:flex swt:flex-col swt:m-10 swt:gap-2"
            prop.children [
                Html.h1 [
                    prop.text "Authentication"
                    prop.className "swt:text-2xl swt:font-bold swt:mb-4"
                ]
                Html.p [
                    prop.testId "SignedInInfo"
                    prop.textf "Signed In: %b" (accounts.ActiveUser() |> Option.isSome)
                ]
                Html.p [
                    prop.testId "DeletedTokensInfo"
                    prop.textf "Deleted tokens: %i" deletedTokens
                ]
                Authentication.UserAvatar(
                    accounts,
                    onSignIn,
                    isLoading = isLoading,
                    onSwitchAccount = onSwitchAccount,
                    onRemoveAccounts = onRemoveAccounts,
                    onRotateToken = onRotateToken,
                    dataHubUrls = dataHubUrls,
                    ?onManageDataHubs = onManageDataHubs
                )
            ]
        ]
