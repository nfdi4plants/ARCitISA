module Renderer.Components.MainContent.ProvenanceGraphArea

// This module must not reference the target or the page. Main imports it, and everything it imports lands in
// the startup bundle. The target stays behind the lazy import of Main, so the page, the canvas and the layout
// library load when the user opens the graph.

open Feliz
open Arcitisa.Components.Primitive.ErrorBoundary

/// An alert with an icon, what failed and a hint. A page open shows the ARC as it was at that time, so the hint
/// sends the user to close the graph and open it again after a fix.
[<ReactComponent>]
let ErrorAlert (message: string) =
    Html.div [
        prop.className "swt:w-full swt:min-w-0 swt:p-3"
        prop.testId "provenance-graph-error"
        prop.children [
            Html.div [
                prop.className "swt:alert swt:alert-error"
                prop.role.alert
                prop.children [
                    Html.i [
                        prop.className "swt:iconify swt:fluent--error-circle-24-regular swt:size-5"
                    ]
                    Html.div [
                        Html.p [ prop.className "swt:m-0"; prop.text message ]
                        Html.p [
                            prop.className "swt:m-0 swt:text-xs"
                            prop.text "After you fix the ARC, close and open the graph again."
                        ]
                    ]
                ]
            ]
        ]
    ]

/// The main content area of the provenance graph page. The request id changes with every open of the page, and a
/// new id mounts the content inside again, so every open starts from scratch. The error boundary shows what
/// failed when the content throws while it renders, and that includes the lazy chunk of the target failing to
/// load, which the target cannot catch itself.
let ProvenanceGraphArea (requestId: string) (content: ReactElement) : ReactElement =
    React.KeyedFragment(
        requestId,
        [
            ErrorBoundary.ErrorBoundary(
                content,
                (fun error -> ErrorAlert $"The provenance graph could not be shown: {error.Message}")
            )
        ]
    )
