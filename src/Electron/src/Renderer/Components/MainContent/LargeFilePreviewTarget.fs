module Renderer.Components.MainContent.LargeFilePreviewTarget

open Feliz
open Swate.Electron.Shared.FileIOTypes
open Renderer

module Paged = Swate.Components.Page.GitComparison.GitPagedDiffTypes

/// The page of a file without text preview. The button opens the file in the default application.
[<ReactComponent>]
let LargeFilePreviewTarget (path: string, sizeBytes: float, reason: NoPreviewReason) =
    let openFailure, setOpenFailure = React.useState<string option> None

    let fileName = path.Split('/') |> Array.last

    let message =
        match reason with
        | NoPreviewReason.TooLarge -> "This file is too large to preview."
        | NoPreviewReason.Binary -> "This file has no text preview."

    let openExternally () =
        promise {
            let! result = Api.ipcArcVaultApi.openPathWithDefaultApplication path

            match result with
            | Ok() -> setOpenFailure None
            | Error error -> setOpenFailure (Some error.Message)
        }
        |> ignore

    Html.div [
        prop.className "swt:size-full swt:flex swt:flex-col swt:gap-2 swt:justify-center swt:items-center"
        prop.children [
            Html.h1 [
                prop.className "swt:text-lg swt:font-semibold"
                prop.text fileName
            ]
            Html.p [ prop.text (Paged.formatBytes sizeBytes) ]
            Html.p [ prop.text message ]
            Html.button [
                prop.className "swt:btn swt:btn-primary"
                prop.text "Open with default application"
                prop.onClick (fun _ -> openExternally ())
            ]
            match openFailure with
            | Some failure -> Html.p [ prop.className "swt:text-error"; prop.text failure ]
            | None -> ()
        ]
    ]
