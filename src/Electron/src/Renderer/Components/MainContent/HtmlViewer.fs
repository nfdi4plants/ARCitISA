module Renderer.Components.MainContent.HtmlViewer

open Feliz

[<ReactComponent>]
let Main (base64Content: string) =
    let htmlContent = "data:text/html;base64," + base64Content

    Html.div [
        prop.className
            "swt:w-full swt:h-full swt:overflow-auto swt:p-4"
        prop.children [
            Html.iframe [
                prop.src htmlContent
                prop.title "HTML preview"
                prop.className
                    "swt:block swt:w-full swt:h-full swt:border-none"
            ]

            Html.div [
                prop.className
                    "swt:w-full swt:max-w-2xl swt:rounded swt:bg-base-200 swt:p-3"
                prop.children [
                    Html.p [
                        prop.className "swt:font-bold swt:text-lg"
                        prop.text "Metadata"
                    ]
                    Html.p [
                        prop.text "File type: text/html"
                    ]
                    Html.p [
                        prop.text (
                            "Size: "
                            + (base64Content.Length / 1024).ToString()
                            + " KB"
                        )
                    ]
                ]
            ]
        ]
    ]
            
