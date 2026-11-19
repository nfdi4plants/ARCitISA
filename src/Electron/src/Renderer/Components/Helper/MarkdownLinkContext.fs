module Renderer.Components.Helper.MarkdownLinkContext

open Fable.Core
open Feliz
open Swate.Components.Composite.MarkdownText.Links.Types
open Swate.Electron.Shared
open Swate.Electron.Shared.DTOs.MarkdownResolverDto
open Renderer.Types

/// The text shown in place of an image the main process refuses to serve.
let assetRefusalMessage (arcPath: string) (refusal: AssetRefusal) : string =
    match refusal with
    | AssetRefusal.ArcNotOpen -> "This image link is from an ARC that is no longer open."
    | AssetRefusal.OutsideArc -> "This image lies outside the ARC."
    | AssetRefusal.NotFound -> $"Image not found: {arcPath}"
    | AssetRefusal.LfsPointer -> "This image is stored in Git LFS and is not downloaded."
    | AssetRefusal.TooLarge(_, limit) -> $"This image is larger than {int (limit / 1048576.0)} MB."
    | AssetRefusal.UnsupportedType "" -> "Images without a file extension cannot be shown."
    | AssetRefusal.UnsupportedType extension -> $"{extension} images cannot be shown."
    | AssetRefusal.Unreadable message -> $"The image could not be read: {message}"

/// What a click on a link does once the main process has checked its target.
type LinkAction =
    | OpenFile of path: string
    | RevealFolder of path: string
    | Fail of message: string

let linkActionOf (arcPath: string) (check: PathCheck) : LinkAction =
    match check with
    | PathCheck.File -> OpenFile arcPath
    | PathCheck.CaseDiffers(onDisk, false) -> OpenFile onDisk
    | PathCheck.Directory when arcPath = "" -> Fail "This link points to the ARC root folder."
    | PathCheck.Directory -> RevealFolder arcPath
    | PathCheck.CaseDiffers(onDisk, true) -> RevealFolder onDisk
    | PathCheck.Missing -> Fail $"The link target does not exist: {arcPath}"
    | PathCheck.OutsideArc -> Fail $"The link target lies outside the ARC: {arcPath}"
    | PathCheck.LfsPointer -> Fail "This file is stored in Git LFS and is not downloaded."
    | PathCheck.Unreadable message -> Fail $"The link target could not be read: {message}"

let targetStateOf (check: PathCheck) : LinkTargetState =
    match check with
    | PathCheck.File
    | PathCheck.LfsPointer
    | PathCheck.Directory -> LinkTargetState.Exists
    | PathCheck.CaseDiffers(onDisk, _) -> LinkTargetState.CaseDiffers onDisk
    | PathCheck.Missing -> LinkTargetState.Missing
    | PathCheck.OutsideArc -> LinkTargetState.OutsideArc
    | PathCheck.Unreadable message -> LinkTargetState.Unreadable message

/// What a link click needs from the app. The hook fills it from the contexts.
type LinkOpenDeps = {
    CheckPath: string -> JS.Promise<Result<PathCheck[], exn>>
    /// Opens a file target, after the leave guard.
    Navigation: FileNavigation.FileNavigationDeps
    /// Shows the folder in the File Explorer and focuses its row. The page and the selection stay.
    RevealFolder: string -> unit
    OnError: string -> unit
}

/// Checks the target of a link and acts on it. A file opens like a click in the File Explorer, so a file that is not
/// the open document asks the leave guard first.
let openArcPathWith (deps: LinkOpenDeps) (path: string) : JS.Promise<unit> = promise {
    let! result = deps.CheckPath path |> Promise.catch (fun e -> Error e)

    match result with
    | Ok [| check |] ->
        match linkActionOf path check with
        | OpenFile p ->
            let! _ = FileNavigation.openFileWith deps.Navigation p false
            ()
        | RevealFolder p -> deps.RevealFolder p
        | Fail message -> deps.OnError message
    | Ok _ -> deps.OnError "The link target could not be checked."
    | Error e -> deps.OnError e.Message
}

/// Checks ARC paths for the link warnings of the editor. A failed call rejects so the lint shows a warning.
let checkTargets (paths: string[]) : JS.Promise<LinkTargetState[]> = promise {
    let! result = Api.ipcMarkdownResolverApi.checkPaths paths

    match result with
    | Ok checks -> return checks |> Array.map targetStateOf
    | Error e -> return! Promise.reject e
}

/// The link context of the open ARC for a Markdown document in baseFolder. It is None while no ARC is open.
[<Hook>]
let useMarkdownLinkContext (baseFolder: string) (onLinkError: string -> unit) : IMarkdownLinkContext option =
    let arcRoot = Renderer.Context.AppStateContext.useAppStateCtx ()
    let navigation = FileNavigation.useFileNavigationDeps ()

    let expansion = Renderer.Context.FileTreeExpansionContext.useFileTreeExpansionCtx ()

    let sidebarNavigation =
        Renderer.Context.LeftSidebarNavigationContext.useLeftSidebarNavigationCtx ()

    let assetBase, setAssetBase = React.useState (None: string option)
    let hasArc = arcRoot.IsSome

    // The callbacks of the context read these refs, so the context changes only with the memo deps.
    let expansionRef = React.useRef expansion
    let sidebarNavigationRef = React.useRef sidebarNavigation
    let onLinkErrorRef = React.useRef onLinkError
    expansionRef.current <- expansion
    sidebarNavigationRef.current <- sidebarNavigation
    onLinkErrorRef.current <- onLinkError

    // The latest base URL. A context built with an older one belongs to an ARC that is no longer open.
    let assetBaseRef = React.useRef assetBase
    assetBaseRef.current <- assetBase

    React.useEffect (
        (fun () ->
            let cancelled = ref false

            if hasArc then
                Api.ipcMarkdownResolverApi.getAssetBaseUrl ()
                |> Promise.catch (fun e -> Error e)
                |> Promise.iter (fun result ->
                    if not cancelled.Value then
                        match result with
                        | Ok url -> setAssetBase (Some url)
                        | Error e ->
                            setAssetBase None
                            onLinkErrorRef.current $"Images of the ARC cannot be shown: {e.Message}"
                )
            else
                setAssetBase None

            { new System.IDisposable with
                member _.Dispose() = cancelled.Value <- true
            }
        ),
        [| box arcRoot |]
    )

    let openArcPath (path: string) =
        let deps = {
            CheckPath = fun p -> Api.ipcMarkdownResolverApi.checkPaths [| p |]
            Navigation = navigation
            // The folder opens in the File Explorer and its row takes the focus, so the click shows where it is.
            RevealFolder =
                fun p ->
                    sidebarNavigationRef.current.showPage LeftSidebarPage.FileExplorer
                    expansionRef.current.revealAndFocus p
            OnError = fun message -> onLinkErrorRef.current message
        }

        openArcPathWith deps path |> Promise.start

    React.useMemo (
        (fun () ->
            if hasArc then
                Some
                    { new IMarkdownLinkContext with
                        member _.baseFolder = baseFolder

                        member _.assetUrl path =
                            assetBase |> Option.map (fun b -> MarkdownAssetUrl.build b path)

                        member _.describeAsset path = promise {
                            if assetBaseRef.current <> assetBase then
                                return Some(assetRefusalMessage path AssetRefusal.ArcNotOpen)
                            else
                                let! result = Api.ipcMarkdownResolverApi.describeAsset path

                                match result with
                                | Ok(Some refusal) -> return Some(assetRefusalMessage path refusal)
                                | Ok None -> return None
                                | Error e -> return Some e.Message
                        }

                        member _.openArcPath path = openArcPath path

                        member _.openExternal url =
                            Browser.Dom.window.``open`` (url, "_blank") |> ignore
                    }
            else
                None
        ),
        [| box baseFolder; box assetBase; box hasArc |]
    )
