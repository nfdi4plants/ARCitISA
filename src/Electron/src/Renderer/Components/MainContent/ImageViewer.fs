module Renderer.Components.MainContent.ImageViewer

open Feliz

[<ReactComponent>]
let Main (filetype: string, base64Content: string) =
    let imageUrl = "data:" + filetype + ";base64," + base64Content

    Html.div [
        prop.className "swt:w-full swt:h-full swt:flex swt:items-center swt:justify-center swt:overflow-auto"
        prop.children [
            Html.img [
                prop.src imageUrl
                prop.alt "PNG preview"
                prop.className "swt:max-w-full swt:max-h-full swt:object-contain"
            ]
        ]
    ]
 