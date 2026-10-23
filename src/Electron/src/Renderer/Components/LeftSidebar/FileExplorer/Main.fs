namespace Renderer.Components.LeftSidebar.FileExplorer

open Fable.Core
open Feliz
open Swate.Components
open Swate.Components.Composite.Actionbar.Types
open Swate.Components.Primitive.ErrorModal.Context
open Swate.Components.Primitive.ErrorModal.Types

[<Erase; Mangle(false)>]
type Main =

    [<ReactComponent>]
    static member private NoArcOpenPlaceholder() =
        Html.div [
            prop.testId "left-sidebar-file-explorer-empty"
            prop.className "swt:flex swt:h-full swt:min-h-0 swt:items-center swt:justify-center swt:p-1"
            prop.children [
                Html.div [
                    prop.className
                        "swt:w-full swt:rounded-box swt:border swt:border-base-content/10 swt:bg-base-100 swt:shadow-sm"
                    prop.children [
                        Html.div [
                            prop.className
                                "swt:flex swt:flex-col swt:items-center swt:gap-2 swt:border-b swt:border-base-content/10 swt:px-2 swt:py-3 swt:text-center"
                            prop.children [
                                Html.div [
                                    prop.className
                                        "swt:flex swt:size-8 swt:shrink-0 swt:items-center swt:justify-center swt:rounded-full swt:bg-base-200 swt:text-primary"
                                    prop.children [
                                        Html.i [
                                            prop.className "swt:iconify swt:fluent--folder-open-24-regular swt:size-4"
                                        ]
                                    ]
                                ]
                                Html.div [
                                    prop.className "swt:min-w-0"
                                    prop.children [
                                        Html.h3 [
                                            prop.className "swt:text-sm swt:font-semibold"
                                            prop.text "No ARC open"
                                        ]
                                        Html.p [
                                            prop.className "swt:mt-1 swt:text-xs swt:text-base-content/70"
                                            prop.text
                                                "Open or create an ARC to browse files and manage notes from this sidebar."
                                        ]
                                    ]
                                ]
                            ]
                        ]
                        Html.div [
                            prop.className "swt:px-2 swt:py-2"
                            prop.children [
                                Html.ul [
                                    prop.className "swt:space-y-1.5 swt:text-xs swt:text-base-content/80"
                                    prop.children [
                                        Html.li [
                                            prop.className "swt:flex swt:items-start swt:gap-2"
                                            prop.children [
                                                Html.i [
                                                    prop.className
                                                        "swt:iconify swt:fluent--arrow-right-24-regular swt:mt-0.5 swt:size-3 swt:text-primary"
                                                ]
                                                Html.span "Use the top toolbar to open an ARC from your machine."
                                            ]
                                        ]
                                        Html.li [
                                            prop.className "swt:flex swt:items-start swt:gap-2"
                                            prop.children [
                                                Html.i [
                                                    prop.className
                                                        "swt:iconify swt:fluent--arrow-right-24-regular swt:mt-0.5 swt:size-3 swt:text-primary"
                                                ]
                                                Html.span
                                                    "After opening, your ARC file tree and quick actions will appear here."
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member Main() =
        let pageStateCtx = Renderer.Context.PageStateContext.usePageStateCtx ()
        let appStateCtx = Renderer.Context.AppStateContext.useAppStateCtx ()
        let errorModalCtx = useErrorModalCtx ()
        let arcNameContextMenuRef = React.useElementRef ()

        let copyArcPathToClipboard path =
            promise {
                try
                    do! navigator.clipboard.writeText path
                with ex ->
                    errorModalCtx.enqueue (
                        ErrorModalRequest.create ($"Failed to copy path: {ex.Message}", title = "Copy path failed")
                    )
            }
            |> Promise.start

        match appStateCtx with
        | Some path ->
            Html.div [
                prop.testId "left-sidebar-file-explorer"
                prop.className "swt:flex swt:h-full swt:min-h-0 swt:flex-col swt:gap-2"
                prop.children [
                    Html.div [
                        prop.testId "left-sidebar-file-explorer-toolbar"
                        prop.className "swt:flex swt:shrink-0 swt:justify-center swt:bg-base-100"
                        prop.children [
                            Swate.Components.Composite.Actionbar.Actionbar.Main(
                                [|
                                    //ButtonInfo.create (
                                    //    "swt:fluent--book-open-24-regular swt:size-5",
                                    //    "Lab book view",
                                    //    fun _ -> pageStateCtx.setState (Some Renderer.Types.PageState.LandingDraftPage)
                                    //)
                                    ButtonInfo.create (
                                        "swt:fluent--document-add-24-regular swt:size-5",
                                        "Create Note",
                                        fun _ -> pageStateCtx.setState (Some Renderer.Types.PageState.NotesDraftPage)
                                    )
                                    ButtonInfo.create (
                                        "swt:fluent--document-search-24-regular swt:size-5",
                                        "Note Search",
                                        fun _ -> pageStateCtx.setState (Some Renderer.Types.PageState.NotesSearchPage)
                                    )
                                // Placeholder for provenance grouping page, which is not ready yet. We want to have this in the action bar for easy access since it's a key feature, but we don't want it to be visible until it's ready, hence the commented out button for now. Once the page is ready, we can uncomment this and remove the fallback in the main content area.
                                // ButtonInfo.create (
                                //     "swt:fluent--text-paragraph-24-regular swt:size-5",
                                //     "Provenance Grouping",
                                //     fun _ ->
                                //         pageStateCtx.setState (Some Renderer.Types.PageState.ProvenanceGroupingPage)
                                // )
                                |],
                                3
                            )
                        ]
                    ]
                    Html.div [
                        prop.ref arcNameContextMenuRef
                        prop.children [
                            Swate.Components.Composite.ArcVaultActions.ArcVaultActions.ArcVaultActions(
                                path,
                                copyArcPathToClipboard,
                                fun () ->
                                    promise {
                                        match! Api.ipcArcVaultApi.openArcFolderInFileExplorer () with
                                        | Ok() -> ()
                                        | Error exn ->
                                            errorModalCtx.enqueue (
                                                ErrorModalRequest.create (
                                                    $"Failed to open folder: {exn.Message}",
                                                    title = "Open folder failed"
                                                )
                                            )
                                    }
                                    |> Promise.start
                            )
                        ]
                    ]
                    Html.div [
                        prop.testId "left-sidebar-file-explorer-tree"
                        prop.className "swt:flex-1 swt:min-h-0 swt:overflow-x-auto swt:[scrollbar-gutter:stable]"
                        prop.children [
                            FileTree.FileTree(rootContextMenuRef = arcNameContextMenuRef)
                        ]
                    ]
                ]
            ]
        | None -> Main.NoArcOpenPlaceholder()
