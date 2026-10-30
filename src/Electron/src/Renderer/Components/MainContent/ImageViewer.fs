module Renderer.Components.MainContent.ImageViewer

open Feliz

[<ReactComponent>]
let Main (filetype: string, base64Content: string) =
    let imageFailed, setImageFailed = React.useState false
    let imageUrl = "data:" + filetype + ";base64," + base64Content

    Html.div [
        prop.className "swt:w-full swt:h-full swt:flex swt:flex-col swt:items-center swt:overflow-auto swt:p-4"
        prop.children [
            Html.div [
                prop.className
                    "swt:flex swt:min-h-0 swt:w-full swt:flex-1 swt:flex-col swt:items-center swt:justify-center swt:gap-2"
                prop.children [
                    Html.img [
                        prop.src imageUrl
                        prop.alt "Image preview"
                        prop.className "swt:max-h-full swt:max-w-full swt:object-contain"
                        prop.onError (fun (_: Browser.Types.Event) -> setImageFailed true)
                    ]
                    if imageFailed then
                        Html.div [
                            prop.role.alert
                            prop.className "swt:badge swt:badge-error swt:text-error-content"
                            prop.text "This file is broken or corrupted."
                        ]
                ]
            ]
            Html.div [
                prop.className "swt:w-full swt:max-w-2xl swt:rounded swt:bg-base-200 swt:p-3"
                prop.children [
                    Html.p [
                        prop.className "swt:font-bold swt:text-lg"
                        prop.text "Metadata"
                    ]
                    Html.p [ prop.text ("File type: " + filetype) ]
                    Html.p [
                        prop.text ("size: " + (base64Content.Length / 1024).ToString() + " kb")
                    ]
                ]
            ]
        ]
    ]
