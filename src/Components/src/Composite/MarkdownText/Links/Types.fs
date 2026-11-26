module Arcitisa.Components.Composite.MarkdownText.Links.Types

/// Where a link or image reference of a Markdown document points.
[<RequireQualifiedAccess>]
type LinkTarget =
    /// An http, https or mailto URL.
    | External of url: string
    /// "#x": a heading in the same document. The id is percent-decoded.
    | Fragment of id: string
    /// A file or folder of the ARC. The path is ARC-relative, with / separators, normalized and percent-decoded.
    /// "" is the ARC root. The fragment is the part after "#", percent-decoded, or None when empty.
    | ArcPath of path: string * fragment: string option
    /// A relative path that climbs above the ARC root.
    | OutsideArc of raw: string
    /// Anything else, with the reason shown to the user.
    | Unsupported of raw: string * reason: string

/// What a Markdown preview needs from its host to show images of an ARC and open the files a link points to.
/// Hosts without an ARC pass no context. JS hosts implement it as a plain object.
type IMarkdownLinkContext =
    /// ARC-relative folder of the open file, with / separators and no leading or trailing slash. "" is the ARC root.
    abstract baseFolder: string
    /// The URL an <img> loads for an ARC path, or None when the host cannot serve the file.
    abstract assetUrl: arcPath: string -> string option
    /// Why the image at an ARC path cannot be shown, or None when the host knows no reason.
    abstract describeAsset: arcPath: string -> Fable.Core.JS.Promise<string option>
    abstract openArcPath: arcPath: string -> unit
    abstract openExternal: url: string -> unit

/// What the host found at an ARC path a link points to.
[<RequireQualifiedAccess>]
type LinkTargetState =
    | Exists
    | Missing
    /// The path exists with another case on disk. onDisk is ARC-relative with the case on disk.
    | CaseDiffers of onDisk: string
    | OutsideArc
    | Unreadable of message: string

/// A folder listing for the path completion: the ARC-relative folder ("" is the ARC root) and the start of the
/// names to list, compared ignoring case.
type ListFolderRequest = {| folder: string; namePrefix: string |}

/// One regular file or folder of an ARC folder.
type FolderEntry = {| name: string; isDirectory: bool |}

/// The entries of a folder whose names start with the asked prefix. truncated is true when the host left out
/// entries, because more of them match than it lists at once.
type FolderContents = {|
    entries: FolderEntry[]
    truncated: bool
|}

/// What a path target at the cursor points to. An image target offers only folders and images.
[<RequireQualifiedAccess>]
type PathTargetKind =
    | Link
    | Image
    /// The target of a link definition, which links and images both use.
    | Any

/// A path completion for the target at the cursor: the folder to list and the name typed so far.
type PathCompletionRequest = {
    Kind: PathTargetKind
    /// ARC-relative, "" for the ARC root.
    Folder: string
    /// The name after the last "/" of the target, as written, with its percent escapes.
    Typed: string
    /// Typed, percent-decoded. The listed names start with it, ignoring case.
    NamePrefix: string
}
