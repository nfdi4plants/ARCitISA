/// Path helpers for the text document page and its leave guard. Pure, so Main tests can cover them.
module Arcitisa.Electron.Shared.TextDocumentHelper

open Fable.Core
open Arcitisa.Components.Shared

let private segments (path: string) : string[] =
    (PathHelpers.normalizeCanonicalRelativePath path).Split('/')
    |> Array.filter (fun s -> s <> "")

/// The editor document id of an ARC-relative path, such as "file:///assays/a%201/README.md".
let documentUri (arcRelativePath: string) : string =
    "file:///"
    + (segments arcRelativePath |> Array.map JS.encodeURIComponent |> String.concat "/")

/// Compares two ARC-relative paths after normalizing separators and slashes.
/// Case matters, as in the key of the open page. A path that differs only in case opens another page, so it must go
/// through the leave guard.
let isSameDocumentPath (left: string) (right: string) : bool =
    PathHelpers.normalizeCanonicalRelativePath left = PathHelpers.normalizeCanonicalRelativePath right

/// True when candidate is the document itself or a folder above it, compared by whole path segments,
/// so "docs" is above "docs/a.md" but not above "docs2/a.md". It ignores case. A false match only adds a leave
/// prompt, and a missed one could lose edits.
let isPathOrAncestor (candidate: string) (documentPath: string) : bool =
    let c = segments candidate
    let d = segments documentPath

    let sameSegment (a: string) (b: string) =
        System.String.Equals(a, b, System.StringComparison.OrdinalIgnoreCase)

    c.Length <= d.Length && Array.forall2 sameSegment c (Array.truncate c.Length d)
