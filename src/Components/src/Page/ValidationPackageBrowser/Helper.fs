module Arcitisa.Components.Page.ValidationPackageBrowser.Helper

open ARCtrl.ValidationPackages
open Types
open ARCtrl.Helper.SemVer

open Arcitisa.Components.Util.SemVer

let pageSize = 20

let hasFlag (fields: SearchFields) (flag: SearchFields) = (int fields &&& int flag) = int flag

let toggleFlag (fields: SearchFields) (flag: SearchFields) =
    enum<SearchFields> (int fields ^^^ int flag)

let allSearchFields = [|
    SearchFields.Name
    SearchFields.Summary
    SearchFields.Description
    SearchFields.Tags
    SearchFields.Authors
|]

let searchFieldLabel (field: SearchFields) =
    match field with
    | SearchFields.Name -> "Name"
    | SearchFields.Summary -> "Summary"
    | SearchFields.Description -> "Description"
    | SearchFields.Tags -> "Tags"
    | SearchFields.Authors -> "Authors"
    | _ -> ""

let toVersionString (dto: ValidationPackageDTO) =
    let baseVersion = $"{dto.MajorVersion}.{dto.MinorVersion}.{dto.PatchVersion}"

    [|
        if dto.PreReleaseVersionSuffix <> "" then
            $"-{dto.PreReleaseVersionSuffix}"
        if dto.BuildMetadataVersionSuffix <> "" then
            $"+{dto.BuildMetadataVersionSuffix}"
    |]
    |> String.concat ""
    |> fun suffix -> baseVersion + suffix

let rowState (config: ValidationPackagesConfig) (dto: ValidationPackageDTO) =
    let configPackage =
        config.ValidationPackages |> Seq.tryFind (fun vp -> vp.Name = dto.Name)

    match configPackage with
    | Some p ->
        match p.Version with
        | Some v ->
            let configSemVer = ARCtrl.Helper.SemVer.SemVer.tryOfString v
            let dtoSemVer = ARCtrl.Helper.SemVer.SemVer.tryOfString (toVersionString dto)

            match configSemVer, dtoSemVer with
            | _, None ->
                // If the version in the DTO is not a valid SemVer, we consider it unchecked, and log an error
                Browser.Dom.console.error ($"Invalid SemVer in DTO for package {dto.Name}: {toVersionString dto}")
                PackageRowState.Unchecked
            | None, _ ->
                // If the version in the config is not a valid SemVer, we consider it unchecked
                PackageRowState.Unchecked
            | Some configSemVer, Some dtoSemVer ->
                if SemVer.isEqualWithoutBuild configSemVer dtoSemVer then
                    PackageRowState.Checked // The versions are equal, so the package is checked
                elif SemVer.isOlder configSemVer dtoSemVer then
                    PackageRowState.HasOlderVersion // The config version is older than the DTO version, so the package has an older version
                else
                    PackageRowState.InvalidVersion // The config version is newer than the DTO version, which is unexpected
        // To my understanding no version means, always take latest, so we can consider it checked
        // I think it might be best if ARCitISA does list specific versions, so we will never actually write this case.
        | None -> PackageRowState.Checked
    | None ->
        // Not found in current config
        PackageRowState.Unchecked

let private containsIgnoreCase (haystack: string) (needle: string) =
    haystack.ToLowerInvariant().Contains(needle.ToLowerInvariant())

let filterBySearch (fields: SearchFields) (query: string) (packages: ValidationPackageDTO[]) =
    if query = "" || int fields = 0 then
        packages
    else
        packages
        |> Array.filter (fun dto ->
            let matchesName =
                hasFlag fields SearchFields.Name && containsIgnoreCase dto.Name query

            let matchesSummary =
                hasFlag fields SearchFields.Summary && containsIgnoreCase dto.Summary query

            let matchesDescription =
                hasFlag fields SearchFields.Description
                && containsIgnoreCase dto.Description query

            let matchesTags =
                hasFlag fields SearchFields.Tags
                && dto.Tags
                   |> Array.exists (fun t ->
                       t.Name
                       |> Option.map (fun name -> containsIgnoreCase name query)
                       |> Option.defaultValue false
                   )

            let matchesAuthors =
                hasFlag fields SearchFields.Authors
                && dto.Authors
                   |> Array.exists (fun a ->
                       a.FullName
                       |> Option.map (fun name -> containsIgnoreCase name query)
                       |> Option.defaultValue false
                   )

            matchesName
            || matchesSummary
            || matchesDescription
            || matchesTags
            || matchesAuthors
        )

let filterByTag (tag: string option) (packages: ValidationPackageDTO[]) =
    match tag with
    | None -> packages
    | Some tag ->
        packages
        |> Array.filter (fun dto -> dto.Tags |> Array.exists (fun t -> t.Name = Some tag))

let filterByAuthor (author: string option) (packages: ValidationPackageDTO[]) =
    match author with
    | None -> packages
    | Some author ->
        packages
        |> Array.filter (fun dto -> dto.Authors |> Array.exists (fun a -> a.FullName = Some author))

let private checkedRank (state: PackageRowState) =
    match state with
    | PackageRowState.Checked -> 0
    | PackageRowState.HasOlderVersion -> 1
    | PackageRowState.InvalidVersion -> 2
    | PackageRowState.Unchecked -> 3

let sortByChecked (sort: CheckedSort) (rowStateMap: Map<string, PackageRowState>) (packages: ValidationPackageDTO[]) =
    let rowStateOf (dto: ValidationPackageDTO) =
        match rowStateMap.TryFind dto.Name with
        | Some state -> state
        | None -> PackageRowState.Unchecked

    match sort with
    | CheckedSort.None -> packages
    | CheckedSort.CheckedFirst -> packages |> Array.sortBy (fun dto -> dto |> rowStateOf |> checkedRank)
    | CheckedSort.CheckedLast -> packages |> Array.sortByDescending (fun dto -> dto |> rowStateOf |> checkedRank)

let nextCheckedSort (sort: CheckedSort) =
    match sort with
    | CheckedSort.None -> CheckedSort.CheckedFirst
    | CheckedSort.CheckedFirst -> CheckedSort.CheckedLast
    | CheckedSort.CheckedLast -> CheckedSort.None

let distinctTags (packages: ValidationPackageDTO[]) =
    packages
    |> Array.collect (fun dto -> dto.Tags |> Array.choose (fun t -> t.Name))
    |> Array.distinct
    |> Array.sort

let distinctAuthors (packages: ValidationPackageDTO[]) =
    packages
    |> Array.collect (fun dto -> dto.Authors |> Array.choose (fun a -> a.FullName))
    |> Array.distinct
    |> Array.sort

let pageCount (packages: ValidationPackageDTO[]) =
    if packages.Length = 0 then
        0
    else
        (packages.Length + pageSize - 1) / pageSize

let slicePage (packages: ValidationPackageDTO[]) (page: int) =
    let startIndex = page * pageSize

    if startIndex >= packages.Length then
        [||]
    else
        let count = min pageSize (packages.Length - startIndex)
        packages.[startIndex .. startIndex + count - 1]

let unlistedNames (config: ValidationPackagesConfig) (packages: ValidationPackageDTO[]) =
    let tableNames = packages |> Array.map (fun p -> p.Name) |> Set.ofArray

    config.ValidationPackages
    |> Seq.map (fun (vp: ValidationPackage) -> vp.Name)
    |> Seq.distinct
    |> Seq.filter (fun name -> not (tableNames.Contains name))
    |> Seq.toArray

/// The package registry returns all versions of each package. This keeps only the newest version per package name.
let latestVersions (packages: ValidationPackageDTO[]) =
    packages
    // ensure all SemVer are valid
    |> Array.filter (fun current ->
        ARCtrl.Helper.SemVer.SemVer.tryOfString (toVersionString current)
        |> Option.isSome
    )
    |> Array.groupBy (fun p -> p.Name)
    |> Array.map (fun (_, versions) ->
        versions
        |> Array.reduce (fun current candidate ->
            let currentSemVer = current.ToSemVer()

            let candidateSemVer = candidate.ToSemVer()

            if SemVer.isOlder currentSemVer candidateSemVer then
                candidate
            else
                current
        )
    )

let computeNewPackages
    (config: ValidationPackagesConfig)
    (packages: ValidationPackageDTO[])
    (edits: Map<string, ValidationPackage option>)
    (removedUnlisted: Set<string>)
    =
    let tableNames = packages |> Array.map (fun p -> p.Name) |> Set.ofArray

    let result = ResizeArray<ValidationPackage>()
    let mutable editedNamesSeen = Set.empty<string>

    for vp in config.ValidationPackages do
        if removedUnlisted.Contains vp.Name then
            ()
        elif tableNames.Contains vp.Name && edits.ContainsKey vp.Name then
            if not (editedNamesSeen.Contains vp.Name) then
                editedNamesSeen <- editedNamesSeen.Add vp.Name

                match edits.[vp.Name] with
                | Some p -> result.Add p
                | None -> ()
        else
            result.Add vp

    let resultNames =
        result |> Seq.map (fun (vp: ValidationPackage) -> vp.Name) |> Set.ofSeq

    for dto in packages do
        match edits.TryFind dto.Name with
        | Some(Some p) when not (resultNames.Contains dto.Name) -> result.Add p
        | _ -> ()

    result |> Seq.toArray
