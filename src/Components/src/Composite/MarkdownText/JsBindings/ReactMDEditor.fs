namespace Swate.Components.Composite.MarkdownText.JsBindings

open Browser.Types
open Fable.Core
open Feliz

[<AllowNullLiteral>]
type ITextRange =
    abstract member start: int
    abstract member ``end``: int

[<AllowNullLiteral>]
type ITextState =
    abstract member text: string
    abstract member selectedText: string
    abstract member selection: ITextRange

[<AllowNullLiteral>]
type ITextAreaTextApi =
    abstract member replaceSelection: string -> ITextState
    abstract member setSelectionRange: ITextRange -> ITextState
    abstract member textArea: HTMLTextAreaElement with get

[<AllowNullLiteral>]
type ICommand =
    abstract member name: string with get
    abstract member keyCommand: string with get
    abstract member icon: ReactElement with get
    abstract member buttonProps: obj with get
    abstract member execute: ITextState -> ITextAreaTextApi -> unit

[<Import("TextAreaCommandOrchestrator", "@uiw/react-md-editor/nohighlight")>]
type TextAreaCommandOrchestrator(textArea: HTMLTextAreaElement) =
    member _.textArea: HTMLTextAreaElement = jsNative
    member _.executeCommand(command: ICommand) : unit = jsNative

/// The part of the rehype-sanitize schema that the preview changes. The schema has more fields, which stay as they are.
[<AllowNullLiteral>]
type ISanitizeSchema =
    abstract member tagNames: string[] with get, set

[<Erase>]
type ReactMDEditor =

    [<Import("commands", "@uiw/react-md-editor/nohighlight")>]
    static member commands: obj = jsNative

    [<Import("handleKeyDown", "@uiw/react-md-editor/nohighlight")>]
    static member handleKeyDown(e: KeyboardEvent, ?tabSize: int, ?defaultTabEnable: bool) : unit = jsNative

    [<Import("shortcuts", "@uiw/react-md-editor/nohighlight")>]
    static member shortcuts(e: KeyboardEvent, commands: ICommand[], orchestrator: TextAreaCommandOrchestrator) : unit =
        jsNative

    [<Import("getCodeString", "rehype-rewrite")>]
    static member getCodeString(children: obj) : string = jsNative

    [<ImportDefault("rehype-sanitize")>]
    static member rehypeSanitize: obj = jsNative

    [<Import("defaultSchema", "rehype-sanitize")>]
    static member defaultSchema: ISanitizeSchema = jsNative

    [<ImportDefault("rehype-raw")>]
    static member rehypeRaw: obj = jsNative

    [<ReactComponent("default", "@uiw/react-markdown-preview/nohighlight")>]
    static member MarkdownPreview
        (source: string, ?className: string, ?components: obj, ?rehypePlugins: obj[], ?wrapperElement: obj, ?style: obj)
        =
        React.Imported()
