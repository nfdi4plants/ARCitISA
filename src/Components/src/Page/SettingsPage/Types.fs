module Swate.Components.PageComponents.SettingsPage.Types

/// What the host knows about the Git executable. ChosenPath is None while the automatic lookup is active.
type GitExecutableSetting = {
    ChosenPath: string option
    PathInUse: string option
    GitVersion: string option
    GitLfsVersion: string option
    Problem: string option
    IsChecking: bool
}
