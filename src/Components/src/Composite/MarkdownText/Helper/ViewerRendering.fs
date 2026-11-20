/// The rendering of MarkdownTextViewer: the react-markdown renderers for code, links and images, the remark and
/// rehype plugins with the sanitizer schema, and the classes of the prose container.
module Swate.Components.Composite.MarkdownText.Helper.ViewerRendering

open System
open Fable.Core
open Fable.Core.JsInterop
open Feliz

open Swate.Components.Composite.MarkdownText
open Swate.Components.Composite.MarkdownText.JsBindings
open Swate.Components.Composite.MarkdownText.Links
open Swate.Components.Composite.MarkdownText.Links.Types
open Swate.Components.Composite.TextEditor

module private ViewerRenderingTypes =

    /// The props react-markdown passes to the "a" and "img" renderers. The sanitizer has already dropped what is
    /// unsafe, so every member can be missing.
    [<AllowNullLiteral>]
    type IElementProps =
        abstract href: string option
        abstract src: string option
        abstract alt: string option
        abstract title: string option
        abstract className: string option
        abstract id: string option
        abstract width: obj
        abstract height: obj
        abstract tabIndex: obj
        abstract ``aria-hidden``: obj
        abstract children: obj

open ViewerRenderingTypes

/// The prefix the sanitizer puts before every id, so an id from the document cannot clobber a global of the page.
let private clobberPrefix = "user-content-"

let mutable private MermaidInitialized = false

let private ensureMermaidInitialized () =
    if not MermaidInitialized then
        Mermaid.instance.initialize (createObj [ "startOnLoad" ==> false; "securityLevel" ==> "strict" ])

        MermaidInitialized <- true

/// The element Mermaid measures a diagram in. At the end of document.body that element makes the page taller
/// while the diagram renders, so the page shows a scrollbar and the whole layout narrows and snaps back. This host
/// is fixed to the top of the viewport, has no height and clips its content, so the page never grows. The
/// diagram keeps the viewport width to lay out in, like it had in document.body.
let private createMeasureHost () =
    let host = Browser.Dom.document.createElement "div"
    host.setAttribute ("aria-hidden", "true")

    host.setAttribute (
        "style",
        "position:fixed;top:0;left:0;right:0;height:0;overflow:hidden;visibility:hidden;pointer-events:none"
    )

    Browser.Dom.document.body.appendChild host |> ignore
    host

let private tryGetClassName (props: obj) =
    let className: obj = props?className

    if isNullOrUndefined className then
        None
    else
        Some(string className)

/// The text of a hast node and its descendants.
let rec private textOf (node: IHastNode) =
    match node.``type``, node.value, node.children with
    | "text", Some value, _ -> value
    | _, _, Some children -> children |> Seq.map textOf |> String.concat ""
    | _ -> ""

let private tryGetCode (props: obj) =
    let node: IHastNode = props?node

    if isNullOrUndefined node then
        let children: obj = props?children

        if isNullOrUndefined children then
            None
        else
            match children with
            | :? string as text -> Some text
            | _ ->
                try
                    children |> unbox<obj[]> |> Array.map string |> String.concat "" |> Some
                with _ ->
                    Some(string children)
    else
        Some(textOf node)

/// The fence name of a code block, such as "python" for the class "language-python".
let private tryGetFenceName (className: string) =
    className.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)
    |> Array.tryPick (fun c ->
        if c.StartsWith("language-", StringComparison.Ordinal) then
            Some(c.Substring "language-".Length)
        else
            None
    )

let private customIf (name: string) (value: obj) =
    if isNullOrUndefined value then
        []
    else
        [ prop.custom (name, value) ]

/// react-markdown passes a single child as it is and several as an array. Feliz spreads the children prop, which
/// fails on a single element, such as the icon of a heading link or the superscript of a footnote reference.
let private childrenOf (children: obj) =
    if isNullOrUndefined children then
        []
    elif JS.Constructors.Array.isArray children then
        [ prop.custom ("children", children) ]
    else
        [ prop.custom ("children", [| children |]) ]

let private optionalProps (props: IElementProps) = [
    match props.className with
    | Some className -> prop.className className
    | None -> ()
    match props.id with
    | Some id -> prop.id id
    | None -> ()
    match props.title with
    | Some title -> prop.title title
    | None -> ()
    yield! customIf "aria-hidden" props.``aria-hidden``
    yield! customIf "tabIndex" props.tabIndex
]

/// A link that cannot be followed. The message is the tooltip.
let private deadLink (props: IElementProps) (message: string) =
    Html.span [
        match props.id with
        | Some id -> prop.id id
        | None -> ()
        prop.className (
            "swt:underline swt:decoration-dotted swt:cursor-help"
            + (props.className |> Option.map (fun c -> " " + c) |> Option.defaultValue "")
        )
        prop.title message
        yield! childrenOf props.children
    ]

let private resolveWith (context: IMarkdownLinkContext option) (raw: string) =
    let baseFolder =
        match context with
        | Some c -> c.baseFolder
        | None -> ""

    LinkResolution.resolve baseFolder raw

let private imageNotice (alt: string option) (message: string) =
    Html.span [
        prop.role "img"
        prop.custom ("aria-label", defaultArg alt "image")
        prop.className
            "swt:inline-flex swt:items-center swt:gap-1 swt:rounded-field swt:bg-base-200 swt:px-2 swt:py-1 swt:text-sm"
        prop.children [
            Html.span [
                prop.className "swt:iconify swt:fluent--image-off-24-regular"
            ]
            Html.span [ prop.text message ]
        ]
    ]

/// The renderers react-markdown uses for code, a and img elements.
[<Erase; Mangle(false)>]
type private Renderers =

    [<ReactComponent>]
    static member CodeRenderer(props: obj) =
        let className = tryGetClassName props |> Option.defaultValue ""
        let code = tryGetCode props |> Option.defaultValue ""

        let isMermaid =
            className.StartsWith("language-mermaid", StringComparison.OrdinalIgnoreCase)

        let fenceName = tryGetFenceName className

        // The runs remember the fence name and code they came from, so changed code shows as plain text until its
        // own runs arrive, never with the colors of the old code.
        let highlighted, setHighlighted =
            React.useState (None: (string * string * CodeHighlight.Segment[]) option)

        React.useEffect (
            (fun () ->
                let cancelled = ref false

                match fenceName with
                | Some name when not isMermaid ->
                    promise {
                        try
                            let! result = CodeHighlight.highlight name code

                            if not cancelled.Value then
                                setHighlighted (result |> Option.map (fun segments -> name, code, segments))
                        with error ->
                            // The block stays plain text.
                            Browser.Dom.console.error ($"Highlighting a {name} code block failed:", error)
                    }
                    |> Promise.start
                | _ -> ()

                { new IDisposable with
                    member _.Dispose() = cancelled.Value <- true
                }
            ),
            [| box fenceName; box code; box isMermaid |]
        )

        // Mermaid uses the id in querySelector, so keep a non-digit prefix to avoid invalid selectors.
        let renderId =
            React.useRef (
                let rawId = Guid.NewGuid().ToString "N"
                sprintf "mermaid-%s" rawId
            )

        let svg, setSvg = React.useState (None: string option)
        let renderError, setRenderError = React.useState (None: string option)

        React.useEffect (
            (fun () ->
                if isMermaid then
                    if String.IsNullOrWhiteSpace code then
                        setSvg None
                        setRenderError (Some "Mermaid code block is empty.")
                    else
                        ensureMermaidInitialized ()

                        promise {
                            let host = createMeasureHost ()

                            try
                                try
                                    let! result = Mermaid.instance.render (renderId.current, code, host)
                                    setRenderError None
                                    setSvg (Some result.svg)
                                with err ->
                                    setSvg None
                                    setRenderError (Some $"Mermaid render error: {string err}")
                            finally
                                host.remove ()
                        }
                        |> Promise.start
            ),
            [| box isMermaid; box code |]
        )

        if isMermaid then
            match renderError, svg with
            | Some message, _ ->
                Html.pre [
                    prop.className "swt:p-3 swt:rounded-box swt:bg-base-200 swt:text-error swt:text-sm"
                    prop.text message
                ]
            | None, Some renderedSvg ->
                Html.div [
                    prop.className "swt:my-2"
                    prop.dangerouslySetInnerHTML renderedSvg
                ]
            | None, None ->
                Html.pre [
                    prop.className "swt:p-3 swt:rounded-box swt:bg-base-200 swt:text-sm"
                    prop.text "Rendering Mermaid diagram..."
                ]
        else
            Html.code [
                if className <> "" then
                    prop.className className
                match highlighted, fenceName with
                | Some(forName, forCode, segments), Some name when forName = name && forCode = code ->
                    prop.children [
                        for i, segment in Array.indexed segments ->
                            Html.span [
                                prop.key i
                                match segment.style with
                                | Some style -> prop.custom ("style", style)
                                | None -> ()
                                prop.text segment.text
                            ]
                    ]
                | _ -> prop.text code
            ]

    [<ReactComponent>]
    static member LinkRenderer(props: IElementProps) =
        let context = LinkContext.useLinkCtx ()

        match props.href with
        | None ->
            Html.a [
                yield! optionalProps props
                yield! childrenOf props.children
            ]
        | Some raw ->
            let anchor (extra: IReactProperty list) =
                Html.a [
                    prop.href raw
                    yield! optionalProps props
                    yield! extra
                    yield! childrenOf props.children
                ]

            match resolveWith context raw with
            | LinkTarget.External url ->
                anchor [
                    prop.rel "noreferrer"
                    match context with
                    | Some c ->
                        prop.onClick (fun e ->
                            e.preventDefault ()
                            c.openExternal url
                        )
                    | None -> ()
                ]
            | LinkTarget.Fragment id ->
                anchor [
                    prop.onClick (fun e ->
                        e.preventDefault ()

                        // The sanitizer prefixes every id, so a link names the id without the prefix. The
                        // footnotes come prefixed already and get the prefix twice. The heading links are added
                        // after the sanitizer and name the prefixed id itself.
                        let target =
                            match Browser.Dom.document.getElementById (clobberPrefix + id) with
                            | null when id.StartsWith(clobberPrefix, StringComparison.Ordinal) ->
                                Browser.Dom.document.getElementById id
                            | found -> found

                        if not (isNull target) then
                            target.scrollIntoView ()
                    )
                ]
            | LinkTarget.ArcPath(path, _) ->
                match context with
                | Some c ->
                    anchor [
                        prop.onClick (fun e ->
                            e.preventDefault ()
                            c.openArcPath path
                        )
                        prop.custom (
                            "onAuxClick",
                            fun (e: Browser.Types.MouseEvent) ->
                                if e.button = 1.0 then
                                    e.preventDefault ()
                                    c.openArcPath path
                        )
                    ]
                | None -> deadLink props "Links to files work only in a file of an open ARC."
            | LinkTarget.OutsideArc _ -> deadLink props "This link points outside the ARC."
            | LinkTarget.Unsupported(_, reason) -> deadLink props reason

    [<ReactComponent>]
    static member ImageRenderer(props: IElementProps) =
        let context = LinkContext.useLinkCtx ()
        // The failure remembers its path, so a new path starts without one.
        let failure, setFailure = React.useState (None: (string * string) option)

        let image (src: string) (onError: (Browser.Types.Event -> unit) option) =
            Html.img [
                prop.src src
                match props.alt with
                | Some alt -> prop.alt alt
                | None -> ()
                match props.title with
                | Some title -> prop.title title
                | None -> ()
                match props.className with
                | Some className -> prop.className className
                | None -> ()
                yield! customIf "width" props.width
                yield! customIf "height" props.height
                prop.custom ("loading", "lazy")
                prop.custom ("decoding", "async")
                match onError with
                | Some handler -> prop.onError (handler: Browser.Types.Event -> unit)
                | None -> ()
            ]

        match props.src with
        | None -> imageNotice props.alt "This image has no source that can be shown."
        | Some raw ->
            match resolveWith context raw with
            | LinkTarget.External url -> image url None
            | LinkTarget.ArcPath(path, _) ->
                match context with
                | None -> imageNotice props.alt "Images of the ARC are shown only for a file of an open ARC."
                | Some c ->
                    match failure with
                    | Some(failedPath, message) when failedPath = path -> imageNotice props.alt message
                    | _ ->
                        match c.assetUrl path with
                        | None -> imageNotice props.alt "Images of the ARC cannot be shown here."
                        | Some url ->
                            let fallback = $"The image could not be loaded: {path}"

                            image
                                url
                                (Some(fun (_: Browser.Types.Event) ->
                                    setFailure (Some(path, fallback))

                                    promise {
                                        try
                                            let! described = c.describeAsset path

                                            match described with
                                            | Some message -> setFailure (Some(path, message))
                                            | None -> ()
                                        with _ ->
                                            ()
                                    }
                                    |> Promise.start
                                ))
            | LinkTarget.OutsideArc _ -> imageNotice props.alt "This image lies outside the ARC."
            | LinkTarget.Fragment _ -> imageNotice props.alt "This image has no path."
            | LinkTarget.Unsupported(_, reason) -> imageNotice props.alt reason

let components =
    createObj [
        "code" ==> Renderers.CodeRenderer
        "a" ==> Renderers.LinkRenderer
        "img" ==> Renderers.ImageRenderer
    ]

type private Alert = {
    /// The kind in the marker class markdown-alert-<kind>.
    Kind: string
    Title: string
    Icon: string
    Border: string
    IconColor: string
}

/// The alerts of GitHub, such as "> [!NOTE]".
let private alerts = [
    {
        Kind = "note"
        Title = "Note"
        Icon = "swt:fluent--info-24-regular"
        Border = "swt:border-info"
        IconColor = "swt:text-info"
    }
    {
        Kind = "tip"
        Title = "Tip"
        Icon = "swt:fluent--lightbulb-24-regular"
        Border = "swt:border-success"
        IconColor = "swt:text-success"
    }
    {
        Kind = "important"
        Title = "Important"
        Icon = "swt:fluent--important-24-regular"
        Border = "swt:border-accent"
        IconColor = "swt:text-accent"
    }
    {
        Kind = "warning"
        Title = "Warning"
        Icon = "swt:fluent--warning-24-regular"
        Border = "swt:border-warning"
        IconColor = "swt:text-warning"
    }
    {
        Kind = "caution"
        Title = "Caution"
        Icon = "swt:fluent--error-circle-24-regular"
        Border = "swt:border-error"
        IconColor = "swt:text-error"
    }
]

let private markerOf (alert: Alert) = "markdown-alert-" + alert.Kind

let private hasClass (name: string) (node: IHastNode) =
    node.properties
    |> Option.bind _.className
    |> Option.exists (Array.contains name)

let private icon (classes: string[]) : IHastNode =
    unbox {|
        ``type`` = "element"
        tagName = "span"
        properties = {|
            className = Array.append [| "swt:iconify"; "swt:size-4"; "swt:shrink-0" |] classes
            ariaHidden = "true"
        |}
        children = ResizeArray<IHastNode>()
    |}

let private text (value: string) : IHastNode =
    unbox {| ``type`` = "text"; value = value |}

/// The default schema keeps picture and source, whose srcset is not checked for its protocol. Dropping both
/// leaves the img inside, which the image renderer then resolves. The marker classes of the alerts stay, so the
/// alert step after the sanitizer finds them. The icon of an alert is an svg, which the sanitizer drops.
let private sanitizeSchema =
    let defaults = ReactMarkdown.defaultSchema

    let allowClasses (existing: obj[] option) (classes: string list) =
        Array.append (defaultArg existing [||]) [| box (Array.ofList ("className" :: classes)) |]

    let attributes =
        JS.Constructors.Object.assign (createEmpty<ISanitizeAttributes>, defaults.attributes)
        |> unbox<ISanitizeAttributes>

    attributes.div <- Some(allowClasses defaults.attributes.div ("markdown-alert" :: List.map markerOf alerts))
    attributes.p <- Some(allowClasses defaults.attributes.p [ "markdown-alert-title" ])

    let schema =
        JS.Constructors.Object.assign (createEmpty<ISanitizeSchema>, defaults)
        |> unbox<ISanitizeSchema>

    schema.attributes <- attributes

    schema.tagNames <-
        defaults.tagNames
        |> Array.filter (fun name -> name <> "picture" && name <> "source")

    schema

let private decorateAlert (alert: Alert) (node: IHastNode) =
    node.properties
    |> Option.iter (fun properties ->
        properties.className <-
            Some(
                Array.append (defaultArg properties.className [||]) [|
                    "swt:my-4"
                    "swt:border-l-4"
                    alert.Border
                    "swt:ps-4"
                    "swt:py-1"
                    "swt:[&>:first-child]:mt-0"
                    "swt:[&>:last-child]:mb-0"
                |]
            )
    )

    node.children
    |> Option.bind (Seq.tryFind (hasClass "markdown-alert-title"))
    |> Option.iter (fun title ->
        title.properties
        |> Option.iter (fun properties ->
            // not-prose keeps the paragraph margins of Typography off the title.
            properties.className <-
                Some [|
                    "markdown-alert-title"
                    "not-prose"
                    "swt:flex"
                    "swt:items-center"
                    "swt:gap-2"
                    "swt:font-semibold"
                |]
        )

        title.children <- Some(ResizeArray [ icon [| alert.Icon; alert.IconColor |]; text alert.Title ])
    )

/// A rehype plugin that draws the alerts with a colored border, an icon and a title. It runs after the sanitizer,
/// which would drop its classes and icons.
let private decorateAlerts () =
    let rec visit (node: IHastNode) =
        node.children
        |> Option.iter (
            Seq.iter (fun child ->
                if child.``type`` = "element" then
                    if hasClass "markdown-alert" child then
                        alerts
                        |> List.tryFind (fun alert -> hasClass (markerOf alert) child)
                        |> Option.iter (fun alert -> decorateAlert alert child)

                    visit child
            )
        )

    System.Func<IHastNode, unit>(visit)

/// A link icon after every heading, shown while the pointer is over the heading. It runs after the sanitizer, so
/// its classes stay and its href is the prefixed id. not-prose keeps the link underline of Typography off it.
let private headingLinks = {|
    behavior = "append"
    headingProperties =
        fun (heading: IHastNode) -> {|
            className =
                Array.append (heading.properties |> Option.bind _.className |> Option.defaultValue [||]) [|
                    "swt:group"
                |]
        |}
    properties = {|
        ariaHidden = "true"
        tabIndex = -1
        className = [|
            "not-prose"
            "swt:ms-2"
            "swt:opacity-0"
            "swt:group-hover:opacity-100"
        |]
    |}
    content = icon [| "swt:fluent--link-24-regular"; "swt:align-middle" |]
|}

/// GitHub Flavored Markdown and GitHub alerts.
let remarkPlugins: obj[] = [| ReactMarkdown.remarkAlert; ReactMarkdown.remarkGfm |]

/// Raw HTML runs before the sanitizer, so the sanitizer sees the final tree. The heading ids come before the
/// sanitizer too, which prefixes them. The heading links and the alerts come after it, because they add classes
/// and icons of their own.
let rehypePlugins: obj[] = [|
    ReactMarkdown.rehypeRaw
    ReactMarkdown.rehypeSlug
    box [| ReactMarkdown.rehypeSanitize; box sanitizeSchema |]
    box [| ReactMarkdown.rehypeAutolinkHeadings; box headingLinks |]
    box decorateAlerts
|]

/// The look of rendered Markdown: Tailwind Typography, which daisyUI colors with the theme. Code blocks get the
/// base colors that the syntax highlighting is made for. Task lists drop their bullets, and the hidden title of the
/// footnotes stays hidden.
let proseClassName =
    "swt:prose swt:max-w-none swt:wrap-break-word swt:prose-a:text-primary swt:prose-pre:bg-base-200 swt:prose-pre:text-base-content swt:[&_.contains-task-list]:list-none swt:[&_.contains-task-list]:ps-0 swt:[&_.sr-only]:sr-only"
