module Arcitisa.Components.Api.GitHubReleases

open Fable.Core
open Fetch
open Arcitisa.Components.Util.SemVer
open ARCtrl.Helper.SemVer

[<CLIMutable>]
type Release = {
    tag_name: string
    name: string option
    body: string option
    draft: bool
    prerelease: bool
}

/// Repository is an owner/name pair, e.g. nfdi4plants/ARCitISA.
let repositoryPath (repository: string) =
    let parts = repository.Trim().Split('/')

    if parts.Length <> 2 || parts |> Array.exists System.String.IsNullOrWhiteSpace then
        invalidArg "repository" "Expected a GitHub repository in owner/name format."

    parts |> Array.map JS.encodeURIComponent |> String.concat "/"

let releaseUrl repository tag =
    $"https://github.com/{repositoryPath repository}/releases/tag/{JS.encodeURIComponent tag}"

let sortByVersion (releases: Release[]) =
    releases
    |> Array.filter (fun release -> not release.draft)
    |> Array.sortWith (fun a b ->
        match SemVer.tryParse a.tag_name, SemVer.tryParse b.tag_name with
        | Some a, Some b when SemVer.isOlder a b -> 1
        | Some a, Some b when SemVer.isOlder b a -> -1
        | Some _, None -> -1
        | None, Some _ -> 1
        | _ -> compare a.tag_name b.tag_name
    )

/// Stable installations stay on stable releases; prerelease installations also see previews.
let tryFindUpdate current releases =
    let acceptsPrerelease =
        SemVer.tryParse current
        |> Option.exists (fun version -> version.PreRelease.IsSome)

    releases
    |> sortByVersion
    |> Array.tryFind (fun release ->
        SemVer.isOlderVersion current release.tag_name
        && (acceptsPrerelease
            || (not release.prerelease
                && (SemVer.tryParse release.tag_name |> Option.exists (fun v -> v.PreRelease.IsNone))))
    )

let matchesVersion version (release: Release) =
    release.tag_name = version
    || (
        match SemVer.tryParse version, SemVer.tryParse release.tag_name with
        | Some a, Some b -> SemVer.isEqualWithoutBuild a b
        | _ -> false
    )

/// Fetch every page so older releases remain available in the changelog selector.
let loadAll repository =
    let path = repositoryPath repository

    let rec loadPage page = promise {
        let! response =
            fetch $"https://api.github.com/repos/{path}/releases?per_page=100&page={page}" [
                requestHeaders [ HttpRequestHeaders.Accept "application/vnd.github+json" ]
            ]

        if not response.Ok then
            failwith $"GitHub could not load releases (HTTP {response.Status}). Please try again later."
        // let! jsonObj = response.json()
        // Browser.Dom.console.log(jsonObj)
        let! releases = response.json<Release[]> ()

        if releases.Length = 100 then
            let! rest = loadPage (page + 1)
            return Array.append releases rest
        else
            return releases
    }

    promise {
        let! releases = loadPage 1
        Browser.Dom.console.log ($"Loaded all releases, total count: {releases.Length}")
        return releases
    }
