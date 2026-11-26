module internal Arcitisa.Components.Tests.ValidationPackageBrowser.Helper

open ARCtrl.ValidationPackages
open Arcitisa.Components.Page.ValidationPackageBrowser.Helper
open Arcitisa.Components.Page.ValidationPackageBrowser.Types
open Vitest
open ARCtrl.Helper.SemVer
open Arcitisa.Components.Util.SemVer

let private mkTag (tagName: string) : OntologyAnnotationDTO = {
    Name = Some tagName
    TermSourceREF = None
    TermAccessionNumber = None
}

let private mkAuthor (fullName: string) : AuthorDTO = {
    FullName = Some fullName
    Email = None
    Affiliation = None
    AffiliationLink = None
}

type ValidationPackageDTO with

    static member mkDefault
        (
            ?name: string,
            ?summary: string,
            ?description,
            ?majorVersion: int,
            ?minorVersion: int,
            ?patchVersion: int,
            ?preReleaseVersionSuffix: string,
            ?buildMetadataVersionSuffix: string,
            ?tags: OntologyAnnotationDTO[],
            ?authors: AuthorDTO[]
        ) : ValidationPackageDTO =
        {
            Name = defaultArg name "Pkg"
            Summary = defaultArg summary "Summary text"
            Description = defaultArg description "Description text"
            MajorVersion = defaultArg majorVersion 1
            MinorVersion = defaultArg minorVersion 0
            PatchVersion = defaultArg patchVersion 0
            PreReleaseVersionSuffix = defaultArg preReleaseVersionSuffix ""
            BuildMetadataVersionSuffix = defaultArg buildMetadataVersionSuffix ""
            ReleaseDate = System.DateTime.UtcNow
            Tags = defaultArg tags [||]
            ReleaseNotes = ""
            CQCHookEndpoint = ""
            Authors = defaultArg authors [||]
            ProgrammingLanguage = "python"
        }

let private mkConfig (packages: (string * string option)[]) =
    packages
    |> Array.map (fun (pkgName, version) -> ValidationPackage(pkgName, ?version = version))
    |> ResizeArray
    |> fun arr -> ValidationPackagesConfig.make arr None

let private latest (pkgName: string) (version: string) =
    ValidationPackage(pkgName, ?version = Some version)


Vitest.describe (
    "Ensure SemVer comparison works as expected",
    fun () ->
        Vitest.test (
            "SemVer comparison functions",
            fun () ->
                let v1 = ARCtrl.Helper.SemVer.SemVer.tryOfString "1.0.0"
                let v2 = ARCtrl.Helper.SemVer.SemVer.tryOfString "1.0.1"
                let v3 = ARCtrl.Helper.SemVer.SemVer.tryOfString "1.0.0-alpha"
                let v4 = ARCtrl.Helper.SemVer.SemVer.tryOfString "1.0.0+build"

                Vitest.expect(SemVer.isEqualWithoutBuild v1.Value v1.Value).toBe true
                Vitest.expect(SemVer.isEqualWithoutBuild v1.Value v4.Value).toBe true
                Vitest.expect(SemVer.isOlder v1.Value v2.Value).toBe true
                Vitest.expect(SemVer.isOlder v1.Value v1.Value).toBe false
                Vitest.expect(SemVer.isOlder v3.Value v1.Value).toBe true
                Vitest.expect(SemVer.isOlder v4.Value v1.Value).toBe false
        )
)

Vitest.describe (
    "hasFlag / toggleFlag",
    fun () ->

        Vitest.test (
            "hasFlag detects single and combined flags",
            fun () ->
                Vitest.expect(hasFlag SearchFields.Name SearchFields.Name).toBe true
                Vitest.expect(hasFlag SearchFields.Name SearchFields.Summary).toBe false
                Vitest.expect(hasFlag (SearchFields.Name ||| SearchFields.Summary) SearchFields.Summary).toBe true
                Vitest.expect(hasFlag (enum<SearchFields> 0) SearchFields.Name).toBe false
        )

        Vitest.test (
            "toggleFlag flips a flag",
            fun () ->
                Vitest
                    .expect(toggleFlag SearchFields.Name SearchFields.Summary)
                    .toEqual (SearchFields.Name ||| SearchFields.Summary)

                Vitest.expect(toggleFlag (SearchFields.Name ||| SearchFields.Summary) SearchFields.Summary).toEqual
                    SearchFields.Name
        )
)

Vitest.describe (
    "toVersionString",
    fun () ->

        Vitest.test (
            "formats plain versions",
            fun () ->
                Vitest
                    .expect(
                        toVersionString (
                            ValidationPackageDTO.mkDefault (majorVersion = 1, minorVersion = 2, patchVersion = 3)
                        )
                    )
                    .toBe ("1.2.3")
        )

        Vitest.test (
            "appends prerelease and build metadata",
            fun () ->
                Vitest
                    .expect(
                        toVersionString (
                            ValidationPackageDTO.mkDefault (
                                majorVersion = 2,
                                minorVersion = 0,
                                patchVersion = 0,
                                preReleaseVersionSuffix = "alpha.1",
                                buildMetadataVersionSuffix = "7"
                            )
                        )
                    )
                    .toBe ("2.0.0-alpha.1+7")
        )
)

Vitest.describe (
    "rowState",
    fun () ->

        Vitest.test (
            "is Unchecked when name absent",
            fun () ->
                Vitest.expect(rowState (mkConfig [||]) (ValidationPackageDTO.mkDefault (name = "A"))).toEqual
                    PackageRowState.Unchecked
        )

        Vitest.test (
            "is Checked when version matches",
            fun () ->
                Vitest
                    .expect(rowState (mkConfig [| "A", Some "1.0.0" |]) (ValidationPackageDTO.mkDefault (name = "A")))
                    .toEqual
                    PackageRowState.Checked
        )

        Vitest.test (
            "is HasOlderVersion on mismatch",
            fun () ->
                Vitest
                    .expect(rowState (mkConfig [| "A", Some "0.9.0" |]) (ValidationPackageDTO.mkDefault (name = "A")))
                    .toEqual
                    PackageRowState.HasOlderVersion
        )

        Vitest.test (
            "is Checked when config version is missing, as it assumes latest",

            fun () ->
                Vitest.expect(rowState (mkConfig [| "A", None |]) (ValidationPackageDTO.mkDefault (name = "A"))).toEqual
                    PackageRowState.Checked
        )

        Vitest.test (
            "is Invalid when config version is higher than package version",
            fun () ->
                Vitest
                    .expect(rowState (mkConfig [| "A", Some "99.0.0" |]) (ValidationPackageDTO.mkDefault (name = "A")))
                    .toEqual
                    PackageRowState.InvalidVersion
        )
)

Vitest.describe (
    "filterBySearch",
    fun () ->

        let pkgs = [|
            ValidationPackageDTO.mkDefault (
                name = "Invenio",
                summary = "A great package",
                tags = [| mkTag "DataPLANT" |]
            )
            ValidationPackageDTO.mkDefault (
                name = "Other",
                summary = "Invenio mentions",
                authors = [| mkAuthor "Kevin Frey" |]
            )
            ValidationPackageDTO.mkDefault (name = "Third", summary = "Nothing here")
        |]

        Vitest.test (
            "returns all for empty query",
            fun () -> Vitest.expect(filterBySearch SearchFields.Name "" pkgs).toHaveLength 3
        )

        Vitest.test (
            "returns all for zero flags",
            fun () -> Vitest.expect(filterBySearch (enum<SearchFields> 0) "Invenio" pkgs).toHaveLength 3
        )

        Vitest.test (
            "name-only search",
            fun () ->
                let result = filterBySearch SearchFields.Name "invenio" pkgs
                Vitest.expect(Array.map (fun p -> p.Name) result).toEqual [| "Invenio" |]
        )

        Vitest.test (
            "summary-only search",
            fun () ->
                let result = filterBySearch SearchFields.Summary "invenio mentions" pkgs
                Vitest.expect(Array.map (fun p -> p.Name) result).toEqual [| "Other" |]
        )

        Vitest.test (
            "combined name+tags search matches tag names",
            fun () ->
                let result =
                    filterBySearch (SearchFields.Name ||| SearchFields.Tags) "dataplant" pkgs

                Vitest.expect(Array.map (fun p -> p.Name) result).toEqual [| "Invenio" |]
        )

        Vitest.test (
            "author search matches full names",
            fun () ->
                let result = filterBySearch SearchFields.Authors "kevin" pkgs
                Vitest.expect(Array.map (fun p -> p.Name) result).toEqual [| "Other" |]
        )
)

Vitest.describe (
    "filterByTag / filterByAuthor",
    fun () ->

        let pkgs = [|
            ValidationPackageDTO.mkDefault (name = "A", tags = [| mkTag "X" |], authors = [| mkAuthor "Anna" |])
            ValidationPackageDTO.mkDefault (name = "B", tags = [| mkTag "Y" |], authors = [| mkAuthor "Ben" |])
        |]

        Vitest.test (
            "no filter returns all",
            fun () ->
                Vitest.expect(filterByTag None pkgs).toHaveLength 2
                Vitest.expect(filterByAuthor None pkgs).toHaveLength 2
        )

        Vitest.test (
            "filters by exact tag and author",
            fun () ->
                Vitest.expect(filterByTag (Some "X") pkgs |> Array.map (fun p -> p.Name)).toEqual [| "A" |]
                Vitest.expect(filterByAuthor (Some "Ben") pkgs |> Array.map (fun p -> p.Name)).toEqual [| "B" |]
        )
)

Vitest.describe (
    "sortByChecked",
    fun () ->

        let RowStateMap =
            Map.ofList [
                "CheckedB", PackageRowState.Checked
                "Old", PackageRowState.HasOlderVersion
            ]

        Vitest.test (
            "returns packages in original order for None",
            fun () ->
                let pkgs = [|
                    ValidationPackageDTO.mkDefault (name = "U1")
                    ValidationPackageDTO.mkDefault (name = "CheckedB")
                    ValidationPackageDTO.mkDefault (name = "U2")
                |]

                Vitest.expect(sortByChecked CheckedSort.None RowStateMap pkgs |> Array.map (fun p -> p.Name)).toEqual [|
                    "U1"
                    "CheckedB"
                    "U2"
                |]
        )

        Vitest.test (
            "puts checked rows first for CheckedFirst",
            fun () ->
                let pkgs = [|
                    ValidationPackageDTO.mkDefault (name = "U1")
                    ValidationPackageDTO.mkDefault (name = "CheckedB")
                    ValidationPackageDTO.mkDefault (name = "U2")
                |]

                Vitest
                    .expect(
                        sortByChecked CheckedSort.CheckedFirst RowStateMap pkgs
                        |> Array.map (fun p -> p.Name)
                    )
                    .toEqual
                    [| "CheckedB"; "U1"; "U2" |]
        )

        Vitest.test (
            "puts checked rows last for CheckedLast",
            fun () ->
                let pkgs = [|
                    ValidationPackageDTO.mkDefault (name = "U1")
                    ValidationPackageDTO.mkDefault (name = "CheckedB")
                    ValidationPackageDTO.mkDefault (name = "U2")
                |]

                Vitest
                    .expect(
                        sortByChecked CheckedSort.CheckedLast RowStateMap pkgs
                        |> Array.map (fun p -> p.Name)
                    )
                    .toEqual
                    [| "U1"; "U2"; "CheckedB" |]
        )

        Vitest.test (
            "groups Checked before HasOlderVersion before Unchecked and keeps stable order",
            fun () ->
                let pkgs = [|
                    ValidationPackageDTO.mkDefault (name = "U1")
                    ValidationPackageDTO.mkDefault (name = "Old")
                    ValidationPackageDTO.mkDefault (name = "CheckedB")
                    ValidationPackageDTO.mkDefault (name = "U2")
                |]

                Vitest
                    .expect(
                        sortByChecked CheckedSort.CheckedFirst RowStateMap pkgs
                        |> Array.map (fun p -> p.Name)
                    )
                    .toEqual
                    [| "CheckedB"; "Old"; "U1"; "U2" |]

                Vitest
                    .expect(
                        sortByChecked CheckedSort.CheckedLast RowStateMap pkgs
                        |> Array.map (fun p -> p.Name)
                    )
                    .toEqual
                    [| "U1"; "U2"; "Old"; "CheckedB" |]
        )
)

Vitest.describe (
    "pageCount / slicePage",
    fun () ->

        let pkgs = Array.init 45 (fun i -> ValidationPackageDTO.mkDefault (name = $"P{i}"))

        Vitest.test (
            "computes page counts",
            fun () ->
                Vitest.expect(pageCount [||]).toBe 0
                Vitest.expect(pageCount pkgs).toBe 3
        )

        Vitest.test (
            "slices pages",
            fun () ->
                Vitest.expect(slicePage pkgs 0).toHaveLength 20
                Vitest.expect(slicePage pkgs 2).toHaveLength 5
                Vitest.expect(slicePage pkgs 3).toHaveLength 0
        )
)

Vitest.describe (
    "unlistedNames",
    fun () ->

        Vitest.test (
            "returns config names missing from table",
            fun () ->
                let config = mkConfig [| "A", Some "1.0.0"; "Legacy", Some "1.0.0" |]
                let pkgs = [| ValidationPackageDTO.mkDefault (name = "A") |]
                Vitest.expect(unlistedNames config pkgs).toEqual [| "Legacy" |]
        )
)

Vitest.describe (
    "latestVersions",
    fun () ->

        Vitest.test (
            "returns one package per name",
            fun () ->
                let pkgs = [|
                    ValidationPackageDTO.mkDefault (name = "A", majorVersion = 1, minorVersion = 0, patchVersion = 0)
                    ValidationPackageDTO.mkDefault (name = "A", majorVersion = 1, minorVersion = 1, patchVersion = 0)
                    ValidationPackageDTO.mkDefault (name = "B", majorVersion = 2, minorVersion = 0, patchVersion = 0)
                |]

                Vitest.expect(latestVersions pkgs |> Array.map (fun p -> p.Name) |> Array.sort).toEqual [| "A"; "B" |]
        )

        Vitest.test (
            "keeps the highest version of each package",
            fun () ->
                let pkgs = [|
                    ValidationPackageDTO.mkDefault (name = "A", majorVersion = 1, minorVersion = 0, patchVersion = 0)
                    ValidationPackageDTO.mkDefault (name = "A", majorVersion = 1, minorVersion = 2, patchVersion = 0)
                    ValidationPackageDTO.mkDefault (name = "A", majorVersion = 1, minorVersion = 1, patchVersion = 9)
                |]

                let result = latestVersions pkgs

                Vitest.expect(result).toHaveLength 1
                Vitest.expect(toVersionString result.[0]).toBe "1.2.0"
        )

        Vitest.test (
            "works regardless of input order",
            fun () ->
                let latest =
                    ValidationPackageDTO.mkDefault (name = "A", majorVersion = 2, minorVersion = 0, patchVersion = 0)

                let older =
                    ValidationPackageDTO.mkDefault (name = "A", majorVersion = 1, minorVersion = 5, patchVersion = 0)

                Vitest.expect(latestVersions [| older; latest |] |> Array.map toVersionString).toEqual [| "2.0.0" |]
                Vitest.expect(latestVersions [| latest; older |] |> Array.map toVersionString).toEqual [| "2.0.0" |]
        )

        Vitest.test (
            "prefers a release version over a pre-release version",
            fun () ->
                let release =
                    ValidationPackageDTO.mkDefault (name = "A", majorVersion = 1, minorVersion = 0, patchVersion = 0)

                let prerelease =
                    ValidationPackageDTO.mkDefault (
                        name = "A",
                        majorVersion = 1,
                        minorVersion = 0,
                        patchVersion = 0,
                        preReleaseVersionSuffix = "alpha.1"
                    )

                Vitest.expect(latestVersions [| prerelease; release |] |> Array.map toVersionString).toEqual [|
                    "1.0.0"
                |]

                Vitest.expect(latestVersions [| release; prerelease |] |> Array.map toVersionString).toEqual [|
                    "1.0.0"
                |]
        )

        Vitest.test (
            "represents an ordinary release without empty prerelease or build identifiers",
            fun () ->
                let version = (ValidationPackageDTO.mkDefault ()).ToSemVer()

                Vitest.expect(version.PreRelease).toEqual None
                Vitest.expect(version.Metadata).toEqual None
        )

        Vitest.test (
            "keeps the current package when the candidate version is not a valid SemVer",
            fun () ->
                let current =
                    ValidationPackageDTO.mkDefault (name = "A", majorVersion = 1, minorVersion = 0, patchVersion = 0)

                let invalid = {
                    current with
                        PreReleaseVersionSuffix = "!!invalid!!"
                }

                Vitest.expect(latestVersions [| current; invalid |] |> Array.map toVersionString).toEqual [| "1.0.0" |]
        )
)

Vitest.describe (
    "computeNewPackages",
    fun () ->

        Vitest.test (
            "adds checked, removes unchecked, keeps unedited",
            fun () ->
                let config = mkConfig [| "Keep", Some "1.0.0"; "RemoveMe", Some "1.0.0" |]

                let pkgs = [|
                    ValidationPackageDTO.mkDefault (name = "Keep")
                    ValidationPackageDTO.mkDefault (name = "RemoveMe")
                    ValidationPackageDTO.mkDefault (name = "Fresh")
                |]

                let edits = Map.ofList [ "RemoveMe", None; "Fresh", Some(latest "Fresh" "1.0.0") ]
                let result = computeNewPackages config pkgs edits Set.empty
                Vitest.expect(result |> Array.map (fun p -> p.Name) |> Array.sort).toEqual [| "Fresh"; "Keep" |]
        )

        Vitest.test (
            "keeps old version unless consciously updated",
            fun () ->
                let config = mkConfig [| "Old", Some "0.9.0" |]
                let pkgs = [| ValidationPackageDTO.mkDefault (name = "Old") |]

                let untouched = computeNewPackages config pkgs Map.empty Set.empty
                Vitest.expect(untouched.[0].Version).toBe (Some "0.9.0")

                let updated =
                    computeNewPackages config pkgs (Map.ofList [ "Old", Some(latest "Old" "1.0.0") ]) Set.empty

                Vitest.expect(updated.[0].Version).toBe (Some "1.0.0")
        )

        Vitest.test (
            "keeps unlisted, drops removed-unlisted",
            fun () ->
                let config = mkConfig [| "A", Some "1.0.0"; "Ghost", Some "1.0.0" |]
                let pkgs = [| ValidationPackageDTO.mkDefault (name = "A") |]

                let kept = computeNewPackages config pkgs Map.empty Set.empty
                Vitest.expect(kept |> Array.map (fun p -> p.Name) |> Array.sort).toEqual [| "A"; "Ghost" |]

                let removed = computeNewPackages config pkgs Map.empty (Set.ofList [ "Ghost" ])
                Vitest.expect(removed |> Array.map (fun p -> p.Name)).toEqual [| "A" |]
        )
)
