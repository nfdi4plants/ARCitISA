module Arcitisa.Electron.Shared.DTOs.MarkdownResolverDto

/// Why the main process does not serve an image of the ARC.
[<RequireQualifiedAccess>]
type AssetRefusal =
    /// No ARC is open in the window, or the URL belongs to an ARC that is no longer open.
    | ArcNotOpen
    /// The path leaves the ARC, or a link on the way points outside it.
    | OutsideArc
    | NotFound
    /// A Git LFS pointer file. The object is not downloaded, and it is never downloaded for the user.
    | LfsPointer
    | TooLarge of sizeBytes: float * limitBytes: float
    /// The extension, lowercased with its dot, or "" when the file has none.
    | UnsupportedType of extension: string
    | Unreadable of message: string

/// What an ARC-relative path of a Markdown reference points to.
[<RequireQualifiedAccess>]
type PathCheck =
    | Missing
    | File
    | Directory
    /// The path exists with another case on disk. onDisk is ARC-relative, with / and the case on disk.
    | CaseDiffers of onDisk: string * isDirectory: bool
    | OutsideArc
    /// A Git LFS pointer file. Its object is not downloaded, and it is never downloaded for the user.
    | LfsPointer
    | Unreadable of message: string

/// A folder listing for the path completion of Markdown documents. folder is ARC-relative, "" for the ARC root.
/// The listed names start with namePrefix, ignoring case. The same type the path completion asks with.
type ListFolderRequestDto = Arcitisa.Components.Composite.MarkdownText.Links.Types.ListFolderRequest

/// One regular file or folder of a listing. The same type the path completion offers from.
type FolderEntryDto = Arcitisa.Components.Composite.MarkdownText.Links.Types.FolderEntry

/// What a folder listing found.
[<RequireQualifiedAccess>]
type FolderListing =
    /// The regular files and folders whose names start with the prefix, folders first. truncated is true when more
    /// of them match than one listing returns.
    | Listed of entries: FolderEntryDto[] * truncated: bool
    /// Nothing at the path, or a file.
    | NotAFolder
    /// The path leaves the ARC, or a link on the way points outside it.
    | OutsideArc
