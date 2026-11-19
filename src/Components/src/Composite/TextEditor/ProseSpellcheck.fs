/// Keeps the browser's spellcheck off the parts of a prose document that are code, markup or addresses, such as
/// <img src="...">, `code` and link URLs in Markdown. Prose keeps its spellcheck. The language's prose rules name the
/// syntax nodes to skip, so a language without a syntax tree, such as plain text, gets none.
module Swate.Components.Composite.TextEditor.ProseSpellcheck

open System.Text.RegularExpressions
open Fable.Core.JsInterop
open Swate.Components.Composite.TextEditor.JsBindings.CodeMirror
open Swate.Components.Composite.TextEditor.JsBindings.Lezer
open Swate.Components.Composite.TextEditor.LanguageDetection

/// A node of skippedTagsIn can hold prose between its tags, so only its tags are skipped.
let private htmlTag = Regex(@"<[^>]*>")

/// The ranges in from..to that the spellcheck skips, in document order. slice from to reads the document text.
/// Every range is non-empty, because a mark on an empty range throws.
let skippedRanges
    (rules: ProseRules)
    (top: SyntaxNode)
    (slice: int -> int -> string)
    (from: int)
    (``to``: int)
    : (int * int)[] =
    let found = ResizeArray<int * int>()

    let rec visit (node: SyntaxNode) =
        if node.``to`` > from && node.from < ``to`` then
            if Array.contains node.name rules.skippedNodes then
                if node.from < node.``to`` then
                    found.Add(node.from, node.``to``)
            elif Array.contains node.name rules.skippedTagsIn then
                for m in htmlTag.Matches(slice node.from node.``to``) do
                    found.Add(node.from + m.Index, node.from + m.Index + m.Length)
            else
                let mutable child = node.firstChild

                while not (isNull child) do
                    visit child
                    child <- child.nextSibling

    visit top
    found.ToArray()

let private skipMark =
    DecorationApi.mark (MarkDecorationSpec(attributes = createObj [ "spellcheck" ==> "false" ]))

/// The view reads the decorations after each update, so only the drawn ranges of the document are walked.
let extension (rules: ProseRules) : Extension =
    EditorViewApi.decorations.``of`` (fun (view: EditorView) ->
        let top = (syntaxTree view.state).topNode
        let doc = view.state.doc
        let slice from ``to`` = doc.sliceString (from, ``to``, "\n")

        view.visibleRanges
        |> Array.collect (fun range -> skippedRanges rules top slice range.from range.``to``)
        |> Array.distinct
        |> Array.map (fun (from, ``to``) -> skipMark.range (from, ``to``))
        |> fun ranges -> DecorationApi.set (ranges, true)
    )
