namespace Arcitisa.Components.Composite.Notes.Editor

open System
open System.Text.RegularExpressions

[<RequireQualifiedAccess>]
module Validation =

    let toOptionalString (value: string) =
        let trimmed = value.Trim()

        if String.IsNullOrWhiteSpace trimmed then
            None
        else
            Some trimmed

    let isRequiredDataValid (draft: NotesDraft) =
        not (String.IsNullOrWhiteSpace draft.Title) && draft.DateCreated.IsSome

    let tryParseDateCreated (dateText: string) =
        let trimmed = dateText.Trim()

        if String.IsNullOrWhiteSpace trimmed then
            None
        else
            let dateOnly = Regex.Match(trimmed, @"^(\d{4})-(\d{2})-(\d{2})$")

            let dateTimeLocal =
                Regex.Match(trimmed, @"^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$")

            if dateOnly.Success then
                try
                    let year = int dateOnly.Groups.[1].Value
                    let month = int dateOnly.Groups.[2].Value
                    let day = int dateOnly.Groups.[3].Value
                    Some(DateTime(year, month, day))
                with _ ->
                    None
            elif dateTimeLocal.Success then
                try
                    let year = int dateTimeLocal.Groups.[1].Value
                    let month = int dateTimeLocal.Groups.[2].Value
                    let day = int dateTimeLocal.Groups.[3].Value
                    Some(DateTime(year, month, day))
                with _ ->
                    None
            else
                match DateTime.TryParse trimmed with
                | true, parsed -> Some parsed.Date
                | false, _ -> None

    let private sanitizeProtocolNameCandidate (candidate: string) =
        let cleaned = Regex.Replace(candidate, @"[^a-zA-Z0-9_\- ]", " ").Trim()
        Regex.Replace(cleaned, @"\s+", " ").Trim()

    let sanitizeProtocolName (candidate: string) =
        candidate
        |> sanitizeProtocolNameCandidate
        |> fun value -> Regex.Replace(value, @"\s+", "_").Trim([| '_'; '-' |])
        |> toOptionalString
