module Arcitisa.Components.Page.Metadata.FormComponents.PersonsInput.Types

open Fable.Core

exception LookupBlocked of string

[<StringEnum; RequireQualifiedAccess>]
type SearchMode =
    | Orcid
    | Name

type PersonResult = {
    orcid: string
    firstName: string option
    lastName: string option
    displayName: string option
    email: string option
    affiliation: string option
}

type SearchPage = {
    results: PersonResult[]
    nextStart: int option
}

/// All metadata is optional because ORCID only exposes fields marked public.
type OrcidValue =
    abstract value: string option

type OrcidName =
    abstract ``given-names``: OrcidValue option
    abstract ``family-name``: OrcidValue option
    abstract ``credit-name``: OrcidValue option

type OrcidEmail =
    abstract email: string option

type OrcidEmails =
    abstract email: OrcidEmail[] option

type OrcidPerson =
    abstract name: OrcidName option
    abstract emails: OrcidEmails option

type OrcidDate =
    abstract year: OrcidValue option
    abstract month: OrcidValue option
    abstract day: OrcidValue option

type OrcidOrganization =
    abstract name: string option

type OrcidEmploymentSummary =
    abstract organization: OrcidOrganization option
    abstract ``department-name``: string option
    abstract ``end-date``: OrcidDate option

type OrcidEmployment =
    abstract ``employment-summary``: OrcidEmploymentSummary option

type OrcidAffiliationGroup =
    abstract summaries: OrcidEmployment[] option

type OrcidEmployments =
    abstract ``affiliation-group``: OrcidAffiliationGroup[] option

type OrcidExpandedResult =
    abstract ``orcid-id``: string option
    abstract ``given-names``: string option
    abstract ``family-names``: string option
    abstract ``credit-name``: string option
    abstract email: string[] option
    abstract ``institution-name``: string[] option

type OrcidExpandedResponse =
    abstract ``expanded-result``: OrcidExpandedResult[] option
    abstract ``num-found``: int option
