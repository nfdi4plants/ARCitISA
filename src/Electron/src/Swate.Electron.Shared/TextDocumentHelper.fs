/// Path helpers for the text document page and its leave guard. Pure, so Main tests can cover them.
module Swate.Electron.Shared.TextDocumentHelper

open Fable.Core
open Swate.Components.Shared

[<Emit("encodeURIComponent($0)")>]
let private encodeSegment (segment: string) : string = jsNative

let private segments (path: string) : string[] =
    (PathHelpers.normalizeCanonicalRelativePath path).Split('/')
    |> Array.filter (fun s -> s <> "")

/// The editor document id of an ARC-relative path, such as "file:///assays/a%201/README.md".
let documentUri (arcRelativePath: string) : string =
    "file:///"
    + (segments arcRelativePath |> Array.map encodeSegment |> String.concat "/")

/// Compares two ARC-relative paths after normalizing separators and slashes. Case matters.
let isSameDocumentPath (left: string) (right: string) : bool =
    PathHelpers.normalizeCanonicalRelativePath left = PathHelpers.normalizeCanonicalRelativePath right

/// True when candidate is the document itself or a folder above it, compared by whole path segments,
/// so "docs" is above "docs/a.md" but not above "docs2/a.md".
let isPathOrAncestor (candidate: string) (documentPath: string) : bool =
    let c = segments candidate
    let d = segments documentPath
    c.Length <= d.Length && Array.forall2 (=) c (Array.truncate c.Length d)
