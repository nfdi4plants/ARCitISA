module internal Arcitisa.Components.Composite.MarkdownText.Links.PathCompletionTests

open Fable.Core
open Vitest
open Arcitisa.Components.Composite.TextEditor
open Arcitisa.Components.Composite.TextEditor.Types
open Arcitisa.Components.Composite.TextEditor.JsBindings.CodeMirror
open Arcitisa.Components.Composite.TextEditor.JsBindings.Autocomplete
open Arcitisa.Components.Composite.MarkdownText.Links
open Arcitisa.Components.Composite.MarkdownText.Links.Types

let private docs = "docs"

/// The folder, the typed name and the kind of the request, or a failure naming the line.
let private requestOf (baseFolder: string) (lineBefore: string) =
    match PathCompletion.request baseFolder lineBefore with
    | Some r -> r.Folder, r.Typed, r.Kind
    | None -> failwith $"Expected a request for \"{lineBefore}\""

let private noRequest (baseFolder: string) (lineBefore: string) =
    match PathCompletion.request baseFolder lineBefore with
    | None -> ()
    | Some r -> failwith $"Expected no request for \"{lineBefore}\", got folder \"{r.Folder}\" and name \"{r.Typed}\""

let private requestFor (kind: PathTargetKind) (prefix: string) : PathCompletionRequest = {
    Kind = kind
    Folder = docs
    Typed = prefix
    NamePrefix = prefix
}

let private folder (name: string) : FolderEntry = {| name = name; isDirectory = true |}
let private file (name: string) : FolderEntry = {| name = name; isDirectory = false |}

let private labels (options: TextCompletionOption[]) = options |> Array.map _.label

let private contextAt (lineBefore: string) (syntaxPath: string[]) : TextCompletionContext = {|
    position = 100 + lineBefore.Length
    lineBefore = lineBefore
    lineAfter = ")"
    explicit = false
    syntaxPath = syntaxPath
|}

Vitest.describe (
    "PathCompletion.request",
    fun () ->
        Vitest.test (
            "starts only at /, ./ and ../, not at . or .. or a name",
            fun () ->
                for line in
                    [
                        "[a]("
                        "[a](."
                        "[a](.."
                        "[a](docs/"
                        "[a](a.md"
                        "[a](.../"
                    ] do
                    noRequest docs line

                Vitest.expect(requestOf docs "[a](./").toEqual ((docs, "", PathTargetKind.Link))
                Vitest.expect(requestOf "docs/sub" "[a](../").toEqual ((docs, "", PathTargetKind.Link))
                Vitest.expect(requestOf docs "[a](/").toEqual (("", "", PathTargetKind.Link))
        )

        Vitest.test (
            "lists the folder up to the last / and keeps the rest as the typed name",
            fun () ->
                Vitest.expect(requestOf docs "[a](/images/ph").toEqual (("images", "ph", PathTargetKind.Link))
                Vitest.expect(requestOf docs "[a](./sub/../other/x").toEqual (("docs/other", "x", PathTargetKind.Link))
                Vitest.expect(requestOf "docs/a/b" "[a](../../").toEqual ((docs, "", PathTargetKind.Link))
        )

        Vitest.test (
            "completes nothing above the ARC root",
            fun () ->
                noRequest "" "[a](../"
                noRequest docs "[a](../../"
        )

        Vitest.test (
            "treats .. as the start of ../ but . as the start of a hidden name",
            fun () ->
                noRequest docs "[a](./sub/.."
                Vitest.expect(requestOf docs "[a](./sub/.").toEqual (("docs/sub", ".", PathTargetKind.Link))
                Vitest.expect(requestOf docs "[a](./.ar").toEqual ((docs, ".ar", PathTargetKind.Link))
        )

        Vitest.test (
            "decodes percent escapes in the folder and the name",
            fun () ->
                Vitest.expect(requestOf docs "[a](./my%20dir/ph").toEqual (("docs/my dir", "ph", PathTargetKind.Link))

                match PathCompletion.request docs "[a](./my%20f" with
                | Some r -> Vitest.expect(r.NamePrefix).toBe "my f"
                | None -> failwith "Expected a request"

                // An escape that is still being typed completes nothing until it is whole.
                noRequest docs "[a](./my%2"
        )

        Vitest.test (
            "completes nothing after a title, query or fragment, a backslash or //",
            fun () ->
                for line in
                    [
                        "[a](./x \"t"
                        "[a](./x?"
                        "[a](./x#"
                        "[a](.\\"
                        "[a](//"
                        "[a](./x) "
                    ] do
                    noRequest docs line
        )

        Vitest.test (
            "tells images from links by the ! before the link text, nested brackets included",
            fun () ->
                Vitest.expect(requestOf docs "![alt](./img/").toEqual (("docs/img", "", PathTargetKind.Image))
                Vitest.expect(requestOf docs "[a [b] c](./").toEqual ((docs, "", PathTargetKind.Link))
                Vitest.expect(requestOf docs "[a](./x.md) and ![b](/").toEqual (("", "", PathTargetKind.Image))
                Vitest.expect(requestOf docs @"\![a](./").toEqual ((docs, "", PathTargetKind.Link))
                // An escaped [ opens no link text.
                noRequest docs @"\[a](./"
        )

        Vitest.test (
            "completes targets in angle brackets, which may hold spaces",
            fun () ->
                Vitest.expect(requestOf docs "[a](<./my dir/").toEqual (("docs/my dir", "", PathTargetKind.Link))
                Vitest.expect(requestOf docs "[a](<./my f").toEqual ((docs, "my f", PathTargetKind.Link))
        )

        Vitest.test (
            "completes link definitions for links and images alike, but not footnotes",
            fun () ->
                Vitest.expect(requestOf docs "[logo]: ./").toEqual ((docs, "", PathTargetKind.Any))
                Vitest.expect(requestOf docs "   [logo]:   <../").toEqual (("", "", PathTargetKind.Any))
                noRequest docs "[^1]: ./"
                // Four spaces make an indented code block.
                noRequest docs "    [logo]: ./"
        )

        Vitest.test (
            "completes src of img and href of a in raw HTML, whatever the case",
            fun () ->
                Vitest.expect(requestOf docs "<img width=\"80\" src=\"./").toEqual ((docs, "", PathTargetKind.Image))
                Vitest.expect(requestOf docs "<IMG SRC='../").toEqual (("", "", PathTargetKind.Image))
                Vitest.expect(requestOf docs "see <a href=\"/data/").toEqual (("data", "", PathTargetKind.Link))
                noRequest docs "<img data-src=\"./"
                noRequest docs "<a src=\"./"
        )
)

Vitest.describe (
    "PathCompletion.isInCode",
    fun () ->
        Vitest.test (
            "is true inside inline code, code blocks and comments",
            fun () ->
                Vitest.expect(PathCompletion.isInCode [| "InlineCode"; "Paragraph"; "Document" |]).toBe true
                Vitest.expect(PathCompletion.isInCode [| "CodeText"; "FencedCode"; "Document" |]).toBe true
                Vitest.expect(PathCompletion.isInCode [| "Comment"; "Paragraph"; "Document" |]).toBe true
                Vitest.expect(PathCompletion.isInCode [| "Paragraph"; "Document" |]).toBe false
        )
)

Vitest.describe (
    "PathCompletion.options",
    fun () ->
        let entries = [|
            folder ".arc"
            file ".gitattributes"
            folder "images"
            file "a.png"
            file "b.md"
            file "scan.TIF"
            file "logo.SVG"
        |]

        Vitest.test (
            "hides names that start with . unless the typed name does",
            fun () ->
                let visible = PathCompletion.options (requestFor PathTargetKind.Link "") entries
                Vitest.expect(labels visible).toEqual [| "images/"; "a.png"; "b.md"; "scan.TIF"; "logo.SVG" |]

                let hidden = PathCompletion.options (requestFor PathTargetKind.Link ".") entries
                Vitest.expect(labels hidden).toEqual [| ".arc/"; ".gitattributes" |]
        )

        Vitest.test (
            "offers only folders and images for an image",
            fun () ->
                let images = PathCompletion.options (requestFor PathTargetKind.Image "") entries
                Vitest.expect(labels images).toEqual [| "images/"; "a.png"; "logo.SVG" |]

                let anyTarget = PathCompletion.options (requestFor PathTargetKind.Any "") entries
                Vitest.expect(anyTarget.Length).toBe 5
        )

        Vitest.test (
            "ends folders in / and reopens the list after them, ahead of files",
            fun () ->
                let found =
                    PathCompletion.options (requestFor PathTargetKind.Link "") [|
                        folder "data"
                        file "a.png"
                        file "b.md"
                    |]

                Vitest.expect(found |> Array.map (fun o -> o.reopen, o.boost)).toEqual [|
                    (true, 1)
                    (false, 0)
                    (false, 0)
                |]

                Vitest.expect(found.[0].iconClass.Value).toContain "folder"
                Vitest.expect(found.[1].iconClass.Value).toContain "image"
                Vitest.expect(found.[2].iconClass.Value).toContain "document"
        )

        Vitest.test (
            "encodes the characters that would end or change the target",
            fun () ->
                Vitest.expect(PathCompletion.encodeName "my file (1).png").toBe "my%20file%20%281%29.png"
                Vitest.expect(PathCompletion.encodeName "50%#?.md").toBe "50%25%23%3F.md"
                Vitest.expect(PathCompletion.encodeName "it's \"x\" <y>.md").toBe "it%27s%20%22x%22%20%3Cy%3E.md"
                Vitest.expect(PathCompletion.encodeName "Größe_ä.md").toBe "Größe_ä.md"

                let found =
                    PathCompletion.options (requestFor PathTargetKind.Link "") [| folder "my data" |]

                Vitest.expect(labels found).toEqual [| "my%20data/" |]
        )

        Vitest.test (
            "gives names that resolve back to the file on disk",
            fun () ->
                for name in [ "my file (1).png"; "50%#?.md"; "a b.md" ] do
                    match LinkResolution.resolve docs ("./" + PathCompletion.encodeName name) with
                    | LinkTarget.ArcPath(path, None) -> Vitest.expect(path).toBe ("docs/" + name)
                    | other -> failwith $"Expected an ARC path for \"{name}\", got {other}"
        )
)

Vitest.describe (
    "PathCompletion.stillValid",
    fun () ->
        Vitest.test (
            "stays valid while the typed text is one name with the requested start",
            fun () ->
                let fromEmpty = PathCompletion.stillValid (requestFor PathTargetKind.Link "")
                Vitest.expect(fromEmpty "do").toBe true
                Vitest.expect(fromEmpty "my%20").toBe true
                Vitest.expect(fromEmpty "do/").toBe false
                Vitest.expect(fromEmpty "a b").toBe false
                Vitest.expect(fromEmpty "x)").toBe false
                Vitest.expect(fromEmpty "%2").toBe false

                let fromDo = PathCompletion.stillValid (requestFor PathTargetKind.Link "do")
                Vitest.expect(fromDo "DOC").toBe true
                Vitest.expect(fromDo "d").toBe false
        )

        Vitest.test (
            "asks again when a . starts or stops a hidden name, and stops at ..",
            fun () ->
                let fromEmpty = PathCompletion.stillValid (requestFor PathTargetKind.Link "")
                Vitest.expect(fromEmpty ".").toBe false

                let fromDot = PathCompletion.stillValid (requestFor PathTargetKind.Link ".")
                Vitest.expect(fromDot ".ar").toBe true
                Vitest.expect(fromDot "..").toBe false
        )
)

Vitest.describe (
    "PathCompletion.source",
    fun () ->
        let listing (contents: FolderContents option) (asked: ResizeArray<ListFolderRequest>) =
            fun (request: ListFolderRequest) -> promise {
                asked.Add request
                return contents
            }

        Vitest.test (
            "lists the folder of the target and replaces the typed name",
            fun () -> promise {
                let asked = ResizeArray()

                let contents = {|
                    entries = [| folder "sub"; file "guide.md" |]
                    truncated = false
                |}

                let source = PathCompletion.source docs (listing (Some contents) asked)
                let context = contextAt "See [x](./su" [| "Paragraph"; "Document" |]
                let! result = source context

                Vitest.expect(asked.ToArray()).toEqual [| {| folder = docs; namePrefix = "su" |} |]

                match result with
                | Some result ->
                    Vitest.expect(result.from).toBe (context.position - 2)
                    Vitest.expect(result.``to``).toBe context.position
                    Vitest.expect(labels result.options).toEqual [| "sub/"; "guide.md" |]
                    Vitest.expect(result.validFor.Value "sub").toBe true
                | None -> failwith "Expected completions"
            }
        )

        Vitest.test (
            "lists again on every edit when the host cut the listing",
            fun () -> promise {
                let asked = ResizeArray()

                let contents = {|
                    entries = [| file "a.md" |]
                    truncated = true
                |}

                let source = PathCompletion.source docs (listing (Some contents) asked)
                let! result = source (contextAt "[x](./" [||])

                match result with
                | Some result -> Vitest.expect(result.validFor.IsNone).toBe true
                | None -> failwith "Expected completions"
            }
        )

        Vitest.test (
            "gives nothing for a missing folder, and asks nothing in code or outside a target",
            fun () -> promise {
                let asked = ResizeArray()
                let missing = PathCompletion.source docs (listing None asked)

                let! inMissing = missing (contextAt "[x](./gone/" [||])
                Vitest.expect(inMissing.IsNone).toBe true
                Vitest.expect(asked.Count).toBe 1

                let! inCode = missing (contextAt "`[x](./" [| "InlineCode"; "Paragraph"; "Document" |])
                let! inText = missing (contextAt "just text ./" [||])
                Vitest.expect(inCode.IsNone).toBe true
                Vitest.expect(inText.IsNone).toBe true
                Vitest.expect(asked.Count).toBe 1
            }
        )

        Vitest.test (
            "asks for folders in text and raw HTML but not in code, with the editor's Markdown parser",
            fun () -> promise {
                let description = languages |> Array.find (fun d -> d.name = "Markdown")
                let! markdown = description.load ()

                // The folders listed when completion is asked at the end of before, with after following it.
                let foldersAsked (before: string) (after: string) = promise {
                    let asked = ResizeArray<ListFolderRequest>()
                    let empty = Some {| entries = [||]; truncated = false |}

                    let completion =
                        TextCompletion.extension (PathCompletion.source docs (listing empty asked))

                    let state =
                        EditorStateApi.create {|
                            doc = before + after
                            extensions = extensions [| markdown; completion |]
                        |}

                    let pos = before.Length

                    // All sources at the position, as the editor asks them, including the HTML tags of Markdown.
                    let! _ =
                        state.languageDataAt<CompletionSource> ("autocomplete", pos)
                        |> Array.map (fun source -> source (CompletionContextApi.Create(state, pos, false)))
                        |> Promise.all

                    return asked |> Seq.map _.folder |> Array.ofSeq
                }

                let! inText = foldersAsked "See [x](./" ")"
                let! inHtml = foldersAsked "<p><img src=\"../" "\"></p>"
                let! inCodeSpan = foldersAsked "Use `[x](./" ")` here"
                let! inFence = foldersAsked "```md\n[x](./" ")\n```"

                Vitest.expect(inText).toEqual [| docs |]
                Vitest.expect(inHtml).toEqual [| "" |]
                Vitest.expect(inCodeSpan).toEqual [||]
                Vitest.expect(inFence).toEqual [||]
            }
        )

        Vitest.test (
            "passes a failed listing on as a rejection",
            fun () -> promise {
                let failing (_: ListFolderRequest) : JS.Promise<FolderContents option> =
                    Promise.reject (exn "permission denied")

                let! outcome =
                    PathCompletion.source docs failing (contextAt "[x](./" [||])
                    |> Promise.map (fun _ -> "resolved")
                    |> Promise.catch (fun e -> e.Message)

                Vitest.expect(outcome).toBe "permission denied"
            }
        )
)
