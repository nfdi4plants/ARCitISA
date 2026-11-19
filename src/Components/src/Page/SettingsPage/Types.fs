module Swate.Components.PageComponents.SettingsPage.Types

open Swate.Components.Page.GitSidebarTypes

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

/// The custom DataHUB addresses the login offers next to the built-in ones. A change applies right away.
type CustomDataHubsField = {
    Urls: string[]
    OnChange: string[] -> unit
}

/// The Git defaults section: the saved values, the built-in values that Reset goes back to, and what Save does.
type GitDefaultsSection = {
    Current: SettingsGitDefaults
    BuiltIn: SettingsGitDefaults
    OnSave: SettingsGitDefaults -> unit
}
