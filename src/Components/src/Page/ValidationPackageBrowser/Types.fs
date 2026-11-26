module Arcitisa.Components.Page.ValidationPackageBrowser.Types

// [
//   {
//     "Name": "MyPackage",
//     "Summary": "MyPackage does the thing",
//     "Description": "MyPackage does the thing.\nIt does it very good, it does it very well.\nIt does it very fast, it does it very swell.",
//     "MajorVersion": 1,
//     "MinorVersion": 0,
//     "PatchVersion": 0,
//     "PreReleaseVersionSuffix": "alpha.1",
//     "BuildMetadataVersionSuffix": "0",
//     "PackageContent": "aHR0cHM6Ly93d3cueW91dHViZS5jb20vd2F0Y2g/dj1kUXc0dzlXZ1hjUQ==",
//     "ReleaseDate": "2026-08-19",
//     "Tags": [
//       {
//         "Name": "string",
//         "TermSourceREF": "string",
//         "TermAccessionNumber": "string"
//       }
//     ],
//     "ReleaseNotes": "string",
//     "CQCHookEndpoint": "string",
//     "Authors": [
//       {
//         "FullName": "string",
//         "Email": "string",
//         "Affiliation": "string",
//         "AffiliationLink": "string"
//       }
//     ],
//     "ProgrammingLanguage": "string"
//   }
// ]

type OntologyAnnotationDTO = {
    Name: string option
    TermSourceREF: string option
    TermAccessionNumber: string option
}

type AuthorDTO = {
    FullName: string option
    Email: string option
    Affiliation: string option
    AffiliationLink: string option
}

type ValidationPackageDTO = {
    Name: string
    Summary: string
    Description: string
    MajorVersion: int
    MinorVersion: int
    PatchVersion: int
    PreReleaseVersionSuffix: string
    BuildMetadataVersionSuffix: string
    // Keep this comment: PackageContent is not used by the package selector and is intentionally omitted to avoid overhead.
    ReleaseDate: System.DateTime
    Tags: OntologyAnnotationDTO[]
    ReleaseNotes: string
    CQCHookEndpoint: string
    Authors: AuthorDTO[]
    ProgrammingLanguage: string
} with

    static member Create
        (
            name: string,
            summary: string,
            description: string,
            majorVersion: int,
            minorVersion: int,
            patchVersion: int,
            preReleaseVersionSuffix: string,
            buildMetadataVersionSuffix: string,
            releaseDate: System.DateTime,
            tags: OntologyAnnotationDTO[],
            releaseNotes: string,
            cqcHookEndpoint: string,
            authors: AuthorDTO[],
            programmingLanguage: string
        ) : ValidationPackageDTO =
        {
            Name = name
            Summary = summary
            Description = description
            MajorVersion = majorVersion
            MinorVersion = minorVersion
            PatchVersion = patchVersion
            PreReleaseVersionSuffix = preReleaseVersionSuffix
            BuildMetadataVersionSuffix = buildMetadataVersionSuffix
            ReleaseDate = releaseDate
            Tags = tags
            ReleaseNotes = releaseNotes
            CQCHookEndpoint = cqcHookEndpoint
            Authors = authors
            ProgrammingLanguage = programmingLanguage
        }

    member this.ToSemVer() : ARCtrl.Helper.SemVer.SemVer =
        let optionalSuffix suffix =
            if System.String.IsNullOrEmpty suffix then
                None
            else
                Some suffix

        ARCtrl.Helper.SemVer.SemVer.create (
            this.MajorVersion,
            this.MinorVersion,
            this.PatchVersion,
            ?pre = optionalSuffix this.PreReleaseVersionSuffix,
            ?meta = optionalSuffix this.BuildMetadataVersionSuffix
        )

[<RequireQualifiedAccess>]
type SelectorState =
    | Idle
    | Loading
    | Loaded of ValidationPackageDTO[]
    | Error of exn

[<RequireQualifiedAccess>]
type PackageRowState =
    | Unchecked
    | Checked
    | HasOlderVersion
    | InvalidVersion

[<RequireQualifiedAccess>]
type CheckedSort =
    | None
    | CheckedFirst
    | CheckedLast

[<System.Flags>]
type SearchFields =
    | Name = 1
    | Summary = 2
    | Description = 4
    | Tags = 8
    | Authors = 16
