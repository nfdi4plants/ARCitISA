/// Version control dependencies used to build the main process session host.
module Main.VersionControl.VersionControlRuntime

open VersionControlService.Abstractions

type VersionControlRuntime = {
    /// Every provider of the app with the functions the app subscribed to.
    Providers: ProviderComposition.AppProvider[]
    PathCaseSensitivity: PathCaseSensitivity
    /// What a new opened repository starts from. The host calls it when it opens a repository, and it
    /// also serves roots without one (the settings of such a root and the diff storage policy
    /// fallback). An opened repository keeps the values it started with when the user defaults change.
    SessionDefaults: unit -> Main.UserDefaults.SessionDefaults
}

/// Creates production dependencies after the app is ready, when the settings root exists.
/// The session host is built after the runtime, so the caller passes its window lookup.
let createProduction (windowOwnerOf: OperationContext -> string) : VersionControlRuntime =
    let sensitivity = ProviderComposition.currentPathCaseSensitivity ()

    {
        Providers = ProviderComposition.createProductionProviders sensitivity windowOwnerOf
        PathCaseSensitivity = sensitivity
        SessionDefaults = fun () -> Main.UserDefaults.sessionDefaultsOf Main.UserDefaults.production
    }
