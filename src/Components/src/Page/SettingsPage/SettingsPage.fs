namespace Swate.Components.PageComponents.SettingsPage

open System
open Feliz
open Fable.Core
open Fable.Core.JsInterop
open Swate.Components
open Swate.Components.Page.GitSidebarTypes
open Swate.Components.Primitive.LayoutComponents
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

    /// Compares field by field. Callers from JavaScript pass plain objects, and record equality
    /// does not match those against record instances.
    let sameGitDefaults (a: SettingsGitDefaults) (b: SettingsGitDefaults) =
        a.LfsAutoTrackThresholdMb = b.LfsAutoTrackThresholdMb
        && a.DownloadLargeFiles = b.DownloadLargeFiles
        && a.DiffIndexingLimitMb = b.DiffIndexingLimitMb
        && a.UploadRightAway = b.UploadRightAway

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
                "swt:input swt:w-32"
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

    [<ReactComponent>]
    static member private GitDefaults
        (
            gitDefaults: SettingsGitDefaults,
            builtIn: SettingsGitDefaults,
            onSave: SettingsGitDefaults -> unit,
            ?gitExecutable: ReactElement
        ) =
        let lfsInput, setLfsInput =
            React.useState (string gitDefaults.LfsAutoTrackThresholdMb)

        let downloadLargeFiles, setDownloadLargeFiles =
            React.useState gitDefaults.DownloadLargeFiles

        let diffInput, setDiffInput =
            React.useState (string gitDefaults.DiffIndexingLimitMb)

        let uploadRightAway, setUploadRightAway = React.useState gitDefaults.UploadRightAway

        React.useEffect (
            (fun () ->
                setLfsInput (string gitDefaults.LfsAutoTrackThresholdMb)
                setDownloadLargeFiles gitDefaults.DownloadLargeFiles
                setDiffInput (string gitDefaults.DiffIndexingLimitMb)
                setUploadRightAway gitDefaults.UploadRightAway
            ),
            [|
                box gitDefaults.LfsAutoTrackThresholdMb
                box gitDefaults.DownloadLargeFiles
                box gitDefaults.DiffIndexingLimitMb
                box gitDefaults.UploadRightAway
            |]
        )

        let lfsValue =
            SettingsPageHelper.tryParseInRange
                GitSettingRanges.MinLfsAutoTrackThresholdMb
                GitSettingRanges.MaxLfsAutoTrackThresholdMb
                lfsInput

        let diffValue =
            SettingsPageHelper.tryParseInRange
                GitSettingRanges.MinDiffIndexingLimitMb
                GitSettingRanges.MaxDiffIndexingLimitMb
                diffInput

        let draft =
            match lfsValue, diffValue with
            | Some lfs, Some diff ->
                Some {
                    LfsAutoTrackThresholdMb = lfs
                    DownloadLargeFiles = downloadLargeFiles
                    DiffIndexingLimitMb = diff
                    UploadRightAway = uploadRightAway
                }
            | _ -> None

        let canSave =
            match draft with
            | Some draft -> not (SettingsPageHelper.sameGitDefaults draft gitDefaults)
            | None -> false

        let lfsId = "SettingsGitDefaultLfsThreshold"
        let downloadId = "SettingsGitDefaultDownloadLargeFiles"
        let diffId = "SettingsGitDefaultDiffIndexingLimit"
        let uploadId = "SettingsGitDefaultUploadRightAway"
        let appliesFromNow = "This is the default for ARCs opened from now on."

        Html.div [
            prop.testId "SettingsGitDefaultsSection"
            prop.className "swt:contents"
            prop.children [
                LayoutComponents.BoxedField(
                    "Git defaults",
                    content = [
                        if gitExecutable.IsSome then
                            gitExecutable.Value

                        SettingsPage.GitDefaultRow(
                            lfsId,
                            "Large file threshold (MB)",
                            $"Files larger than this are tracked with Git LFS. {appliesFromNow}",
                            SettingsPage.GitDefaultNumberInput(
                                lfsId,
                                lfsInput,
                                setLfsInput,
                                GitSettingRanges.MinLfsAutoTrackThresholdMb,
                                GitSettingRanges.MaxLfsAutoTrackThresholdMb,
                                lfsValue.IsSome
                            ),
                            lfsValue <> Some builtIn.LfsAutoTrackThresholdMb,
                            (fun () -> setLfsInput (string builtIn.LfsAutoTrackThresholdMb)),
                            ?rangeHint =
                                (if lfsValue.IsNone then
                                     Some
                                         $"Enter a whole number from {GitSettingRanges.MinLfsAutoTrackThresholdMb} to {GitSettingRanges.MaxLfsAutoTrackThresholdMb}."
                                 else
                                     None)
                        )

                        SettingsPage.GitDefaultRow(
                            downloadId,
                            "Download large files",
                            $"Downloads the content of large files when you clone an ARC or download changes. {appliesFromNow}",
                            SettingsPage.GitDefaultToggle(downloadId, downloadLargeFiles, setDownloadLargeFiles),
                            downloadLargeFiles <> builtIn.DownloadLargeFiles,
                            (fun () -> setDownloadLargeFiles builtIn.DownloadLargeFiles)
                        )

                        SettingsPage.GitDefaultRow(
                            diffId,
                            "Diff indexing limit (MB)",
                            $"Limits how much of a file the diff reads in the background. {appliesFromNow}",
                            SettingsPage.GitDefaultNumberInput(
                                diffId,
                                diffInput,
                                setDiffInput,
                                GitSettingRanges.MinDiffIndexingLimitMb,
                                GitSettingRanges.MaxDiffIndexingLimitMb,
                                diffValue.IsSome
                            ),
                            diffValue <> Some builtIn.DiffIndexingLimitMb,
                            (fun () -> setDiffInput (string builtIn.DiffIndexingLimitMb)),
                            ?rangeHint =
                                (if diffValue.IsNone then
                                     Some
                                         $"Enter a whole number from {GitSettingRanges.MinDiffIndexingLimitMb} to {GitSettingRanges.MaxDiffIndexingLimitMb}."
                                 else
                                     None)
                        )

                        SettingsPage.GitDefaultRow(
                            uploadId,
                            "Upload right away",
                            $"Uploads saved changes to the online repository right away. {appliesFromNow}",
                            SettingsPage.GitDefaultToggle(uploadId, uploadRightAway, setUploadRightAway),
                            uploadRightAway <> builtIn.UploadRightAway,
                            (fun () -> setUploadRightAway builtIn.UploadRightAway)
                        )

                        Html.div [
                            prop.className "swt:flex swt:justify-end"
                            prop.children [
                                Html.button [
                                    prop.testId "SettingsGitDefaultsSave"
                                    prop.type'.button
                                    prop.className "swt:btn swt:btn-primary"
                                    prop.disabled (not canSave)
                                    prop.text "Save"
                                    prop.onClick (fun _ -> draft |> Option.iter onSave)
                                ]
                            ]
                        ]
                    ]
                )
            ]
        ]

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
                        "The Git program the application runs. Leave it on automatic to use the Git found on your PATH, or choose the executable yourself. Git LFS runs through this Git, so the Git LFS version shown belongs to it. A change here applies right away and needs no Save."
                ]
        )

    [<ReactComponent>]
    static member private General
        (
            ?onAutoCreateNotesFolderEnabled: unit -> unit,
            ?uiScaling: int,
            ?onUIScaling: int -> unit,
            ?autoCreateNotesFolder: bool,
            ?onAutoCreateNotesFolderChange: bool -> unit
        ) =
        let onAutoCreateNotesFolderEnabled =
            defaultArg onAutoCreateNotesFolderEnabled ignore

        LayoutComponents.BoxedField(
            "General",
            content = [
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
                | _, _ -> Html.none
            ]
        )

    [<ReactComponent>]
    static member private SearchConfig() =
        LayoutComponents.BoxedField(
            "Term Search Configuration",
            content = [
                TermSearchConfigSetter.TermSearchConfigSetter(fun props ->
                    SettingsPage.SettingColumnElement(
                        props.title,
                        props.settingElement,
                        description = props.description
                    )
                )
            ]
        )

    [<ReactComponent>]
    static member SettingsPage
        (
            ?onAutoCreateNotesFolderEnabled: unit -> unit,
            ?uiScaling: int,
            ?onUIScaling: int -> unit,
            ?gitDefaults: GitDefaultsSection,
            ?autoCreateNotesFolder: bool,
            ?onAutoCreateNotesFolderChange: bool -> unit,
            ?gitExecutable: GitExecutableField
        ) =
        let onAutoCreateNotesFolderEnabled =
            defaultArg onAutoCreateNotesFolderEnabled ignore

        let gitExecutable =
            gitExecutable
            |> Option.map (fun field ->
                SettingsPage.GitExecutable(field.Setting, field.OnChoose, field.OnUseAutomatic, field.OnCheckAgain)
            )

        LayoutComponents.Section [
            SettingsPage.General(
                onAutoCreateNotesFolderEnabled = onAutoCreateNotesFolderEnabled,
                ?uiScaling = uiScaling,
                ?onUIScaling = onUIScaling,
                ?autoCreateNotesFolder = autoCreateNotesFolder,
                ?onAutoCreateNotesFolderChange = onAutoCreateNotesFolderChange
            )

            // The Git executable leads the Git defaults. A host without Git defaults still shows it in the same box.
            match gitDefaults, gitExecutable with
            | Some gitDefaults, _ ->
                SettingsPage.GitDefaults(
                    gitDefaults.Current,
                    gitDefaults.BuiltIn,
                    gitDefaults.OnSave,
                    ?gitExecutable = gitExecutable
                )
            | None, Some gitExecutable -> LayoutComponents.BoxedField("Git defaults", content = [ gitExecutable ])
            | None, None -> Html.none

            SettingsPage.SearchConfig()

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
