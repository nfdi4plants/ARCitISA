/// Completion of ARC paths in the targets of Markdown links, images, link definitions and the src and href of raw
/// HTML. A target that starts with "/", "./" or "../" completes the names in the folder that it names up to its last
/// "/": "/" is the ARC root, "./" the folder of the document and "../" the one above it. "." and ".." on their own
/// complete nothing, and neither does a target that starts with a name. The host lists one folder level per request,
/// so the cost depends only on the folders the user types through. Pure, apart from source, which asks the host.
[<RequireQualifiedAccess>]
module Swate.Components.Composite.MarkdownText.Links.PathCompletion

open System
open System.Text.RegularExpressions
open Fable.Core
open Swate.Components.Composite.TextEditor.Types
open Swate.Components.Composite.MarkdownText.Links.Types

module private PathCompletionHelpers =

    // Each pattern matches the end of the line before the cursor, and group 1 is the target typed so far. A target in
    // angle brackets may hold spaces.
    let inlineAngle = Regex(@"\]\(\s*<([^<>]*)$")
    let inlinePlain = Regex(@"\]\(\s*([^\s()<>]*)$")
    // A label that starts with ^ is a footnote, not a link definition.
    let definitionAngle = Regex(@"^ {0,3}\[[^\[\]^][^\[\]]*\]:[ \t]*<([^<>]*)$")
    let definitionPlain = Regex(@"^ {0,3}\[[^\[\]^][^\[\]]*\]:[ \t]*([^\s<>]*)$")

    let htmlImage =
        Regex(@"<img\b[^<>]*\ssrc\s*=\s*[""']([^""'<>\s]*)$", RegexOptions.IgnoreCase)

    let htmlLink =
        Regex(@"<a\b[^<>]*\shref\s*=\s*[""']([^""'<>\s]*)$", RegexOptions.IgnoreCase)

    /// The syntax nodes of @lezer/markdown whose text is code or a comment, where a target is not a link.
    let codeNodes = [|
        "InlineCode"
        "FencedCode"
        "CodeBlock"
        "Comment"
        "CommentBlock"
    |]

    let folderIcon = "swt:iconify swt:fluent--folder-24-regular"
    let imageIcon = "swt:iconify swt:fluent--image-24-regular"
    let fileIcon = "swt:iconify swt:fluent--document-24-regular"

    /// Characters that end the name being typed: the folder separator, and what ends a target or starts its title,
    /// query or fragment.
    let endsName (c: char) =
        Char.IsWhiteSpace c || "/\\()<>\"'?#".IndexOf c >= 0

    let tryDecode (text: string) =
        try
            Some(JS.decodeURIComponent text)
        with _ ->
            None

    let isEscaped (text: string) (index: int) =
        let mutable backslashes = 0
        let mutable i = index - 1

        while i >= 0 && text.[i] = '\\' do
            backslashes <- backslashes + 1
            i <- i - 1

        backslashes % 2 = 1

    /// The index of the "[" that pairs with the "]" at closing. Nested pairs and escaped brackets are skipped.
    let openingBracket (text: string) (closing: int) : int option =
        let rec scan i depth =
            if i < 0 then
                None
            elif isEscaped text i then
                scan (i - 1) depth
            else
                match text.[i] with
                | ']' -> scan (i - 1) (depth + 1)
                | '[' when depth = 0 -> Some i
                | '[' -> scan (i - 1) (depth - 1)
                | _ -> scan (i - 1) depth

        scan (closing - 1) 0

    /// The kind and the text typed so far of the target that the line before the cursor ends in.
    let targetAt (lineBefore: string) : (PathTargetKind * string) option =
        // "](" counts only after a link text whose "[" is on the same line. A "!" before that "[" makes it an image.
        let inlineTarget (pattern: Regex) () =
            let m = pattern.Match lineBefore

            if not m.Success then
                None
            else
                openingBracket lineBefore m.Index
                |> Option.map (fun opening ->
                    let isImage =
                        opening > 0
                        && lineBefore.[opening - 1] = '!'
                        && not (isEscaped lineBefore (opening - 1))

                    (if isImage then
                         PathTargetKind.Image
                     else
                         PathTargetKind.Link),
                    m.Groups.[1].Value
                )

        let plainTarget (kind: PathTargetKind) (pattern: Regex) () =
            let m = pattern.Match lineBefore

            if m.Success then Some(kind, m.Groups.[1].Value) else None

        [
            inlineTarget inlineAngle
            inlineTarget inlinePlain
            plainTarget PathTargetKind.Any definitionAngle
            plainTarget PathTargetKind.Any definitionPlain
            plainTarget PathTargetKind.Image htmlImage
            plainTarget PathTargetKind.Link htmlLink
        ]
        |> List.tryPick (fun find -> find ())

    let startsWithDot (name: string) = name.StartsWith "."

open PathCompletionHelpers

/// True when the cursor is in code or a comment, given the names of the syntax nodes at the cursor.
let isInCode (syntaxPath: string[]) : bool =
    syntaxPath |> Array.exists (fun name -> Array.contains name codeNodes)

/// The request for the target that the line before the cursor ends in, None when there is nothing to complete.
let request (baseFolder: string) (lineBefore: string) : PathCompletionRequest option =
    match targetAt lineBefore with
    | None -> None
    | Some(kind, target) ->
        let startsAtFolder =
            target.StartsWith "/" || target.StartsWith "./" || target.StartsWith "../"

        if
            not startsAtFolder
            || target.StartsWith "//"
            || target |> Seq.exists (fun c -> c = '\\' || c = '?' || c = '#')
        then
            None
        else
            let slash = target.LastIndexOf '/'
            let typed = target.Substring(slash + 1)

            // ".." is the start of "../", not a name.
            if typed = ".." then
                None
            else
                match LinkResolution.resolve baseFolder (target.Substring(0, slash + 1)), tryDecode typed with
                | LinkTarget.ArcPath(folder, None), Some prefix ->
                    Some {
                        Kind = kind
                        Folder = folder
                        Typed = typed
                        NamePrefix = prefix
                    }
                | _ -> None

/// Percent-encodes the characters of a file name that would end the target or change where it points. Other
/// characters, also non-ASCII letters, stay as they are, so the target stays readable.
let encodeName (name: string) : string =
    name
    |> Seq.map (fun c ->
        match c with
        // encodeURIComponent leaves these three as they are.
        | '(' -> "%28"
        | ')' -> "%29"
        | '\'' -> "%27"
        | '%'
        | '<'
        | '>'
        | '#'
        | '?'
        | '"'
        | '\\' -> JS.encodeURIComponent (string c)
        | c when Char.IsWhiteSpace c || Char.IsControl c -> JS.encodeURIComponent (string c)
        | c -> string c
    )
    |> String.concat ""

/// The entries to offer for the request. Names that start with "." show only when the typed name starts with ".".
/// An image target offers folders and images only. A folder ends in "/" and opens the list again for its entries.
let options (request: PathCompletionRequest) (entries: FolderEntry[]) : TextCompletionOption[] =
    let showHidden = startsWithDot request.NamePrefix

    entries
    |> Array.filter (fun entry -> startsWithDot entry.name = showHidden)
    |> Array.filter (fun entry ->
        entry.isDirectory
        || request.Kind <> PathTargetKind.Image
        || ImageTypes.isImage entry.name
    )
    |> Array.map (fun entry ->
        if entry.isDirectory then
            {|
                label = encodeName entry.name + "/"
                iconClass = Some folderIcon
                reopen = true
                boost = 1
            |}
        else
            {|
                label = encodeName entry.name
                iconClass =
                    Some(
                        if ImageTypes.isImage entry.name then
                            imageIcon
                        else
                            fileIcon
                    )
                reopen = false
                boost = 0
            |}
    )

/// True while text, the name typed since the request, can be filtered from the same listing: it is still one name,
/// it still starts with the requested prefix, and it still starts with "." exactly when the prefix does.
let stillValid (request: PathCompletionRequest) (text: string) : bool =
    let sameStart (name: string) =
        name.ToLowerInvariant().StartsWith(request.NamePrefix.ToLowerInvariant())
        && startsWithDot name = startsWithDot request.NamePrefix

    text <> ".."
    && not (text |> Seq.exists endsName)
    && (tryDecode text |> Option.map sameStart |> Option.defaultValue false)

/// The completion source for a Markdown document in baseFolder. listFolder lists one ARC folder. It resolves None
/// for a folder that does not exist or lies outside the ARC, and rejects when it cannot list one.
let source
    (baseFolder: string)
    (listFolder: ListFolderRequest -> JS.Promise<FolderContents option>)
    : TextCompletionSource =
    fun context -> promise {
        let found =
            if isInCode context.syntaxPath then
                None
            else
                request baseFolder context.lineBefore

        match found with
        | None -> return None
        | Some request ->
            let! contents =
                listFolder {|
                    folder = request.Folder
                    namePrefix = request.NamePrefix
                |}

            return
                contents
                |> Option.map (fun contents -> {|
                    from = context.position - request.Typed.Length
                    ``to`` = context.position
                    options = options request contents.entries
                    // A cut listing lacks names that the next character may need, so each edit lists again.
                    validFor =
                        if contents.truncated then
                            None
                        else
                            Some(stillValid request)
                |})
    }
