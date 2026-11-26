/// The CM6 theme of the TextEditor, built only from DaisyUI CSS variables. CM6 styles are plain CSS,
/// so var(--color-*) and color-mix() resolve live and a data-theme switch needs no code.
module Arcitisa.Components.Composite.TextEditor.Theme

open Fable.Core.JsInterop
open Arcitisa.Components.Composite.TextEditor.JsBindings.CodeMirror

let private mix (variable: string) (percent: int) =
    $"color-mix(in oklch, var({variable}) {percent}%%, transparent)"

/// A token color moved towards the text color, so it stays readable on light and dark backgrounds.
/// OKLab keeps the hue. OKLCH would turn it towards the hue of the text color.
let private tint (variable: string) =
    $"color-mix(in oklab, var({variable}) 60%%, var(--color-base-content))"

let private style (pairs: (string * string) list) : obj =
    pairs |> List.map (fun (k, v) -> k, box v) |> createObj

// @codemirror/lint draws its underlines and gutter dots as SVG data URLs with fixed colors.
// These rules replace them with the theme colors.
let private wavy (variable: string) =
    style [
        "backgroundImage", "none"
        "textDecoration", $"underline wavy var({variable})"
        "textDecorationSkipInk", "none"
        "textUnderlineOffset", "3px"
    ]

let private dot (variable: string) =
    style [
        "content", "normal"
        "width", "0.6em"
        "height", "0.6em"
        "margin", "0.2em"
        "borderRadius", "50%"
        "backgroundColor", $"var({variable})"
    ]

let private editorTheme: Extension =
    EditorViewApi.theme (
        createObj [
            "&",
            style [
                "color", "var(--color-base-content)"
                "backgroundColor", "var(--color-base-100)"
                "height", "100%"
                "fontSize", "13px"
            ]
            "&.cm-focused", style [ "outline", "none" ]
            ".cm-scroller",
            style [
                "fontFamily", "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace"
            ]
            ".cm-content", style [ "caretColor", "var(--color-primary)" ]
            ".cm-cursor, .cm-dropCursor", style [ "borderLeftColor", "var(--color-primary)" ]
            "&.cm-focused > .cm-scroller > .cm-selectionLayer .cm-selectionBackground, .cm-selectionBackground, .cm-content ::selection",
            style [ "backgroundColor", mix "--color-primary" 30 ]
            ".cm-gutters",
            style [
                "backgroundColor", "var(--color-base-200)"
                "color", mix "--color-base-content" 55
                "borderRight", "1px solid var(--color-base-300)"
            ]
            ".cm-activeLine", style [ "backgroundColor", mix "--color-base-content" 5 ]
            ".cm-activeLineGutter", style [ "backgroundColor", "var(--color-base-300)" ]
            ".cm-foldPlaceholder",
            style [
                "backgroundColor", "var(--color-base-300)"
                "color", "var(--color-base-content)"
                "border", "none"
            ]
            ".cm-searchMatch", style [ "backgroundColor", mix "--color-warning" 30 ]
            ".cm-searchMatch.cm-searchMatch-selected", style [ "backgroundColor", mix "--color-warning" 55 ]
            ".cm-selectionMatch", style [ "backgroundColor", mix "--color-info" 20 ]
            "&.cm-focused .cm-matchingBracket", style [ "backgroundColor", mix "--color-success" 30 ]
            "&.cm-focused .cm-nonmatchingBracket", style [ "backgroundColor", mix "--color-error" 30 ]
            ".cm-tooltip",
            style [
                "backgroundColor", "var(--color-base-200)"
                "color", "var(--color-base-content)"
                "border", "1px solid var(--color-base-300)"
                "borderRadius", "var(--radius-box)"
            ]
            ".cm-tooltip-autocomplete ul li[aria-selected]",
            style [
                "backgroundColor", mix "--color-primary" 35
                "color", "var(--color-base-content)"
            ]
            // An entry with its own icon would otherwise also get CodeMirror's empty icon box in front of it.
            $".cm-completionIcon-{TextCompletion.IconType}", style [ "display", "none" ]
            $".{TextCompletion.IconClass}",
            style [
                "marginRight", "0.4em"
                "verticalAlign", "-0.125em"
                "opacity", "0.75"
            ]
            ".cm-panels",
            style [
                "backgroundColor", "var(--color-base-200)"
                "color", "var(--color-base-content)"
            ]
            ".cm-panels-top", style [ "borderBottom", "1px solid var(--color-base-300)" ]
            ".cm-panels-bottom", style [ "borderTop", "1px solid var(--color-base-300)" ]
            ".cm-textfield",
            style [
                "backgroundColor", "var(--color-base-100)"
                "color", "var(--color-base-content)"
                "border", "1px solid var(--color-base-300)"
                "borderRadius", "var(--radius-field)"
            ]
            ".cm-button",
            style [
                "backgroundImage", "none"
                "backgroundColor", "var(--color-base-300)"
                "color", "var(--color-base-content)"
                "border", "1px solid var(--color-base-300)"
                "borderRadius", "var(--radius-field)"
            ]
            ".cm-panel.cm-panel-lint ul [aria-selected]", style [ "backgroundColor", mix "--color-primary" 25 ]
            ".cm-panel.cm-panel-lint ul:focus [aria-selected]",
            style [
                "backgroundColor", mix "--color-primary" 40
                "color", "var(--color-base-content)"
            ]
            ".cm-lintRange-error", wavy "--color-error"
            ".cm-lintRange-warning", wavy "--color-warning"
            ".cm-lintRange-info", wavy "--color-info"
            ".cm-lintRange-hint", wavy "--color-info"
            ".cm-lintPoint-error:after", style [ "borderBottomColor", "var(--color-error)" ]
            ".cm-lintPoint-warning:after", style [ "borderBottomColor", "var(--color-warning)" ]
            ".cm-diagnostic", style [ "padding", "4px 8px"; "fontFamily", "inherit" ]
            ".cm-diagnostic-error", style [ "borderLeft", "4px solid var(--color-error)" ]
            ".cm-diagnostic-warning", style [ "borderLeft", "4px solid var(--color-warning)" ]
            ".cm-diagnostic-info", style [ "borderLeft", "4px solid var(--color-info)" ]
            ".cm-diagnosticSource", style [ "color", mix "--color-base-content" 60 ]
            ".cm-lint-marker-error", dot "--color-error"
            ".cm-lint-marker-warning", dot "--color-warning"
            ".cm-lint-marker-info", dot "--color-info"
        ]
    )

/// The syntax colors. The editor uses them, and CodeHighlight uses them for code outside of an editor.
let tagStyles: TagStyle[] =
    let t = tags

    [|
        TagStyle([| t.propertyName; t.definition t.propertyName |], color = tint "--color-info")
        TagStyle([| t.string; t.special t.string |], color = tint "--color-success")
        TagStyle([| t.number; t.integer; t.float |], color = tint "--color-warning")
        TagStyle([| t.bool; t.``null``; t.atom; t.keyword |], color = tint "--color-accent")
        TagStyle(
            [| t.comment; t.lineComment; t.blockComment |],
            color = mix "--color-base-content" 55,
            fontStyle = "italic"
        )
        TagStyle([| t.punctuation; t.separator; t.brace; t.squareBracket |], color = mix "--color-base-content" 70)
        TagStyle([| t.meta; t.labelName; t.typeName |], color = tint "--color-primary")
        TagStyle([| t.heading |], color = tint "--color-primary", fontWeight = "bold")
        TagStyle([| t.emphasis |], fontStyle = "italic")
        TagStyle([| t.strong |], fontWeight = "bold")
        TagStyle([| t.link; t.url |], color = tint "--color-info", textDecoration = "underline")
        TagStyle([| t.monospace; t.quote |], color = mix "--color-base-content" 80)
        TagStyle([| t.invalid |], color = tint "--color-error")
    |]

let private highlight: Extension =
    syntaxHighlighting (HighlightStyleApi.define tagStyles)

/// The editor colors, the syntax colors and the lint marks.
let daisyTheme: Extension = extensions [| editorTheme; highlight |]
