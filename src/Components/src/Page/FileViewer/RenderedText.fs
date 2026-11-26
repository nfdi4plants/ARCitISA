namespace Arcitisa.Components.Page.FileViewer

open Fable.Core
open Feliz
open Arcitisa.Components.Composite.MarkdownText
open Arcitisa.Components.Composite.MarkdownText.Links.Types
open Arcitisa.Components.Page.FileViewer.Types

[<Erase; Mangle(false)>]
type RenderedText =

    /// A text file as it looks: Markdown through the sanitizing preview (links resolve through linkContext), HTML in
    /// the sandboxed frame of HtmlViewer and SVG as an image.
    [<ReactComponent>]
    static member RenderedText(rendering: TextRendering, text: string, ?linkContext: IMarkdownLinkContext) =
        match rendering with
        | TextRendering.Markdown ->
            Html.div [
                prop.className "swt:flex-1 swt:min-h-0 swt:overflow-auto"
                prop.children [
                    MarkdownTextViewer.MarkdownTextViewer(text, className = "swt:p-4", ?linkContext = linkContext)
                ]
            ]
        | TextRendering.Html ->
            HtmlViewer.SandboxedFrame(
                text,
                className = "swt:flex-1 swt:min-h-0 swt:w-full swt:border-none swt:bg-white"
            )
        | TextRendering.Svg ->
            Html.div [
                prop.className "swt:flex-1 swt:min-h-0 swt:overflow-auto swt:p-4 swt:flex swt:justify-center"
                prop.children [
                    Html.img [
                        prop.alt "SVG preview"
                        prop.src ("data:image/svg+xml;charset=utf-8," + JS.encodeURIComponent text)
                        prop.className "swt:max-w-full swt:self-start"
                    ]
                ]
            ]

    /// Switches a text file between its source and how it looks. Markdown is written, so Edit comes first and the
    /// other side is called Preview. HTML and SVG are looked at, so View comes first.
    [<ReactComponent>]
    static member ModeToggle(rendering: TextRendering, showRendered: bool, onEdit: unit -> unit, onView: unit -> unit) =
        // DaisyUI radio buttons: the checked one gets the primary colors, the label comes from aria-label (drawn in
        // ::after) and the icon is drawn in ::before. Only the unchecked one is a ghost button, because .btn-ghost
        // would also clear the background of the checked one.
        let modeRadio (label: string) (iconClass: string) (active: bool) (onSelect: unit -> unit) =
            Html.input [
                prop.type'.radio
                prop.key label
                prop.name "text-document-mode"
                prop.ariaLabel label
                prop.isChecked active
                prop.onCheckedChange (fun isChecked ->
                    if isChecked then
                        onSelect ()
                )
                prop.className [
                    "swt:join-item swt:btn swt:btn-sm swt:before:iconify"
                    iconClass
                    if not active then
                        "swt:btn-ghost"
                ]
            ]

        let editRadio =
            modeRadio "Edit" "swt:before:fluent--edit-24-regular" (not showRendered) onEdit

        let viewRadio =
            modeRadio
                (if rendering = TextRendering.Markdown then
                     "Preview"
                 else
                     "View")
                "swt:before:fluent--eye-24-regular"
                showRendered
                onView

        Html.div [
            prop.role "radiogroup"
            prop.ariaLabel "Show the file as"
            prop.className "swt:join"
            prop.children (
                if rendering = TextRendering.Markdown then
                    [ editRadio; viewRadio ]
                else
                    [ viewRadio; editRadio ]
            )
        ]
