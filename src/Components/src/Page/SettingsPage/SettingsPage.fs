namespace Swate.Components.PageComponents.SettingsPage

open System
open Feliz
open Fable.Core
open Fable.Core.JsInterop
open Swate.Components
open Swate.Components.Page.GitSidebarTypes
open Swate.Components.Primitive.LayoutComponents
open Swate.Components.JsBindings
open Swate.Components.Composite.Authentication.Helper
open Swate.Components.Composite.ThemeSelector
open Swate.Components.Composite.ThemeSelector.Context
open Swate.Components.Composite.TermSearch
open Swate.Components.PageComponents.SettingsPage.Types
open Swate.Components.Util.UserOS

module SettingsPageDefaults =
    [<Literal>]
    let AutoCreateNotesFolderLocalStorageKey = "swate-settings-auto-create-notes-folder"

module private SettingsPageHelper =

    /// The number in the input if it is a whole number inside the range.
    let tryParseInRange (minValue: int) (maxValue: int) (input: string) : int option =
        let success, value = Int32.TryParse(input.Trim())

        if success && value >= minValue && value <= maxValue then
            Some value
        else
            None

    let description (text: string) =
        Html.p [
            prop.className "swt:mt-1 swt:text-sm swt:text-base-content/70"
            prop.text text
        ]

    /// The test id of a category's page and the start of the ids of its settings.
    let pageId (category: SettingsCategory) = $"settings-{category}"

    /// The id of a setting on its category's page, the target of its link in the navigation.
    let fieldId (category: SettingsCategory) (index: int) = $"{pageId category}-{index}"

[<Erase; Mangle(false)>]
type SettingsPage =

    /// A label with its value. The test id sits on the value, so it holds exactly the shown text.
    [<ReactComponent>]
    static member private GitExecutableRow(label: string, value: string, testId: string) =
        Html.p [
            prop.className "swt:flex swt:flex-wrap swt:gap-x-2 swt:text-sm"
            prop.children [
                Html.span [ prop.className "swt:font-medium"; prop.text label ]
                Html.span [
                    prop.testId testId
                    prop.className "swt:break-all swt:text-base-content/70"
                    prop.text value
                ]
            ]
        ]

    /// One setting with its title, control and description.
    [<ReactComponent>]
    static member private Field(id: string, field: SettingField) =
        Html.div [
            prop.id id
            prop.className "swt:grid swt:grid-cols-1 swt:md:grid-cols-2 swt:gap-2 swt:py-2 swt:scroll-mt-4"
            prop.children [
                Html.p [
                    prop.className "swt:text-xl not-prose"
                    prop.text field.Title
                ]
                Html.div [
                    prop.className "not-prose"
                    prop.children [ field.Children ]
                ]
                Html.div [
                    prop.className "swt:text-sm swt:text-base-content/70 swt:md:col-span-2 swt:prose"
                    prop.children field.Description
                ]
            ]
        ]

    [<ReactComponent>]
    static member private AutoCreateNotesFolderToggle(isEnabled: bool, onChange: bool -> unit) =
        Html.input [
            prop.testId "SettingsAutoCreateNotesFolder"
            prop.className [
                if isEnabled then
                    "swt:toggle-primary"
                "swt:toggle"
            ]
            prop.type'.checkbox
            prop.isChecked isEnabled
            prop.onChange onChange
        ]

    /// Keeps the value in localStorage. The hook writes its default into localStorage on mount, so the
    /// controlled variant is a separate component and never mounts it.
    [<ReactComponent>]
    static member private LocalAutoCreateNotesFolderSetting(?onEnabled: unit -> unit) =
        let onEnabled = defaultArg onEnabled ignore

        let autoCreateNotesFolder, setAutoCreateNotesFolder =
            React.useLocalStorage (SettingsPageDefaults.AutoCreateNotesFolderLocalStorageKey, true)

        SettingsPage.AutoCreateNotesFolderToggle(
            autoCreateNotesFolder,
            fun isEnabled ->
                setAutoCreateNotesFolder isEnabled

                if isEnabled then
                    onEnabled ()
        )

    [<ReactComponent>]
    static member private ControlledAutoCreateNotesFolderSetting
        (isEnabled: bool, ?onChange: bool -> unit, ?onEnabled: unit -> unit)
        =
        let onChange = defaultArg onChange ignore
        let onEnabled = defaultArg onEnabled ignore

        SettingsPage.AutoCreateNotesFolderToggle(
            isEnabled,
            fun nextValue ->
                onChange nextValue

                if nextValue then
                    onEnabled ()
        )

    [<ReactComponent>]
    static member private AutoCreateNotesFolderSetting
        (?autoCreateNotesFolder: bool, ?onChange: bool -> unit, ?onEnabled: unit -> unit)
        =
        match autoCreateNotesFolder with
        | Some isEnabled ->
            SettingsPage.ControlledAutoCreateNotesFolderSetting(isEnabled, ?onChange = onChange, ?onEnabled = onEnabled)
        | None -> SettingsPage.LocalAutoCreateNotesFolderSetting(?onEnabled = onEnabled)

    /// The control of a Git default, marked when it differs from the built-in value and with a Reset then.
    [<ReactComponent>]
    static member private GitDefaultControl
        (rowId: string, control: ReactElement, isChanged: bool, onReset: unit -> unit, ?rangeHint: string)
        =
        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-1"
            prop.children [
                Html.div [
                    prop.className "swt:flex swt:flex-wrap swt:items-center swt:gap-2"
                    prop.children [
                        control
                        if isChanged then
                            Html.span [
                                prop.testId (rowId + "Changed")
                                prop.className "swt:badge swt:badge-sm swt:badge-warning"
                                prop.title "Differs from the built-in default"
                                prop.text "Custom"
                            ]

                            Html.button [
                                prop.testId (rowId + "Reset")
                                prop.type'.button
                                prop.className "swt:btn swt:btn-ghost swt:btn-sm swt:btn-square"
                                prop.title "Reset to the built-in default"
                                prop.ariaLabel "Reset to the built-in default"
                                prop.onClick (fun _ -> onReset ())
                                prop.children [
                                    Html.span [
                                        prop.className "swt:iconify swt:fluent--arrow-reset-24-regular swt:size-4"
                                    ]
                                ]
                            ]
                    ]
                ]
                if rangeHint.IsSome then
                    Html.p [
                        prop.testId (rowId + "Range")
                        prop.className "swt:text-sm swt:text-error"
                        prop.text rangeHint.Value
                    ]
            ]
        ]

    [<ReactComponent>]
    static member private GitDefaultNumberInput
        (rowId: string, draft: string, onDraft: string -> unit, minValue: int, maxValue: int, isValid: bool)
        =
        Html.input [
            prop.testId rowId
            prop.className [
                "swt:join-item swt:input swt:w-32"
                if not isValid then
                    "swt:input-error"
            ]
            prop.type'.number
            prop.step 1
            prop.min minValue
            prop.max maxValue
            prop.custom ("aria-invalid", not isValid)
            prop.value draft
            prop.onChange (fun (value: string) -> onDraft value)
        ]

    [<ReactComponent>]
    static member private GitDefaultToggle(rowId: string, isChecked: bool, onChange: bool -> unit) =
        Html.input [
            prop.testId rowId
            prop.className [
                if isChecked then
                    "swt:toggle-primary"
                "swt:toggle"
            ]
            prop.type'.checkbox
            prop.isChecked isChecked
            prop.onChange onChange
        ]

    /// A switch that applies right away.
    [<ReactComponent>]
    static member private GitDefaultToggleSetting(rowId: string, value: bool, builtIn: bool, onApply: bool -> unit) =
        SettingsPage.GitDefaultControl(
            rowId,
            SettingsPage.GitDefaultToggle(rowId, value, onApply),
            value <> builtIn,
            (fun () -> onApply builtIn)
        )

    /// A number that applies with Update, like the UI scaling. Reset applies the built-in value right away.
    [<ReactComponent>]
    static member private GitDefaultNumberSetting
        (rowId: string, value: int, builtIn: int, minValue: int, maxValue: int, onApply: int -> unit)
        =
        let draft, setDraft = React.useState (string value)

        React.useEffect ((fun () -> setDraft (string value)), [| box value |])

        let parsed = SettingsPageHelper.tryParseInRange minValue maxValue draft

        SettingsPage.GitDefaultControl(
            rowId,
            Html.div [
                prop.className "swt:join"
                prop.children [
                    SettingsPage.GitDefaultNumberInput(rowId, draft, setDraft, minValue, maxValue, parsed.IsSome)
                    Html.button [
                        prop.testId (rowId + "Update")
                        prop.type'.button
                        prop.className "swt:join-item swt:btn"
                        prop.disabled (parsed.IsNone || parsed = Some value)
                        prop.text "Update"
                        prop.onClick (fun _ -> parsed |> Option.iter onApply)
                    ]
                ]
            ],
            value <> builtIn,
            (fun () -> onApply builtIn),
            ?rangeHint =
                (if parsed.IsNone then
                     Some $"Enter a whole number from {minValue} to {maxValue}."
                 else
                     None)
        )

    [<ReactComponent>]
    static member private UIScalingControl(uiScaling: int, onUIScaling: int -> unit) =
        let scalingDraft, setScalingDraft = React.useState uiScaling

        React.useEffect ((fun () -> setScalingDraft uiScaling), [| box uiScaling |])

        Html.div [
            prop.className "swt:join"
            prop.children [
                Html.label [
                    prop.className "swt:join-item swt:input"
                    prop.children [
                        Html.input [
                            prop.className "swt:w-16"
                            prop.onChange setScalingDraft
                            prop.type'.number
                            prop.value scalingDraft
                            prop.step 5
                        ]
                        Html.text "%"
                    ]
                ]
                Html.div [
                    prop.text "Update"
                    prop.className "swt:join-item swt:btn"
                    prop.onClick (fun _ -> onUIScaling scalingDraft)
                ]
            ]
        ]

    [<ReactComponent>]
    static member private UIScalingDescription() =
        let modifierKey =
            match getUserOS () with
            | MacOS
            | IOS -> "⌘"
            | _ -> "Ctrl"

        Html.div [
            SettingsPageHelper.description
                "Allows adjusting the scaling of the user interface. 100% represents the default size."
            Html.p [
                prop.className "swt:text-sm swt:text-base-content/70"
                prop.children [
                    Html.text "Scale the UI globally with "
                    Html.kbd [
                        prop.className "swt:kbd swt:kbd-sm"
                        prop.text modifierKey
                    ]
                    Html.text " + "
                    Html.kbd [ prop.className "swt:kbd swt:kbd-sm"; prop.text "+" ]
                    Html.text " or "
                    Html.kbd [
                        prop.className "swt:kbd swt:kbd-sm"
                        prop.text modifierKey
                    ]
                    Html.text " + "
                    Html.kbd [ prop.className "swt:kbd swt:kbd-sm"; prop.text "-" ]
                    Html.text "."
                ]
            ]
        ]

    [<ReactComponent>]
    static member private GitExecutable
        (setting: GitExecutableSetting, onChoose: unit -> unit, onUseAutomatic: unit -> unit, onCheckAgain: unit -> unit) =
        let source =
            match setting.ChosenPath with
            | Some path -> path
            | None -> "Automatic (Git found on PATH)"

        Html.div [
            prop.testId "SettingsGitExecutable"
            prop.className "swt:flex swt:flex-col swt:gap-2"
            prop.children [
                SettingsPage.GitExecutableRow("Source", source, "SettingsGitExecutableSource")

                (match setting.PathInUse with
                 | Some path -> SettingsPage.GitExecutableRow("In use", path, "SettingsGitExecutablePathInUse")
                 | None -> Html.none)

                (match setting.GitVersion with
                 | Some version -> SettingsPage.GitExecutableRow("Git", version, "SettingsGitExecutableGitVersion")
                 | None -> Html.none)

                (match setting.GitLfsVersion with
                 | Some version ->
                     SettingsPage.GitExecutableRow("Git LFS", version, "SettingsGitExecutableGitLfsVersion")
                 | None -> Html.none)

                (match setting.Problem with
                 | Some problem ->
                     Html.div [
                         prop.testId "SettingsGitExecutableProblem"
                         prop.className "swt:alert swt:alert-warning swt:min-w-0 swt:px-3 swt:py-2 swt:text-sm"
                         prop.children [
                             Html.span [
                                 prop.className "swt:iconify swt:fluent--warning-24-regular swt:size-4 swt:shrink-0"
                                 prop.ariaHidden true
                             ]
                             Html.span [
                                 prop.className "swt:min-w-0 swt:wrap-anywhere"
                                 prop.text problem
                             ]
                         ]
                     ]
                 | None -> Html.none)

                (if setting.IsChecking then
                     Html.span [
                         prop.testId "SettingsGitExecutableChecking"
                         prop.className "swt:flex swt:items-center swt:gap-2 swt:text-sm"
                         prop.children [
                             Html.span [
                                 prop.className "swt:loading swt:loading-spinner swt:loading-xs"
                             ]
                             Html.span "Checking..."
                         ]
                     ]
                 else
                     Html.none)

                Html.div [
                    prop.className "swt:flex swt:flex-wrap swt:items-center swt:gap-2"
                    prop.children [
                        Html.button [
                            prop.testId "SettingsGitExecutableChoose"
                            prop.type'.button
                            prop.className "swt:btn swt:btn-sm"
                            prop.text "Choose..."
                            prop.onClick (fun _ -> onChoose ())
                        ]
                        (if setting.ChosenPath.IsSome then
                             Html.button [
                                 prop.testId "SettingsGitExecutableUseAutomatic"
                                 prop.type'.button
                                 prop.className "swt:btn swt:btn-sm"
                                 prop.text "Use automatic"
                                 prop.onClick (fun _ -> onUseAutomatic ())
                             ]
                         else
                             Html.none)
                        Html.button [
                            prop.testId "SettingsGitExecutableCheckAgain"
                            prop.type'.button
                            prop.className "swt:btn swt:btn-sm"
                            prop.disabled setting.IsChecking
                            prop.text "Check again"
                            prop.onClick (fun _ -> onCheckAgain ())
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member private DataHubItem(url: string, onRemove: unit -> unit) =
        Html.li [
            prop.className "swt:flex swt:items-center swt:gap-2"
            prop.children [
                Html.span [
                    prop.testId "SettingsDataHub"
                    prop.className "swt:min-w-0 swt:flex-1 swt:break-all swt:text-sm"
                    prop.text url
                ]
                Html.button [
                    prop.testId $"SettingsDataHubRemove-{url}"
                    prop.type'.button
                    prop.className "swt:btn swt:btn-ghost swt:btn-sm swt:btn-square"
                    prop.title $"Remove {url}"
                    prop.ariaLabel $"Remove {url}"
                    prop.onClick (fun _ -> onRemove ())
                    prop.children [
                        Html.span [
                            prop.className "swt:iconify swt:fluent--delete-24-regular swt:size-4"
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member private DataHubs(urls: string[], onChange: string[] -> unit) =
        let draft, setDraft = React.useState ""
        let nextUrls = DataHubCollection.TryAddUrl(draft, urls)

        let isInvalid =
            not (String.IsNullOrWhiteSpace draft) && (GitLabUrls.tryNormalize draft).IsNone

        let add () =
            nextUrls
            |> Option.iter (fun next ->
                onChange next
                setDraft ""
            )

        Html.div [
            prop.testId "SettingsDataHubs"
            prop.className "swt:flex swt:flex-col swt:gap-2"
            prop.children [
                if urls.Length > 0 then
                    Html.ul [
                        prop.className "swt:flex swt:flex-col swt:gap-1"
                        prop.children (
                            urls
                            |> Array.map (fun url ->
                                SettingsPage.DataHubItem(url, fun () -> onChange (urls |> Array.filter ((<>) url)))
                            )
                        )
                    ]
                Html.div [
                    prop.className "swt:join"
                    prop.children [
                        Html.input [
                            prop.testId "SettingsDataHubInput"
                            prop.className [
                                "swt:join-item swt:input"
                                if isInvalid then
                                    "swt:input-error"
                            ]
                            prop.type'.url
                            prop.placeholder "https://"
                            prop.custom ("aria-invalid", isInvalid)
                            prop.value draft
                            prop.onChange (fun (value: string) -> setDraft value)
                            prop.onKeyDown (fun e ->
                                if e.key = "Enter" then
                                    add ()
                            )
                        ]
                        Html.button [
                            prop.testId "SettingsDataHubAdd"
                            prop.type'.button
                            prop.className "swt:join-item swt:btn"
                            prop.disabled nextUrls.IsNone
                            prop.text "Add"
                            prop.onClick (fun _ -> add ())
                        ]
                    ]
                ]
            ]
        ]

    /// The page of one category: a box with its settings.
    [<ReactComponent>]
    static member private CategoryPage(settings: CategorySettings) =
        let sectionId = SettingsPageHelper.pageId settings.Category

        Html.div [
            prop.testId sectionId
            prop.className "swt:flex swt:w-full swt:justify-center"
            prop.children [
                LayoutComponents.BoxedField(
                    settings.Title,
                    content = [
                        for index, field in Array.indexed settings.Fields do
                            SettingsPage.Field(SettingsPageHelper.fieldId settings.Category index, field)
                    ]
                )
            ]
        ]

    /// The categories with their settings. An expanded category lists its settings, which link into its page.
    [<ReactComponent>]
    static member private Navigation
        (
            categories: CategorySettings[],
            active: SettingsCategory,
            expanded: Set<SettingsCategory>,
            onOpenCategory: SettingsCategory -> unit,
            onToggleCategory: SettingsCategory -> unit,
            onSelectField: SettingsCategory -> int -> unit
        ) =
        Html.nav [
            prop.testId "SettingsNavigation"
            prop.ariaLabel "Settings categories"
            prop.className
                "swt:shrink-0 swt:px-4 swt:pt-4 swt:@3xl/settings:sticky swt:@3xl/settings:top-0 swt:@3xl/settings:w-56 swt:@5xl/settings:w-64 swt:@3xl/settings:pb-4"
            prop.children [
                Html.ul [
                    prop.className
                        "swt:menu swt:menu-sm swt:@5xl/settings:menu-md swt:w-full swt:bg-base-200 swt:rounded-box swt:[--menu-active-bg:color-mix(in_oklab,var(--color-primary)_10%,transparent)] swt:[--menu-active-fg:var(--color-primary)]"
                    prop.children [
                        for settings in categories do
                            let isActive = settings.Category = active
                            let isExpanded = expanded.Contains settings.Category
                            let sectionId = SettingsPageHelper.pageId settings.Category

                            Html.li [
                                Html.details [
                                    prop.custom ("open", isExpanded)
                                    prop.children [
                                        // The arrow expands or collapses the category. The rest of the row opens
                                        // its page. The arrow is a button of its own, so it replaces the drawn one.
                                        Html.summary [
                                            prop.testId $"SettingsNavigation-{sectionId}"
                                            prop.className [
                                                "swt:grid-cols-[1fr_auto] swt:after:hidden"
                                                if isActive then
                                                    "swt:bg-primary/10 swt:font-semibold swt:text-primary"
                                            ]
                                            if isActive then
                                                prop.custom ("aria-current", "page")
                                            prop.onClick (fun e ->
                                                e.preventDefault ()
                                                onOpenCategory settings.Category
                                            )
                                            prop.children [
                                                Html.span (settings.Title)
                                                Html.button [
                                                    prop.testId $"SettingsNavigationToggle-{sectionId}"
                                                    prop.type'.button
                                                    prop.className "swt:btn swt:btn-ghost swt:btn-xs swt:btn-square"
                                                    prop.ariaLabel (
                                                        if isExpanded then
                                                            $"Collapse {settings.Title}"
                                                        else
                                                            $"Expand {settings.Title}"
                                                    )
                                                    prop.ariaExpanded isExpanded
                                                    prop.onClick (fun e ->
                                                        e.preventDefault ()
                                                        e.stopPropagation ()
                                                        onToggleCategory settings.Category
                                                    )
                                                    prop.children [
                                                        Html.span [
                                                            prop.className [
                                                                "swt:iconify swt:fluent--chevron-down-16-regular swt:size-4 swt:transition-transform"
                                                                if isExpanded then
                                                                    "swt:rotate-180"
                                                            ]
                                                        ]
                                                    ]
                                                ]
                                            ]
                                        ]
                                        Html.ul [
                                            for index, field in Array.indexed settings.Fields do
                                                Html.li [
                                                    Html.button [
                                                        prop.testId $"SettingsNavigation-{sectionId}-{index}"
                                                        prop.type'.button
                                                        prop.className "swt:whitespace-normal"
                                                        prop.text field.Title
                                                        prop.onClick (fun _ -> onSelectField settings.Category index)
                                                    ]
                                                ]
                                        ]
                                    ]
                                ]
                            ]
                    ]
                ]
            ]
        ]

    /// The settings, one category per page, with a navigation beside the page or above it where the page is
    /// narrow. With a category the page opens at it.
    [<ReactComponent>]
    static member SettingsPage
        (
            ?onAutoCreateNotesFolderEnabled: unit -> unit,
            ?uiScaling: int,
            ?onUIScaling: int -> unit,
            ?gitDefaults: GitDefaultsSection,
            ?autoCreateNotesFolder: bool,
            ?onAutoCreateNotesFolderChange: bool -> unit,
            ?gitExecutable: GitExecutableField,
            ?dataHubs: DataHubsField,
            ?category: SettingsCategory
        ) =
        let onAutoCreateNotesFolderEnabled =
            defaultArg onAutoCreateNotesFolderEnabled ignore

        let termSearch = TermSearchConfigSetter.useSettings ()
        let appliesFromNow = "This is the default for ARCs opened from now on."

        let applyGitDefault (update: SettingsGitDefaults -> SettingsGitDefaults) =
            gitDefaults
            |> Option.iter (fun gitDefaults -> gitDefaults.OnChange(update gitDefaults.Current))

        let generalFields = [|
            {
                Title = "Theme"
                Description =
                    SettingsPageHelper.description
                        "Select the theme for the application. The 'Auto' option will use the system's theme settings."
                Children = ThemeSelector.ThemeSelector()
            }
            {
                Title = "Automatically create notes folder"
                Description =
                    SettingsPageHelper.description
                        "Automatically creates an optional /notes folder when an ARC is opened or created. Disable this here if you do not want automatic notes scaffolding."
                Children =
                    SettingsPage.AutoCreateNotesFolderSetting(
                        ?autoCreateNotesFolder = autoCreateNotesFolder,
                        ?onChange = onAutoCreateNotesFolderChange,
                        onEnabled = onAutoCreateNotesFolderEnabled
                    )
            }
            match uiScaling, onUIScaling with
            | Some uiScaling, Some onUIScaling -> {
                Title = "UI Scaling"
                Description = SettingsPage.UIScalingDescription()
                Children = SettingsPage.UIScalingControl(uiScaling, onUIScaling)
              }
            | _ -> ()
        |]

        let dataHubFields = [|
            match dataHubs with
            | Some field -> {
                Title = "DataHUB addresses"
                Description =
                    SettingsPageHelper.description
                        "The sign-in offers these under Other options, next to the official PLANTdataHUB. New addresses are added when you sign in."
                Children = SettingsPage.DataHubs(field.Urls, field.OnChange)
              }
            | None -> ()
        |]

        let gitFields = [|
            match gitExecutable with
            | Some field -> {
                Title = "Git executable"
                Description =
                    SettingsPageHelper.description
                        "The Git program the application runs. Leave it on automatic to use the Git found on your PATH, or choose the executable yourself. Git LFS runs through this Git, so the Git LFS version shown belongs to it."
                Children =
                    SettingsPage.GitExecutable(field.Setting, field.OnChoose, field.OnUseAutomatic, field.OnCheckAgain)
              }
            | None -> ()
            match gitDefaults with
            | Some gitDefaults ->
                {
                    Title = "Upload right away"
                    Description =
                        SettingsPageHelper.description
                            $"Uploads saved changes to the online repository right away. {appliesFromNow}"
                    Children =
                        SettingsPage.GitDefaultToggleSetting(
                            "SettingsGitDefaultUploadRightAway",
                            gitDefaults.Current.UploadRightAway,
                            gitDefaults.BuiltIn.UploadRightAway,
                            fun value -> applyGitDefault (fun current -> { current with UploadRightAway = value })
                        )
                }

                {
                    Title = "Diff indexing limit (MB)"
                    Description =
                        SettingsPageHelper.description
                            $"Limits how much of a file the diff reads in the background. {appliesFromNow}"
                    Children =
                        SettingsPage.GitDefaultNumberSetting(
                            "SettingsGitDefaultDiffIndexingLimit",
                            gitDefaults.Current.DiffIndexingLimitMb,
                            gitDefaults.BuiltIn.DiffIndexingLimitMb,
                            GitSettingRanges.MinDiffIndexingLimitMb,
                            GitSettingRanges.MaxDiffIndexingLimitMb,
                            fun value ->
                                applyGitDefault (fun current -> {
                                    current with
                                        DiffIndexingLimitMb = value
                                })
                        )
                }
            | None -> ()
        |]

        let largeFileFields = [|
            match gitDefaults with
            | Some gitDefaults ->
                {
                    Title = "Large file threshold (MB)"
                    Description =
                        SettingsPageHelper.description
                            $"Files larger than this are tracked with Git LFS. {appliesFromNow}"
                    Children =
                        SettingsPage.GitDefaultNumberSetting(
                            "SettingsGitDefaultLfsThreshold",
                            gitDefaults.Current.LfsAutoTrackThresholdMb,
                            gitDefaults.BuiltIn.LfsAutoTrackThresholdMb,
                            GitSettingRanges.MinLfsAutoTrackThresholdMb,
                            GitSettingRanges.MaxLfsAutoTrackThresholdMb,
                            fun value ->
                                applyGitDefault (fun current -> {
                                    current with
                                        LfsAutoTrackThresholdMb = value
                                })
                        )
                }

                {
                    Title = "Download large files"
                    Description =
                        SettingsPageHelper.description
                            $"Downloads the content of large files when you clone an ARC or download changes. {appliesFromNow}"
                    Children =
                        SettingsPage.GitDefaultToggleSetting(
                            "SettingsGitDefaultDownloadLargeFiles",
                            gitDefaults.Current.DownloadLargeFiles,
                            gitDefaults.BuiltIn.DownloadLargeFiles,
                            fun value ->
                                applyGitDefault (fun current -> {
                                    current with
                                        DownloadLargeFiles = value
                                })
                        )
                }
            | None -> ()
        |]

        let termSearchFields =
            termSearch.settings
            |> Array.map (fun setting -> {
                Title = setting.title
                Description = setting.description
                Children = setting.settingElement
            })

        // A category without settings is left out, together with its link.
        let categories =
            [|
                {
                    Category = SettingsCategory.General
                    Title = "General"
                    Fields = generalFields
                }
                {
                    Category = SettingsCategory.DataHub
                    Title = "DataHUB"
                    Fields = dataHubFields
                }
                {
                    Category = SettingsCategory.Git
                    Title = "Git"
                    Fields = gitFields
                }
                {
                    Category = SettingsCategory.LargeFiles
                    Title = "Large files"
                    Fields = largeFileFields
                }
                {
                    Category = SettingsCategory.TermSearch
                    Title = "Term search"
                    Fields = termSearchFields
                }
            |]
            |> Array.filter (fun settings -> settings.Fields.Length > 0)

        let initialCategory = defaultArg category SettingsCategory.General
        let active, setActive = React.useState initialCategory

        // The categories whose settings the navigation lists. Opening a page expands its category.
        let expanded, setExpanded = React.useState (Set.singleton initialCategory)

        let openCategory (category: SettingsCategory) =
            setActive category
            setExpanded (expanded.Add category)

        // General always has settings, so a category without settings falls back to the first.
        let shown =
            categories
            |> Array.tryFind (fun settings -> settings.Category = active)
            |> Option.defaultValue categories[0]

        let shownCategory = shown.Category

        let rootRef = React.useElementRef ()

        let scrollToElement (selector: string) (smooth: bool) =
            rootRef.current
            |> Option.bind (fun root -> root.querySelector selector |> Option.ofObj)
            |> Option.iter (fun element -> Dom.scrollIntoView element smooth)

        // A setting chosen on another page is scrolled to once that page shows.
        let pendingField = React.useRef (None: string option)

        React.useEffect (
            (fun () ->
                pendingField.current
                |> Option.iter (fun id ->
                    pendingField.current <- None
                    scrollToElement ("#" + id) false
                )
            ),
            [| box active |]
        )

        let toggleCategory (category: SettingsCategory) =
            setExpanded (
                if expanded.Contains category then
                    expanded.Remove category
                else
                    expanded.Add category
            )

        // A page opens at its top.
        let openPage (category: SettingsCategory) =
            rootRef.current |> Option.iter (fun root -> Dom.scrollIntoView root false)
            openCategory category

        let selectField (category: SettingsCategory) (index: int) =
            let id = SettingsPageHelper.fieldId category index

            if category = shownCategory then
                scrollToElement ("#" + id) true
            else
                pendingField.current <- Some id
                openCategory category

        Html.div [
            prop.ref rootRef
            prop.className "swt:@container/settings swt:w-full"
            prop.children [
                Html.div [
                    prop.className "swt:flex swt:flex-col swt:@3xl/settings:flex-row swt:@3xl/settings:items-start"
                    prop.children [
                        SettingsPage.Navigation(
                            categories,
                            shownCategory,
                            expanded,
                            openPage,
                            toggleCategory,
                            selectField
                        )
                        Html.div [
                            prop.className "swt:min-w-0 swt:flex-1"
                            prop.children [
                                LayoutComponents.Section [
                                    // A new page gets new fields, so no draft of one page shows on another.
                                    React.KeyedFragment(
                                        SettingsPageHelper.pageId shownCategory,
                                        [ SettingsPage.CategoryPage shown ]
                                    )
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent(true)>]
    static member Entry(?onAutoCreateNotesFolderEnabled: unit -> unit) =
        let onAutoCreateNotesFolderEnabled =
            defaultArg onAutoCreateNotesFolderEnabled ignore

        ThemeProvider.ThemeProvider(
            TermSearchConfigProvider.TIBQueryProvider(
                SettingsPage.SettingsPage(onAutoCreateNotesFolderEnabled = onAutoCreateNotesFolderEnabled)
            )
        )
