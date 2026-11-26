/// Highlights code outside of an editor, such as the code blocks of the Markdown preview, with the grammars and
/// colors of the TextEditor. A grammar loads on first use, and @codemirror/language-data keeps it for later blocks.
module Arcitisa.Components.Composite.TextEditor.CodeHighlight

open Fable.Core
open Arcitisa.Components.Composite.TextEditor.JsBindings.CodeMirror
open Arcitisa.Components.Composite.TextEditor.JsBindings.Lezer

/// The inline style of a highlighted run, usable as a React style object. Unset fields stay undefined.
[<Global>]
type SegmentStyle
    [<ParamObject; Emit("$0")>]
    (?color: string, ?fontStyle: string, ?fontWeight: string, ?textDecoration: string) =
    member val color: string option = jsNative with get, set
    member val fontStyle: string option = jsNative with get, set
    member val fontWeight: string option = jsNative with get, set
    member val textDecoration: string option = jsNative with get, set

/// A run of code text. style is None for plain text.
type Segment = {|
    text: string
    style: SegmentStyle option
|}

/// The language of a code block by its fence name, such as "python" in ```python. Names and aliases come first,
/// then file extensions, so "py" finds Python. Case does not matter.
let findLanguage (descriptions: LanguageDescription[]) (fenceName: string) : LanguageDescription option =
    let name = fenceName.Trim().ToLowerInvariant()
    let same (value: string) = value.ToLowerInvariant() = name

    if name = "" then
        None
    else
        descriptions
        |> Array.tryFind (fun d -> same d.name || Array.exists same d.alias)
        |> Option.orElseWith (fun () -> descriptions |> Array.tryFind (fun d -> Array.exists same d.extensions))

// Each rule of the theme gets its index as class name, so the classes of a run lead back to its colors.
let private highlighter =
    Theme.tagStyles
    |> Array.mapi (fun i rule -> TagClass(rule.tag, string i))
    |> tagHighlighter

/// The style of the rules that classes name. Where two rules set the same field, the later one wins.
let private styleOf (classes: string) : SegmentStyle =
    let rules =
        classes.Split([| ' ' |], System.StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun c -> Theme.tagStyles.[int c])

    let last (field: TagStyle -> string option) =
        rules |> Array.choose field |> Array.tryLast

    SegmentStyle(
        ?color = last _.color,
        ?fontStyle = last _.fontStyle,
        ?fontWeight = last _.fontWeight,
        ?textDecoration = last _.textDecoration
    )

/// Splits code into highlighted runs with the grammar that fenceName names. None when no grammar has that name.
/// The promise fails when the grammar cannot be loaded.
let highlight (fenceName: string) (code: string) : JS.Promise<Segment[] option> =
    match findLanguage languages fenceName with
    | None -> Promise.lift None
    | Some description -> promise {
        let! loaded = description.load ()
        let tree = (unbox<LanguageSupport> loaded).language.parser.parse code
        let segments = ResizeArray<Segment>()

        highlightCode (
            code,
            tree,
            highlighter,
            System.Action<string, string>(fun text classes ->
                segments.Add {|
                    text = text
                    style = if classes = "" then None else Some(styleOf classes)
                |}
            ),
            System.Action(fun () -> segments.Add {| text = "\n"; style = None |})
        )

        return Some(segments.ToArray())
      }
