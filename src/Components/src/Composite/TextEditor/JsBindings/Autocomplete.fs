/// Typed bindings for the parts of @codemirror/autocomplete that the TextEditor uses. basicSetup turns the
/// autocompletion on, these bindings add sources and settings to it.
module Arcitisa.Components.Composite.TextEditor.JsBindings.Autocomplete

open Browser.Types
open Fable.Core
open Arcitisa.Components.Composite.TextEditor.JsBindings.CodeMirror

/// Where a completion source is asked for completions.
[<AllowNullLiteral>]
type CompletionContext =
    abstract state: EditorState
    abstract pos: int
    /// True when the user asked for completions with a key, such as Ctrl-Space, and false when typing asked.
    abstract explicit: bool

type CompletionContextStatic =
    /// For tests. In the editor, the autocompletion makes the contexts.
    [<EmitConstructor>]
    abstract Create: state: EditorState * pos: int * explicit: bool -> CompletionContext

[<Import("CompletionContext", "@codemirror/autocomplete")>]
let CompletionContextApi: CompletionContextStatic = jsNative

/// One entry of a completion list. CodeMirror passes the object on as it is, so swtIcon and swtReopen, which only
/// the TextEditor reads, come back with the entry. Compiles to a plain object, unset fields stay out.
[<Global>]
type Completion
    [<ParamObject; Emit("$0")>]
    (label: string, ?``type``: string, ?boost: int, ?swtIcon: string, ?swtReopen: bool) =
    /// Shown in the list, matched against the typed text and inserted.
    member val label: string = jsNative with get, set
    /// CodeMirror draws its icon with the class "cm-completionIcon-" + type.
    member val ``type``: string option = jsNative with get, set
    /// Moves the entry up (positive) or down among entries that match the typed text equally well.
    member val boost: int option = jsNative with get, set
    /// The CSS classes of the TextEditor's own icon for the entry.
    member val swtIcon: string option = jsNative with get, set
    /// The TextEditor opens the list again after the entry is applied.
    member val swtReopen: bool option = jsNative with get, set

/// Completions for the document range from..to, which the chosen entry replaces. to defaults to the cursor. While
/// validFor accepts the text of the range after an edit, CodeMirror filters the same entries again instead of asking
/// the source. Compiles to a plain object, unset fields stay out.
[<Global>]
type CompletionResult
    [<ParamObject; Emit("$0")>]
    (from: int, options: Completion[], ?``to``: int, ?validFor: string -> bool) =
    member val from: int = jsNative with get, set
    member val options: Completion[] = jsNative with get, set
    member val ``to``: int option = jsNative with get, set
    member val validFor: (string -> bool) option = jsNative with get, set

/// A completion source. CodeMirror treats a promise of undefined as no completions, and closes the list and logs
/// the error when the promise rejects.
type CompletionSource = CompletionContext -> JS.Promise<CompletionResult option>

/// The language data entry that adds a completion source, see EditorStateStatic.languageData.
type CompletionLanguageData = {| autocomplete: CompletionSource |}

/// An extra part of each entry of the list. CodeMirror's icon sits at position 20 and the label at 50. render
/// returns null to add nothing to an entry.
type CompletionOptionPart = {|
    render: Completion -> HTMLElement
    position: int
|}

/// Settings that join those of basicSetup. A setting without a merge rule, such as activateOnCompletion, must be set
/// only once per editor. Compiles to a plain object, unset fields stay out.
[<Global>]
type AutocompletionConfig
    [<ParamObject; Emit("$0")>]
    (?activateOnCompletion: Completion -> bool, ?addToOptions: CompletionOptionPart[]) =
    /// True for an entry after which the list opens again.
    member val activateOnCompletion: (Completion -> bool) option = jsNative with get, set
    member val addToOptions: CompletionOptionPart[] option = jsNative with get, set

/// The autocompletion with more settings. The state, plugin and keys are the instances basicSetup already holds,
/// so adding it to an editor with basicSetup adds only the settings.
[<Import("autocompletion", "@codemirror/autocomplete")>]
let autocompletion (config: AutocompletionConfig) : Extension = jsNative
