/// This module is used to update versions right before releasing in different files.
module VersionIO

open System
open System.IO
open Semver
open System.Text.RegularExpressions

// robust function, that looks in string[] for matching line and replaces it if found, returns bool*string []
// true if replaced, false if not found
let tryReplace (pattern: Regex) (replacement: string) (content: string[]) =
    let mutable wasReplaced = false

    let nextContent =
        content
        |> Array.map (fun line ->
            if pattern.IsMatch(line) then
                wasReplaced <- true
                replacement
            else
                line
        )

    wasReplaced, nextContent

let updateVersionFiles (version: Changelog.Version) =
    printGreenfn "Updating version files to %O" version.Version
    let serverPath = Path.Combine(ProjectInfo.ProjectPaths.serverPath, "Version.fs")
    let clientPath = Path.Combine(ProjectInfo.ProjectPaths.clientPath, "Version.fs")

    let nextReleaseDate =
        match version.Date with
        | Some date -> date.ToShortDateString()
        | None -> DateTime.UtcNow.ToShortDateString()

    let nextVersion = version.Version.ToString()

    // let AssemblyVersion = "0.0.0"
    let patternAssemblyVersion = @"^let AssemblyVersion = "".*"""
    // let AssemblyMetadata_ReleaseDate = "09.10.2025"
    let patternAssemblyMetadata_ReleaseDate =
        @"^let AssemblyMetadata_ReleaseDate = "".*"""

    let versionRegex = Regex(patternAssemblyVersion)
    let dateRegex = Regex(patternAssemblyMetadata_ReleaseDate)

    let replace (path: string) =
        let content = File.ReadAllLines(path)

        let newContent =
            match
                content
                |> tryReplace versionRegex (sprintf $"let AssemblyVersion = \"%s{nextVersion}\"")
            with
            | (true, newContent) ->
                match
                    newContent
                    |> tryReplace dateRegex (sprintf $"let AssemblyMetadata_ReleaseDate = \"%s{nextReleaseDate}\"")
                with
                | (true, finalContent) -> finalContent
                | (false, _) -> failwithf "Date line not found in version file: %s" path
            | (false, _) -> failwithf "Version line not found in version file: %s" path

        File.WriteAllLines(path, newContent)

    replace serverPath
    printGreenfn "Updated %s" serverPath
    replace clientPath
    printGreenfn "Updated %s" clientPath

let updateFSharpProjectVersions (version: Changelog.Version) =
    printGreenfn "Updating .fsproj files to version %O" version.Version

    let componentsProjectPath =
        Path.Combine(ProjectInfo.ProjectPaths.componentsPath, "src/Arcitisa.Components.fsproj")

    let sharedProjectPath =
        Path.Combine(ProjectInfo.ProjectPaths.sharedPath, "Arcitisa.Components.Core.fsproj")


    let nextVersion = version.Version.ToString()
    let versionRegexPattern = @"<PackageVersion>.*</PackageVersion>"

    let replace (path: string) =
        let content = File.ReadAllText(path)

        let nextContent =
            match Regex.Count(content, versionRegexPattern) with
            | 1 ->
                Regex.Replace(content, versionRegexPattern, sprintf "<PackageVersion>%s</PackageVersion>" nextVersion)
            | _ -> failwithf "Version line not found in version file: %s" path

        File.WriteAllText(path, nextContent)

    replace componentsProjectPath
    printGreenfn "Updated %s" componentsProjectPath
    replace sharedProjectPath
    printGreenfn "Updated %s" sharedProjectPath

let updateComponentsPackageJSONVersion (version: Changelog.Version) =
    run
        "npm"
        [
            "version"
            version.Version.ToString()
            "--allow-same-version"
            "--no-git-tag-version"
        ]
        ProjectInfo.ProjectPaths.componentsPath

    printfn "Updated %s/package.json to version %O" ProjectInfo.ProjectPaths.componentsPath version.Version

let updateElectronPackageJSONVersion (version: Changelog.Version) =
    run
        "npm"
        [
            "version"
            version.Version.ToString()
            "--allow-same-version"
            "--no-git-tag-version"
        ]
        ProjectInfo.ProjectPaths.electronPath

    printfn "Updated %s/package.json to version %O" ProjectInfo.ProjectPaths.electronPath version.Version

let updateAllVersionInformationInFiles (version: Changelog.Version) =
    updateVersionFiles version
    updateFSharpProjectVersions version
    updateComponentsPackageJSONVersion version
    updateElectronPackageJSONVersion version
