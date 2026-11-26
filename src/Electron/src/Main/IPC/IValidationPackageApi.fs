module Main.IPC.ValidationPackageApi

open Fable.Core
open Fetch
open Arcitisa.Electron.Shared.IPCTypes
open Arcitisa.Components.Page.ValidationPackageBrowser.Types

/// The package registry serves package content as base64. The DTO intentionally omits it to
/// avoid moving large payloads over IPC. The renderer only needs package metadata.
[<Literal>]
let private AVPR_BASE_URL = "https://avpr.nfdi4plants.org/api/v1"

/// Raw response shape of GET /api/v1/packages. ReleaseDate is a date string and nullable
/// fields may be null, both are normalized before building the actual DTO.
module private Raw =

    type Tag = {
        Name: string
        TermSourceREF: string
        TermAccessionNumber: string
    }

    type Author = {
        FullName: string
        Email: string
        Affiliation: string
        AffiliationLink: string
    }

    type Package = {
        Name: string
        Summary: string
        Description: string
        MajorVersion: int
        MinorVersion: int
        PatchVersion: int
        PreReleaseVersionSuffix: string
        BuildMetadataVersionSuffix: string
        ReleaseDate: string
        Tags: Tag[]
        ReleaseNotes: string
        CQCHookEndpoint: string
        Authors: Author[]
        ProgrammingLanguage: string
    }

module private Normalize =

    let str (value: string) =
        if System.String.IsNullOrWhiteSpace value then "" else value

    let optionalString (value: string) =
        if System.String.IsNullOrWhiteSpace value then
            None
        else
            Some value

    let tags (tags: Raw.Tag[]) : OntologyAnnotationDTO[] =
        if isNull (box tags) then
            [||]
        else
            tags
            |> Array.map (fun tag -> {
                Name = optionalString tag.Name
                TermSourceREF = optionalString tag.TermSourceREF
                TermAccessionNumber = optionalString tag.TermAccessionNumber
            })

    let authors (authors: Raw.Author[]) : AuthorDTO[] =
        if isNull (box authors) then
            [||]
        else
            authors
            |> Array.map (fun author -> {
                FullName = optionalString author.FullName
                Email = optionalString author.Email
                Affiliation = optionalString author.Affiliation
                AffiliationLink = optionalString author.AffiliationLink
            })

    let package (package: Raw.Package) : ValidationPackageDTO = {
        Name = str package.Name
        Summary = str package.Summary
        Description = str package.Description
        MajorVersion = package.MajorVersion
        MinorVersion = package.MinorVersion
        PatchVersion = package.PatchVersion
        PreReleaseVersionSuffix = str package.PreReleaseVersionSuffix
        BuildMetadataVersionSuffix = str package.BuildMetadataVersionSuffix
        ReleaseDate =
            match System.DateTime.TryParse(str package.ReleaseDate) with
            | true, parsed -> parsed
            | false, _ -> System.DateTime.MinValue
        Tags = tags package.Tags
        ReleaseNotes = str package.ReleaseNotes
        CQCHookEndpoint = str package.CQCHookEndpoint
        Authors = authors package.Authors
        ProgrammingLanguage = str package.ProgrammingLanguage
    }

let api: IValidationPackageIPC = {
    getAllPackages =
        fun () -> promise {
            try
                let! response =
                    fetchUnsafe $"{AVPR_BASE_URL}/packages" [
                        RequestProperties.Method HttpMethod.GET
                        requestHeaders [ HttpRequestHeaders.Accept "application/json" ]
                    ]

                if not response.Ok then
                    let! bodyText = response.text ()

                    return
                        Error(
                            exn
                                $"Validation package registry request failed with HTTP {response.Status} {response.StatusText}: {bodyText}"
                        )
                else
                    let! packages = response.json<Raw.Package[]> ()
                    return Ok(packages |> Array.map Normalize.package)
            with error ->
                return Error(exn $"Failed to fetch validation packages from the package registry: {error.Message}")
        }
}
