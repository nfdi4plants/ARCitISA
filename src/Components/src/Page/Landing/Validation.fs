namespace Arcitisa.Components.Page.Landing

open System
open System.Text.RegularExpressions
open ARCtrl

[<RequireQualifiedAccess>]
module Validation =

    let toOptionalString (value: string) =
        let trimmed = value.Trim()

        if String.IsNullOrWhiteSpace trimmed then
            None
        else
            Some trimmed

    let isRequiredDataValid (draft: LandingDraft) =
        not (String.IsNullOrWhiteSpace draft.Title)
        && not (String.IsNullOrWhiteSpace draft.Description)

    let private sanitizeIdentifierCandidate (candidate: string) =
        let cleaned = Regex.Replace(candidate, @"[^a-zA-Z0-9_\- ]", " ").Trim()
        Regex.Replace(cleaned, @"\s+", " ").Trim()

    let private tryNormalizeIdentifier (candidate: string) =
        let sanitized =
            candidate
            |> sanitizeIdentifierCandidate
            |> fun value -> Regex.Replace(value, @"\s+", "_").Trim([| '_'; '-' |])

        if String.IsNullOrWhiteSpace sanitized then
            None
        elif ARCtrl.Helper.Identifier.tryCheckValidCharacters sanitized then
            Some sanitized
        else
            None

    let private generateSafeIdentifierFromTitle (title: string) =
        title |> tryNormalizeIdentifier |> Option.defaultValue "Experiment"

    let resolveIdentifier (draft: LandingDraft) =
        match toOptionalString draft.Identifier with
        | Some identifier ->
            match tryNormalizeIdentifier identifier with
            | Some safeIdentifier -> safeIdentifier
            | None -> generateSafeIdentifierFromTitle draft.Title
        | None -> generateSafeIdentifierFromTitle draft.Title
