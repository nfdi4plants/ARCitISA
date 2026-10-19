module Renderer.Components.MainContent.Main

open Feliz
open Renderer.Types
open Swate.Electron.Shared
open Renderer.Components.MainContent.ArcFilePreviewTarget
open Renderer.Components.MainContent.DataHubBrowserTarget
open Renderer.Components.MainContent.EmptySelectionTarget
open Renderer.Components.MainContent.ErrorViewTarget
open Renderer.Components.MainContent.GitDiffTarget
open Renderer.Components.MainContent.GitFileChoiceConflictTarget
open Renderer.Components.MainContent.GitMergeConflictTarget
open Renderer.Components.MainContent.GitUnsupportedTarget
open Renderer.Components.MainContent.NotesDraftTarget
open Renderer.Components.MainContent.NotesSearchTarget
open Renderer.Components.MainContent.ProvenanceGroupingTarget
open Renderer.Components.MainContent.TextPreviewTarget
open Renderer.Components.MainContent.UnknownPreviewTarget
open Renderer.Components.MainContent.SettingsPageTarget
open Renderer.Components.MainContent.ValidationPackageBrowserTarget

module private MainHelper =

    open Fable.Core

    let loadTemplates () =
        promise {
            // let! json =
            //     Swate.Components.Api.SwateApi.SwateTemplateApi.getTemplates ()
            //     |> Async.StartAsPromise
            // return Ok(ARCtrl.Json.Templates.fromJsonString json)

            let! result = Api.ipcTemplateApi.getTemplates ()

            return
                result
                |> Result.map ARCtrl.Json.Templates.fromJsonString
                |> Result.mapError (fun error -> error.Message)
        }
        |> Promise.catch (fun error ->
            // Handle error, e.g., log it or show a notification
            Error(sprintf "Error loading templates: %s" error.Message)
        )


module private LazyComponents =

    open Swate.Components.Primitive

    [<ReactComponent>]
    let FullPageLoadingSpinner (text: string) =
        Html.div [
            prop.className "swt:flex-1 swt:flex swt:min-w-0 swt:min-h-0 swt:grow swt:justify-center swt:items-center"
            prop.children [
                Swate.Components.Primitive.LoadingSpinner.LoadingSpinner.LoadingSpinner(
                    size = DaisyuiSize.XL,
                    color = DaisyuiColors.Primary,
                    text = text
                )
            ]
        ]

    [<ReactLazyComponent>]
    let LazySettingPage () =
        Renderer.Components.MainContent.SettingsPageTarget.SettingsPage()

    [<ReactLazyComponent>]
    let LazyChangelogTarget (version: string) =
        Renderer.Components.MainContent.ChangelogTarget.ChangelogTarget(version)

    [<ReactLazyComponent>]
    let LazyMarkdownEditorTarget (content: string) =
        Renderer.Components.MainContent.MarkdownEditorTargetView.MarkdownEditorTarget(content)

    [<ReactLazyComponent>]
    let ProvenanceGroupingTarget () =
        Renderer.Components.MainContent.ProvenanceGroupingTarget.ProvenanceGroupingTarget()

    [<ReactLazyComponent>]
    let ProvenanceGraphTarget () =
        Renderer.Components.MainContent.ProvenanceGraphTarget.ProvenanceGraphTarget()

/// This can be further reduced by using the actual contexts instead of passing down the states and setters as props, but this is good enough for now
[<ReactMemoComponent>]
let Main (appRootPath: ArcRootPath, pageState: PageState option) =

    Swate.Components.Composite.Template.TemplateCacheProvider.TemplateCacheProvider(
        loadTemplates = MainHelper.loadTemplates,
        children =
            Html.div [
                prop.className "swt:size-full swt:min-w-0 swt:min-h-0 swt:flex swt:justify-center swt:overflow-hidden"
                prop.children [
                    match appRootPath, pageState with
                    | _, Some PageState.DataHubBrowser -> DataHubBrowserTarget()
                    | Some _, Some PageState.ValidationPackageBrowser -> ValidationPackageBrowserTarget()
                    | _, Some(PageState.Changelog(version, requestId)) ->
                        Html.div [
                            prop.key requestId
                            prop.className "swt:flex swt:grow swt:min-w-0 swt:min-h-0"
                            prop.children [
                                React.Suspense(
                                    [ LazyComponents.LazyChangelogTarget(version) ],
                                    fallback = LazyComponents.FullPageLoadingSpinner("Loading changelog...")
                                )
                            ]
                        ]
                    | _, Some PageState.SettingsPage ->
                        React.Suspense(
                            [ LazyComponents.LazySettingPage() ],
                            fallback = LazyComponents.FullPageLoadingSpinner("Loading settings...")
                        )
                    | None, _ ->
                        Html.div [
                            prop.className
                                "swt:flex-1 swt:min-w-0 swt:min-h-0 swt:flex swt:justify-center swt:items-center"
                            prop.children [ Renderer.Components.InitState.InitState() ]
                        ]
                    | Some _, Some(PageState.ArcFilePage(arcFile, requestedView)) ->
                        ArcFilePreviewTarget(arcFile, requestedView)
                    | Some _, Some(PageState.MarkdownPage content) ->
                        React.Suspense(
                            [ LazyComponents.LazyMarkdownEditorTarget(content) ],
                            fallback = LazyComponents.FullPageLoadingSpinner("Loading markdown editor...")
                        )
                    | Some _, Some(PageState.PDFPage content) ->
                        React.Suspense(
                            [ PDFViewer.DisplayPDF.Main(content) ],
                            fallback = LazyComponents.FullPageLoadingSpinner("Loading pdf...")
                        )
                    | Some _, Some(PageState.PNGImage content) -> ImageViewer.Main("image/png", content)
                    | Some _, Some(PageState.JPGImage content) -> ImageViewer.Main("image/jpeg", content)
                    | Some _, Some(PageState.SVGImage content) -> ImageViewer.Main("image/svg+xml", content)
                    | Some _, Some(PageState.TextPage content) -> TextPreviewTarget content
                    | Some _, Some PageState.UnknownPage -> UnknownPreviewTarget()
                    | Some _, Some(PageState.ErrorPage errMsg) -> ErrorViewTarget errMsg
                    // | Some _, Some PageState.LandingDraftPage -> LandingDraftTarget()
                    | Some _, Some PageState.NotesDraftPage -> NotesDraftTarget()
                    | Some _, Some PageState.NotesSearchPage -> NotesSearchTarget()
                    | Some _, Some PageState.ProvenanceGroupingPage ->
                        React.Suspense(
                            [ LazyComponents.ProvenanceGroupingTarget() ],
                            fallback = LazyComponents.FullPageLoadingSpinner("Loading Table Editor...")
                        )
                    | Some _, Some(PageState.ProvenanceGraphPage requestId) ->
                        // The area keys the content by the request id and shows what failed when the lazy import
                        // or the target throws. It sits in its own module, so Main imports the target only lazily.
                        ProvenanceGraphArea.ProvenanceGraphArea
                            requestId
                            (React.Suspense(
                                [ LazyComponents.ProvenanceGraphTarget() ],
                                fallback = LazyComponents.FullPageLoadingSpinner("Loading provenance graph...")
                            ))
                    | Some _, Some(PageState.GitDiffPage diffPage) -> GitDiffTarget.Main diffPage
                    | Some _, Some(PageState.GitMergeConflictPage mergeData) -> GitMergeConflictTarget.Main mergeData
                    | Some _, Some(PageState.GitFileChoiceConflictPage choiceData) ->
                        GitFileChoiceConflictTarget.Main choiceData
                    | Some _, Some(PageState.GitUnsupportedPage unsupportedPage) ->
                        GitUnsupportedTarget.Main unsupportedPage
                    | Some _, None -> EmptySelectionTarget()
                ]
            ]
    )
