namespace Swate.Components.Composite.MarkdownText

open System
open Fable.Core
open Fable.Core.JsInterop
open Feliz

open Swate.Components.Composite.MarkdownText.JsBindings
open Swate.Components.Composite.TextEditor

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

    [<ReactComponent>]
    let private CodeRenderer (props: obj) =
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

    let components = createObj [ "code" ==> CodeRenderer ]

    let rehypePlugins: obj[] = [| ReactMDEditor.rehypeSanitize |]
