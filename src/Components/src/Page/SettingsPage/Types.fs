module Arcitisa.Components.PageComponents.SettingsPage.Types

open Fable.Core
open Arcitisa.Components.Page.GitSidebarTypes

/// The categories of the settings page. Other parts of the app open the page at one of them.
[<StringEnum; RequireQualifiedAccess>]
type SettingsCategory =
    | General
    | DataHub
    | Git
    | LargeFiles
    | TermSearch

/// One setting: its title, what it does and its control.
type SettingField = {
    Title: string
    Description: Feliz.ReactElement
    Children: Feliz.ReactElement
}

/// A category with its settings. Each category is one page of the settings.
type CategorySettings = {
    /// What other parts of the app open the page with.
    Category: SettingsCategory
    Title: string
    Fields: SettingField[]
}

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
