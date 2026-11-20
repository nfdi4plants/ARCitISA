module Renderer.Components.MainContent.EmptySelectionTarget

open Feliz

[<ReactComponent>]
let EmptySelectionTarget () =
    Html.div [
        prop.className "swt:size-full swt:flex swt:justify-center swt:items-center"
        prop.children [
            Html.h1 [
                prop.className
                    "swt:text-xl swt:font-semibold swt:text-transparent swt:bg-clip-text swt:bg-linear-to-r swt:from-primary swt:to-secondary"
                prop.text "Select files in the file explorer to edit them."
            ]
        ]
    ]
