namespace Swate.Components.Page.FileViewer

open Feliz
open Fable.Core
open Fable.Core.JsInterop

module private ImageMetadata =

    [<Emit("atob($0).length")>]
    let byteLength (base64Content: string) : int = jsNative

    [<Import("parse", "exifr")>]
    let parse (dataUrl: string) : JS.Promise<obj> = jsNative

    [<Emit("JSON.stringify($0, null, 2)")>]
    let stringify (value: obj) : string = jsNative

    [<Emit("$0 === undefined || $0 === null")>]
    let isEmpty (value: obj) : bool = jsNative

    [<Emit("$0.target.naturalWidth")>]
    let naturalWidth (event: Browser.Types.Event) : int = jsNative

    [<Emit("$0.target.naturalHeight")>]
    let naturalHeight (event: Browser.Types.Event) : int = jsNative

[<Erase; Mangle(false)>]
type ImageViewer =
    [<ReactComponent>]
    static member Main(filetype: string, base64Content: string, path: string) =
        let imageUrl = "data:" + filetype + ";base64," + base64Content
        let failedImageUrl, setFailedImageUrl = React.useState<string option> (None)
        let exifMetadata, setExifMetadata = React.useState<string option> (None)
        let dimensions, setDimensions = React.useState<(int * int) option> (None)
        let fileSize = ImageMetadata.byteLength base64Content / 1024

        React.useEffect (
            (fun () ->
                promise {
                    try
                        let! parsedMetadata = ImageMetadata.parse imageUrl

                        if ImageMetadata.isEmpty parsedMetadata then
                            setExifMetadata (Some "No additional info found.")
                        else
                            setExifMetadata (Some(ImageMetadata.stringify parsedMetadata))
                    with ex ->
                        setExifMetadata (Some($"No additional info available: {ex.Message}"))
                }
                |> Promise.start
            ),
            [| box base64Content |]
        )

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
                            prop.onLoad (fun event ->
                                setDimensions (
                                    Some(ImageMetadata.naturalWidth event, ImageMetadata.naturalHeight event)
                                )
                            )
                            prop.onError (fun (_: Browser.Types.Event) -> setFailedImageUrl (Some imageUrl))
                        ]
                        if failedImageUrl = Some imageUrl then
                            Html.div [
                                prop.role.alert
                                prop.className "swt:badge swt:badge-error swt:text-error-content"
                                prop.text "This file is broken or corrupted."
                            ]
                    ]
                ]
                Html.div [
                    prop.className "swt:w-full swt:max-w-full swt:rounded swt:bg-base-200 swt:p-3"
                    prop.children [
                        Html.p [
                            prop.className "swt:font-bold swt:text-lg"
                            prop.text "Metadata"
                        ]
                        Html.pre [
                            prop.className "swt:max-h-[50vh] swt:overflow-auto swt:whitespace-pre-wrap"
                            prop.text (
                                [
                                    Some $"File: {path}"
                                    Some $"MIME type: {filetype}"
                                    Some $"File size: {fileSize} kB"
                                    dimensions
                                    |> Option.map (fun (width, height) -> $"Dimensions: {width} x {height} pixels")
                                    exifMetadata |> Option.map (fun value -> $"Additional info:\n{value}")
                                ]
                                |> List.choose id
                                |> String.concat "\n"
                            )
                        ]
                    ]
                ]
            ]
        ]
