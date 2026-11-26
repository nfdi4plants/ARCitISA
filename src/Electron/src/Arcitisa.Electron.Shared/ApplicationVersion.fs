module Arcitisa.Electron.Shared.ApplicationVersion

open Fable.Core

[<ImportDefault("../../../../CHANGELOG.md?raw")>]
let private changelog: string = jsNative

/// <summary>
/// Alternative define in vite.config from package.json. package.json version is set on release by build pipeline.
/// ```
/// // vite.config.ts
/// define: {
///   'import.meta.env.APP_VERSION': JSON.stringify(pkg.version),
/// }
/// ```
/// </summary>
let current =
    changelog.Split(Arcitisa.Components.ClipboardContract.Contract.LineBreaks, System.StringSplitOptions.None)
    |> Array.tryPick (fun line ->
        if line.StartsWith "## " then
            let heading = line.Substring 3
            let dateSeparatorIndex = heading.IndexOf " - "

            if dateSeparatorIndex > 0 then
                heading.Substring(0, dateSeparatorIndex)
                |> ARCtrl.Helper.SemVer.SemVer.tryOfString
                |> Option.map _.AsString()
            else
                None
        else
            None
    )
    |> Option.defaultValue "Unknown"

let windowTitle (arcName: string option) =
    match arcName with
    | Some name when not (System.String.IsNullOrWhiteSpace name) -> $"ARCitISA {current} — {name}"
    | _ -> $"ARCitISA {current}"
