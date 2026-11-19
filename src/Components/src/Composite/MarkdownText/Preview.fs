namespace Swate.Components.Composite.MarkdownText

open System
open Fable.Core
open Fable.Core.JsInterop
open Feliz

open Swate.Components.Composite.MarkdownText.JsBindings
open Swate.Components.Composite.MarkdownText.Links
open Swate.Components.Composite.MarkdownText.Links.Types
open Swate.Components.Composite.TextEditor

module private PreviewTypes =

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

open PreviewTypes

[<RequireQualifiedAccess>]
module Preview =

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

    let private tryGetCode (props: obj) =
        let node: obj = props?node

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
            let children: obj = node?children

            if isNullOrUndefined children then
                None
            else
                Some(ReactMDEditor.getCodeString children)

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
            yield! customIf "children" props.children
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
                    yield! customIf "children" props.children
                ]
            | Some raw ->
                let anchor (extra: IReactProperty list) =
                    Html.a [
                        prop.href raw
                        yield! optionalProps props
                        yield! extra
                        yield! customIf "children" props.children
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
                            // The sanitizer prefixes every heading id.
                            let target = Browser.Dom.document.getElementById ("user-content-" + id)

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

    /// The default schema keeps picture and source, whose srcset is not checked for its protocol. Dropping both
    /// leaves the img inside, which the image renderer then resolves.
    let private sanitizeSchema =
        let schema =
            JS.Constructors.Object.assign (createEmpty<ISanitizeSchema>, ReactMDEditor.defaultSchema)
            |> unbox<ISanitizeSchema>

        schema.tagNames <-
            ReactMDEditor.defaultSchema.tagNames
            |> Array.filter (fun name -> name <> "picture" && name <> "source")

        schema

    /// Raw HTML runs before the sanitizer, so the sanitizer sees the final tree.
    let rehypePlugins: obj[] = [|
        ReactMDEditor.rehypeRaw
        box [| ReactMDEditor.rehypeSanitize; box sanitizeSchema |]
    |]
