module Arcitisa.Components.Page.ValidationPackageBrowser.ValidationPackagesYaml

open System
open ARCtrl.ValidationPackages
open ARCtrl.Yaml

// YAMLicious writes every mapping inside a sequence as a bare "-" line followed by an indented block:
//
//   validation_packages:
//     -
//       name: invenio
//       version: 3.2.0
//
// That parses fine, but ARCitect and the DataHUB templates use the compact layout where the dash sits on the
// first key line. This module rewrites the writer output into that layout so both tools produce the same file.

let private indentOf (line: string) =
    line.Length - line.TrimStart(' ').Length

let private spaces (count: int) = String(' ', max 0 count)

/// Pulls each bare "-" line onto its first child line and shifts the rest of that block so the keys line up
/// under the first one. Works for any indentation width and for nested sequences.
let compactSequenceItems (yaml: string) : string =
    let lines = yaml.Replace("\r\n", "\n").Split('\n') |> List.ofArray

    // Regions are (dash indent, column shift) pairs for blocks whose dash was merged upward.
    // Indents are always compared in the original coordinates so nested blocks stay consistent.
    let rec loop (remaining: string list) (regions: (int * int) list) (acc: string list) =
        match remaining with
        | [] -> List.rev acc
        | line :: rest when String.IsNullOrWhiteSpace line -> loop rest regions (line :: acc)
        | line :: rest ->
            let indent = indentOf line

            let activeRegions =
                regions |> List.filter (fun (dashIndent, _) -> indent > dashIndent)

            let shift = activeRegions |> List.sumBy snd

            match rest with
            | firstChild :: afterChild when line.Trim() = "-" && indentOf firstChild > indent ->
                let childShift = (indent + 2) - indentOf firstChild
                let merged = spaces (indent + shift) + "- " + firstChild.TrimStart(' ')
                loop afterChild ((indent, childShift) :: activeRegions) (merged :: acc)
            | _ ->
                let shifted = spaces (indent + shift) + line.TrimStart(' ')
                loop rest activeRegions (shifted :: acc)

    loop lines [] [] |> String.concat "\n"

/// Serializes the config with the ARCtrl YAMLicious encoder and reformats it into the compact layout.
let toCompactYamlString (config: ValidationPackagesConfig) : string =
    config.toYamlString () |> compactSequenceItems
