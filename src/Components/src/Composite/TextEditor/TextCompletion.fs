/// Connects a TextCompletionSource to the autocompletion of the editor. The source sees the line of the cursor and
/// the syntax nodes around it, and its entries can carry an icon and open the list again after they are inserted.
module Arcitisa.Components.Composite.TextEditor.TextCompletion

open Browser.Types
open Fable.Core
open Arcitisa.Components.Composite.TextEditor.JsBindings.CodeMirror
open Arcitisa.Components.Composite.TextEditor.JsBindings.Lezer
open Arcitisa.Components.Composite.TextEditor.JsBindings.Autocomplete
open Arcitisa.Components.Composite.TextEditor.Types

/// The completion type of an entry with its own icon. The theme hides CodeMirror's icon for this type, so the
/// entry shows only its own.
[<Literal>]
let IconType = "swt-icon"

/// The class of the element that holds an entry's own icon.
[<Literal>]
let IconClass = "cm-swtCompletionIcon"

module private TextCompletionHelpers =

    let syntaxPathAt (state: EditorState) (pos: int) : string[] =
        let path = ResizeArray<string>()
        let mutable node = (syntaxTree state).resolveInner (pos, -1)

        while not (isNull node) do
            path.Add node.name
            node <- node.parent

        path.ToArray()

    let contextOf (context: CompletionContext) : TextCompletionContext =
        let doc = context.state.doc
        let line = doc.lineAt context.pos

        {|
            position = context.pos
            lineBefore = doc.sliceString (line.from, context.pos, "\n")
            lineAfter = doc.sliceString (context.pos, line.``to``, "\n")
            explicit = context.explicit
            syntaxPath = syntaxPathAt context.state context.pos
        |}

    let completionOf (option: TextCompletionOption) : Completion =
        Completion(
            option.label,
            ?``type`` = (option.iconClass |> Option.map (fun _ -> IconType)),
            ?boost = (if option.boost = 0 then None else Some option.boost),
            ?swtIcon = option.iconClass,
            ?swtReopen = (if option.reopen then Some true else None)
        )

    let resultOf (result: TextCompletionResult) : CompletionResult =
        CompletionResult(
            result.from,
            result.options |> Array.map completionOf,
            ``to`` = result.``to``,
            ?validFor = result.validFor
        )

    let renderIcon (completion: Completion) : HTMLElement =
        match completion.swtIcon with
        | Some iconClass ->
            let icon = Browser.Dom.document.createElement "span"
            icon.className <- IconClass + " " + iconClass
            icon.setAttribute ("aria-hidden", "true")
            icon
        | None -> null

    let reopensAfter (completion: Completion) =
        completion.swtReopen |> Option.defaultValue false

open TextCompletionHelpers

/// The source as a CodeMirror completion source. A read-only editor gets no completions, because inserting one
/// would change its text.
let toCompletionSource (source: TextCompletionSource) : CompletionSource =
    fun context ->
        if context.state.readOnly then
            Promise.lift None
        else
            source (contextOf context) |> Promise.map (Option.map resultOf)

/// Adds source to the completions of the editor, next to those of the language, such as HTML tags in Markdown.
/// Use it once per editor, because it also sets activateOnCompletion for the reopen option.
let extension (source: TextCompletionSource) : Extension =
    let data: CompletionLanguageData = {|
        autocomplete = toCompletionSource source
    |}

    extensions [|
        EditorStateApi.languageData.``of`` (fun _ -> [| box data |])
        autocompletion (
            AutocompletionConfig(
                activateOnCompletion = reopensAfter,
                addToOptions = [| {| render = renderIcon; position = 20 |} |]
            )
        )
    |]
