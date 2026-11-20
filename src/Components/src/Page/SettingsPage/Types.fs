module Swate.Components.PageComponents.SettingsPage.Types

open Fable.Core
open Swate.Components.Page.GitSidebarTypes

/// The categories of the settings page. Other parts of the app open the page at one of them.
[<StringEnum; RequireQualifiedAccess>]
type SettingsCategory =
    | General
    | DataHub
    | Git
    | LargeFiles
    | TermSearch

module SettingsCategory =

    let title (category: SettingsCategory) =
        match category with
        | SettingsCategory.General -> "General"
        | SettingsCategory.DataHub -> "DataHUB"
        | SettingsCategory.Git -> "Git"
        | SettingsCategory.LargeFiles -> "Large files"
        | SettingsCategory.TermSearch -> "Term search"

    /// The id of the category's section on the page.
    let sectionId (category: SettingsCategory) =
        match category with
        | SettingsCategory.General -> "settings-general"
        | SettingsCategory.DataHub -> "settings-datahub"
        | SettingsCategory.Git -> "settings-git"
        | SettingsCategory.LargeFiles -> "settings-large-files"
        | SettingsCategory.TermSearch -> "settings-term-search"

/// What the host knows about the Git executable. ChosenPath is None while the automatic lookup is active.
type GitExecutableSetting = {
    ChosenPath: string option
    PathInUse: string option
    GitVersion: string option
    GitLfsVersion: string option
    Problem: string option
    IsChecking: bool
}

/// The Git executable field: what the host knows about Git and what the buttons do. The parts only work together,
/// so the host passes all of them or none.
type GitExecutableField = {
    Setting: GitExecutableSetting
    OnChoose: unit -> unit
    OnUseAutomatic: unit -> unit
    OnCheckAgain: unit -> unit
}

/// The DataHUB addresses the sign-in offers next to the official one. A change applies right away.
type DataHubsField = {
    Urls: string[]
    OnChange: string[] -> unit
}

/// The Git defaults: the saved values, the built-in values that Reset goes back to, and what a change does.
/// A change applies right away.
type GitDefaultsSection = {
    Current: SettingsGitDefaults
    BuiltIn: SettingsGitDefaults
    OnChange: SettingsGitDefaults -> unit
}
