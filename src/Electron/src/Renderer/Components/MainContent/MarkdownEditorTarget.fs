module Renderer.Components.MainContent.MarkdownEditorTargetView

open Feliz
open Swate.Components.Composite.MarkdownText
open Swate.Components.Composite.MarkdownText.Types
open Swate.Components.Shared
open Swate.Electron.Shared.FileIOHelper
open Swate.Electron.Shared.FileIOTypes

[<ReactComponent(true)>]
let MarkdownEditorTarget (content: string) =
    let fileStateCtx = Renderer.Context.FileStateContext.useFileStateCtx ()

    let expansionCtx =
        Renderer.Context.FileTreeExpansionContext.useFileTreeExpansionCtx ()

    let markdown, setMarkdown = React.useState content
    let lastSavedContent, setLastSavedContent = React.useState content
    let isSaving, setIsSaving = React.useState false
    let saveError, setSaveError = React.useState (None: string option)

    let selectedPath =
        fileStateCtx.state.Selection.TreePath |> Option.map PathHelpers.normalizePath

    let hasUnsavedChanges = markdown <> lastSavedContent

    React.useEffect (
        (fun () ->
            setMarkdown content
            setLastSavedContent content
            setSaveError None
        ),
        [| box content |]
    )

    let writeMarkdown path =
        FileContentDTO.create FileContentType.Markdown markdown path
        |> Api.ipcArcVaultApi.writeFile

    let saveMarkdown () =
        match selectedPath with
        | None -> setSaveError (Some "No markdown file is selected.")
        | Some relativePath ->
            promise {
                setIsSaving true
                setSaveError None

                let! writeResult = writeMarkdown relativePath

                match writeResult with
                | Ok() ->
                    setLastSavedContent markdown
                    expansionCtx.refreshContentStamp ()
                | Error exn -> setSaveError (Some $"Failed to save markdown file: {exn.Message}")

                setIsSaving false
            }
            |> Promise.start

    let saveStatusText =
        match saveError with
        | Some err -> err
        | None when hasUnsavedChanges -> "Unsaved changes"
        | None -> "Saved"

    Html.div [
        prop.className
            "swt:size-full swt:min-w-0 swt:min-h-0 swt:overflow-auto swt:bg-base-100 swt:p-4 swt:flex swt:flex-col swt:gap-3"
        prop.children [
            Html.div [
                prop.className "swt:flex swt:flex-wrap swt:items-center swt:justify-between swt:gap-2"
                prop.children [
                    Html.span [
                        prop.className (
                            if saveError.IsSome then
                                "swt:text-xs swt:text-error"
                            else
                                "swt:text-xs swt:opacity-70"
                        )
                        prop.text saveStatusText
                    ]
                    Html.button [
                        prop.type'.button
                        prop.className "swt:btn swt:btn-sm swt:btn-primary"
                        prop.disabled (isSaving || selectedPath.IsNone || not hasUnsavedChanges)
                        prop.text (if isSaving then "Saving..." else "Save")
                        prop.onClick (fun _ -> saveMarkdown ())
                    ]
                ]
            ]
            Html.div [
                prop.className "swt:flex-1"
                prop.children [
                    TextInputWithMarkdown.TextInputWithMarkdown(
                        markdown,
                        setMarkdown,
                        placeholder = "Write markdown...",
                        mode = PreviewMode.Live,
                        height = 560
                    )
                ]
            ]
        ]
    ]
