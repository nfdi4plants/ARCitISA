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

    [<ReactComponent>]
    static member private SettingColumnElement
        (title: string, settingElement: ReactElement, ?description: ReactElement)
        =
        Html.div [
            prop.className "swt:grid swt:grid-cols-1 swt:md:grid-cols-2 swt:gap-2 swt:py-2"
            prop.children [
                Html.p [ prop.className "swt:text-xl not-prose"; prop.text title ]
                Html.div [
                    prop.className "not-prose"
                    prop.children [ settingElement ]
                ]
                if description.IsSome then
                    Html.div [
                        prop.className "swt:text-sm swt:text-base-content/70 swt:md:col-span-2 swt:prose"
                        prop.children description.Value
                    ]
            ]
        ]

    [<ReactComponent>]
    static member private AutoCreateNotesFolderToggle(isEnabled: bool, onChange: bool -> unit) =
        SettingsPage.SettingColumnElement(
            "Automatically create notes folder",
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
            ],
            description =
                Html.p [
                    prop.className "swt:mt-1 swt:text-sm swt:text-base-content/70"
                    prop.text
                        "Automatically creates an optional /notes folder when an ARC is opened or created. Disable this here if you do not want automatic notes scaffolding."
                ]
        )

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

    [<ReactComponent>]
    static member private GitDefaultRow
        (
            rowId: string,
            title: string,
            description: string,
            control: ReactElement,
            isChanged: bool,
            onReset: unit -> unit,
            ?rangeHint: string
        ) =
        SettingsPage.SettingColumnElement(
            title,
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
            ],
            description =
                Html.p [
                    prop.className "swt:mt-1 swt:text-sm swt:text-base-content/70"
                    prop.text description
                ]
        )

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
    static member private GitDefaultToggleSetting
        (rowId: string, title: string, description: string, value: bool, builtIn: bool, onApply: bool -> unit)
        =
        SettingsPage.GitDefaultRow(
            rowId,
            title,
            description,
            SettingsPage.GitDefaultToggle(rowId, value, onApply),
            value <> builtIn,
            (fun () -> onApply builtIn)
        )

    /// A number that applies with Update, like the UI scaling. Reset applies the built-in value right away.
    [<ReactComponent>]
    static member private GitDefaultNumberSetting
        (
            rowId: string,
            title: string,
            description: string,
            value: int,
            builtIn: int,
            minValue: int,
            maxValue: int,
            onApply: int -> unit
        ) =
        let draft, setDraft = React.useState (string value)

        React.useEffect ((fun () -> setDraft (string value)), [| box value |])

        let parsed = SettingsPageHelper.tryParseInRange minValue maxValue draft

        SettingsPage.GitDefaultRow(
            rowId,
            title,
            description,
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
    static member private UIScalingSetting(uiScaling: int, onUIScaling: int -> unit) =
        let scalingDraft, setScalingDraft = React.useState uiScaling

        React.useEffect ((fun () -> setScalingDraft uiScaling), [| box uiScaling |])

        let modifierKey =
            match getUserOS () with
            | MacOS
            | IOS -> "⌘"
            | _ -> "Ctrl"

        SettingsPage.SettingColumnElement(
            "UI Scaling",
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
            ],
            description =
                Html.div [
                    Html.p [
                        prop.className "swt:mt-1 swt:text-sm swt:text-base-content/70"
                        prop.text
                            "Allows adjusting the scaling of the user interface. 100% represents the default size."
                    ]
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
        )

    [<ReactComponent>]
    static member private GitExecutable
        (setting: GitExecutableSetting, onChoose: unit -> unit, onUseAutomatic: unit -> unit, onCheckAgain: unit -> unit) =
        let source =
            match setting.ChosenPath with
            | Some path -> path
            | None -> "Automatic (Git found on PATH)"

        SettingsPage.SettingColumnElement(
            "Git executable",
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
            ],
            description =
                Html.p [
                    prop.className "swt:mt-1 swt:text-sm swt:text-base-content/70"
                    prop.text
                        "The Git program the application runs. Leave it on automatic to use the Git found on your PATH, or choose the executable yourself. Git LFS runs through this Git, so the Git LFS version shown belongs to it."
                ]
        )

    [<ReactComponent>]
    static member private DataHubItem(url: string, onRemove: unit -> unit) =
        Html.li [
            prop.className "swt:flex swt:items-center swt:gap-2"
            prop.children [
                Html.span [
                    prop.testId "SettingsDataHub"
                    prop.className "swt:min-w-0 swt:break-all swt:text-sm"
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

        SettingsPage.SettingColumnElement(
            "DataHUB addresses",
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
            ],
            description =
                Html.p [
                    prop.className "swt:mt-1 swt:text-sm swt:text-base-content/70"
                    prop.text
                        "The sign-in offers these under Other options, next to the official PLANTdataHUB. New addresses are added when you sign in."
                ]
        )

    [<ReactComponent>]
    static member private SearchConfig() =
        TermSearchConfigSetter.TermSearchConfigSetter(fun props ->
            SettingsPage.SettingColumnElement(props.title, props.settingElement, description = props.description)
        )

    /// One category: a box with the category's title. Its id is the target of the navigation.
    [<ReactComponent>]
    static member private CategorySection(category: SettingsCategory, content: ReactElement list) =
        let sectionId = SettingsCategory.sectionId category

        Html.div [
            prop.id sectionId
            prop.testId sectionId
            prop.className "swt:flex swt:w-full swt:justify-center swt:scroll-mt-4"
            prop.children [
                LayoutComponents.BoxedField(SettingsCategory.title category, content = content)
            ]
        ]

    /// Links to the categories. It narrows with the page and is left out where the page is too narrow.
    [<ReactComponent>]
    static member private Navigation
        (categories: SettingsCategory list, active: SettingsCategory, onSelect: SettingsCategory -> unit)
        =
        Html.nav [
            prop.testId "SettingsNavigation"
            prop.ariaLabel "Settings categories"
            prop.className
                "swt:hidden swt:@3xl/settings:block swt:sticky swt:top-0 swt:shrink-0 swt:py-4 swt:pl-4 swt:w-40 swt:@5xl/settings:w-56"
            prop.children [
                Html.ul [
                    prop.className
                        "swt:menu swt:menu-sm swt:@5xl/settings:menu-md swt:w-full swt:bg-base-200 swt:rounded-box swt:[--menu-active-bg:color-mix(in_oklab,var(--color-primary)_10%,transparent)] swt:[--menu-active-fg:var(--color-primary)]"
                    prop.children [
                        for category in categories do
                            Html.li [
                                Html.button [
                                    prop.testId $"SettingsNavigation-{SettingsCategory.sectionId category}"
                                    prop.type'.button
                                    if category = active then
                                        prop.className "swt:font-semibold swt:shadow-none"
                                        prop.custom ("aria-current", "location")
                                    prop.text (SettingsCategory.title category)
                                    prop.onClick (fun _ -> onSelect category)
                                ]
                            ]
                    ]
                ]
            ]
        ]

    /// The settings in categories, with a navigation at the side. With a category the page opens at it.
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

        let appliesFromNow = "This is the default for ARCs opened from now on."

        let applyGitDefault (update: SettingsGitDefaults -> SettingsGitDefaults) =
            gitDefaults
            |> Option.iter (fun gitDefaults -> gitDefaults.OnChange(update gitDefaults.Current))

        let generalRows = [
            SettingsPage.SettingColumnElement(
                "Theme",
                ThemeSelector.ThemeSelector(),
                description =
                    Html.p [
                        prop.className "swt:mt-1 swt:text-sm swt:text-base-content/70"
                        prop.text
                            "Select the theme for the application. The 'Auto' option will use the system's theme settings."
                    ]
            )

            SettingsPage.AutoCreateNotesFolderSetting(
                ?autoCreateNotesFolder = autoCreateNotesFolder,
                ?onChange = onAutoCreateNotesFolderChange,
                onEnabled = onAutoCreateNotesFolderEnabled
            )

            match uiScaling, onUIScaling with
            | Some uiScaling, Some onUIScaling -> SettingsPage.UIScalingSetting(uiScaling, onUIScaling)
            | _ -> ()
        ]

        let gitRows = [
            match gitExecutable with
            | Some field ->
                SettingsPage.GitExecutable(field.Setting, field.OnChoose, field.OnUseAutomatic, field.OnCheckAgain)
            | None -> ()
            match gitDefaults with
            | Some gitDefaults ->
                SettingsPage.GitDefaultToggleSetting(
                    "SettingsGitDefaultUploadRightAway",
                    "Upload right away",
                    $"Uploads saved changes to the online repository right away. {appliesFromNow}",
                    gitDefaults.Current.UploadRightAway,
                    gitDefaults.BuiltIn.UploadRightAway,
                    fun value -> applyGitDefault (fun current -> { current with UploadRightAway = value })
                )

                SettingsPage.GitDefaultNumberSetting(
                    "SettingsGitDefaultDiffIndexingLimit",
                    "Diff indexing limit (MB)",
                    $"Limits how much of a file the diff reads in the background. {appliesFromNow}",
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
            | None -> ()
        ]

        let largeFileRows = [
            match gitDefaults with
            | Some gitDefaults ->
                SettingsPage.GitDefaultNumberSetting(
                    "SettingsGitDefaultLfsThreshold",
                    "Large file threshold (MB)",
                    $"Files larger than this are tracked with Git LFS. {appliesFromNow}",
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

                SettingsPage.GitDefaultToggleSetting(
                    "SettingsGitDefaultDownloadLargeFiles",
                    "Download large files",
                    $"Downloads the content of large files when you clone an ARC or download changes. {appliesFromNow}",
                    gitDefaults.Current.DownloadLargeFiles,
                    gitDefaults.BuiltIn.DownloadLargeFiles,
                    fun value ->
                        applyGitDefault (fun current -> {
                            current with
                                DownloadLargeFiles = value
                        })
                )
            | None -> ()
        ]

        let dataHubRows = [
            match dataHubs with
            | Some field -> SettingsPage.DataHubs(field.Urls, field.OnChange)
            | None -> ()
        ]

        // A category without rows is left out, together with its link.
        let sections =
            [
                SettingsCategory.General, generalRows
                SettingsCategory.DataHub, dataHubRows
                SettingsCategory.Git, gitRows
                SettingsCategory.LargeFiles, largeFileRows
                SettingsCategory.TermSearch, [ SettingsPage.SearchConfig() ]
            ]
            |> List.filter (snd >> List.isEmpty >> not)

        let categories = sections |> List.map fst

        let categoryKey =
            categories |> List.map SettingsCategory.sectionId |> String.concat " "

        let rootRef = React.useElementRef ()

        let active, setActive =
            React.useState (defaultArg category SettingsCategory.General)

        // A category chosen in the navigation or by the host stays marked while the page scrolls to it, even if
        // the page ends before its box reaches the top. The user's own scrolling gives the mark back to the
        // observer.
        let pinned = React.useRef false

        let sectionOf (root: Browser.Types.HTMLElement) (category: SettingsCategory) =
            root.querySelector ("#" + SettingsCategory.sectionId category) |> Option.ofObj

        let jumpTo (smooth: bool) (category: SettingsCategory) =
            pinned.current <- true
            setActive category

            rootRef.current
            |> Option.bind (fun root -> sectionOf root category)
            |> Option.iter (fun section -> Dom.scrollIntoView section smooth)

        React.useEffect ((fun () -> category |> Option.iter (jumpTo false)), [| box category |])

        let pageEndRef = React.useElementRef ()

        // Marks the topmost category with a part in the upper third of the view. At the end of the page the last
        // boxes can't reach the top, so there the last category takes the mark.
        React.useEffect (
            (fun () ->
                match rootRef.current, pageEndRef.current with
                | Some root, Some pageEnd when Dom.IntersectionObserver.isAvailable ->
                    let inUpperThird = System.Collections.Generic.HashSet<string>()
                    let atPageEnd = ref false

                    let mark () =
                        if not pinned.current then
                            let inView =
                                categories
                                |> List.filter (fun category ->
                                    inUpperThird.Contains(SettingsCategory.sectionId category)
                                )

                            (if atPageEnd.Value && not (List.contains (List.head categories) inView) then
                                 List.tryLast categories
                             else
                                 List.tryHead inView)
                            |> Option.iter setActive

                    let sectionObserver =
                        Dom.IntersectionObserver(
                            (fun entries ->
                                for entry in entries do
                                    if entry.isIntersecting then
                                        inUpperThird.Add entry.target.id |> ignore
                                    else
                                        inUpperThird.Remove entry.target.id |> ignore

                                mark ()
                            ),
                            {| rootMargin = "0px 0px -67% 0px" |}
                        )

                    let pageEndObserver =
                        Dom.IntersectionObserver(
                            (fun entries ->
                                for entry in entries do
                                    atPageEnd.Value <- entry.isIntersecting

                                mark ()
                            ),
                            {| rootMargin = "0px" |}
                        )

                    for category in categories do
                        sectionOf root category |> Option.iter sectionObserver.observe

                    pageEndObserver.observe pageEnd

                    let unpin (_: Browser.Types.Event) = pinned.current <- false
                    let userScrollEvents = [ "wheel"; "touchstart"; "keydown"; "pointerdown" ]

                    for eventName in userScrollEvents do
                        Browser.Dom.document.addEventListener (eventName, unpin)

                    (fun () ->
                        sectionObserver.disconnect ()
                        pageEndObserver.disconnect ()

                        for eventName in userScrollEvents do
                            Browser.Dom.document.removeEventListener (eventName, unpin)
                    )
                | _ -> ignore
            ),
            [| box categoryKey |]
        )

        Html.div [
            prop.ref rootRef
            prop.className "swt:@container/settings swt:w-full"
            prop.children [
                Html.div [
                    prop.className "swt:flex swt:items-start"
                    prop.children [
                        SettingsPage.Navigation(categories, active, jumpTo true)
                        Html.div [
                            prop.className "swt:min-w-0 swt:flex-1"
                            prop.children [
                                LayoutComponents.Section [
                                    for category, content in sections do
                                        SettingsPage.CategorySection(category, content)
                                ]
                                Html.div [ prop.ref pageEndRef ]
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
