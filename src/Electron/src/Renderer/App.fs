module Renderer.App

open Elmish
open Feliz
open Feliz.UseElmish
open Renderer.Components
open Renderer.Components.Helper.ArcVaultHelper
open Renderer.Types
open Swate.Components
open Swate.Components.Composite.Layout
open Swate.Components.Primitive.BaseModal
open Swate.Components.Primitive.ErrorModal
open Swate.Components.Primitive.ErrorModal.Context
open Swate.Electron.Shared
open Swate.Electron.Shared.IPCTypes.MainToRendererIpc

type private Model = {
    // Current ARC root shared with renderer contexts.
    ArcRootPath: ArcRootPath
    // Whether the last pathChange renamed the open ARC folder.
    ArcRootIsRename: bool
    // Bumped for main-process live updates so stale snapshot replies are ignored.
    ArcRootPathLiveUpdateVersion: int
    // Bumped for each snapshot request so older async replies cannot win.
    ArcRootPathRequestVersion: int
    PageState: PageState option
    LeftSidebarTarget: LeftSidebarPage
} with

    static member Empty = {
        ArcRootPath = None
        ArcRootIsRename = false
        ArcRootPathLiveUpdateVersion = 0
        ArcRootPathRequestVersion = 0
        PageState = None
        LeftSidebarTarget = LeftSidebarPage.FileExplorer
    }

type private Msg =
    | ArcRootPathSnapshotRequested
    | ArcRootPathSnapshotLoaded of requestVersion: int * liveUpdateVersionAtStart: int * Result<ArcRootPath, exn>
    | ArcRootPathChanged of ArcRootPath * isRename: bool
    | PageStateChanged of PageState option
    | PageStateUpdated of (PageState option -> PageState option)
    | SetLeftSidebarTarget of LeftSidebarPage

let private init () : Model * Cmd<Msg> = Model.Empty, Cmd.none

let private msgName =
    function
    | ArcRootPathSnapshotRequested -> "ArcRootPathSnapshotRequested"
    | ArcRootPathSnapshotLoaded _ -> "ArcRootPathSnapshotLoaded"
    | ArcRootPathChanged _ -> "ArcRootPathChanged"
    | PageStateChanged _ -> "PageStateChanged"
    | PageStateUpdated _ -> "PageStateUpdated"
    | SetLeftSidebarTarget _ -> "SetLeftSidebarTarget"

let private traceUpdateMsg (msg: Msg) =
    console.log ($"[Renderer.App Elmish] {msgName msg}")

let private resetForClosedArc (model: Model) = {
    Model.Empty with
        ArcRootPathLiveUpdateVersion = model.ArcRootPathLiveUpdateVersion
        ArcRootPathRequestVersion = model.ArcRootPathRequestVersion
}

let private applyArcRootPath (arcRootPath: ArcRootPath) (model: Model) =
    match arcRootPath with
    | Some _ -> { model with ArcRootPath = arcRootPath }
    | None when model.ArcRootPath.IsNone -> model
    | None -> resetForClosedArc model

let private createGetOpenPathCmd requestVersion liveUpdateVersionAtStart =
    Cmd.OfPromise.either
        Api.ipcArcVaultApi.getOpenPath
        ()
        (fun arcRootPath -> ArcRootPathSnapshotLoaded(requestVersion, liveUpdateVersionAtStart, Ok arcRootPath))
        (fun error -> ArcRootPathSnapshotLoaded(requestVersion, liveUpdateVersionAtStart, Error error))

let private update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    traceUpdateMsg msg

    match msg with
    | ArcRootPathSnapshotRequested ->
        let requestVersion = model.ArcRootPathRequestVersion + 1

        {
            model with
                ArcRootPathRequestVersion = requestVersion
        },
        createGetOpenPathCmd requestVersion model.ArcRootPathLiveUpdateVersion
    | ArcRootPathSnapshotLoaded(requestVersion, liveUpdateVersionAtStart, Ok arcRootPath) when
        requestVersion = model.ArcRootPathRequestVersion
        && liveUpdateVersionAtStart = model.ArcRootPathLiveUpdateVersion
        ->
        { model with ArcRootIsRename = false } |> applyArcRootPath arcRootPath, Cmd.none
    | ArcRootPathSnapshotLoaded(requestVersion, _, _) when requestVersion = model.ArcRootPathRequestVersion ->
        model, Cmd.none
    | ArcRootPathSnapshotLoaded _ -> model, Cmd.none
    | ArcRootPathChanged(appState, isRename) ->
        let model = {
            model with
                ArcRootPathLiveUpdateVersion = model.ArcRootPathLiveUpdateVersion + 1
        }

        match appState with
        | Some _ ->
            {
                model with
                    ArcRootIsRename = isRename
            }
            |> applyArcRootPath appState,
            Cmd.none
        | None -> model |> resetForClosedArc, Cmd.none
    | PageStateChanged pageStateOption ->
        {
            model with
                PageState = pageStateOption
        },
        Cmd.none
    | PageStateUpdated update ->
        {
            model with
                PageState = update model.PageState
        },
        Cmd.none
    | SetLeftSidebarTarget leftSidebarTarget ->
        {
            model with
                LeftSidebarTarget = leftSidebarTarget
        },
        Cmd.none

let private subscribe (_model: Model) : Sub<Msg> = [
    [ "appPathChange" ],
    fun dispatch ->
        let dispose =
            Renderer.IpcReceiver.subscribeProxyReceiver<IPathChangeRendererApi> {
                pathChange =
                    fun change ->
                        ArcRootPathChanged(change |> Option.map _.path, change |> Option.exists _.isRename)
                        |> dispatch
            }

        dispatch ArcRootPathSnapshotRequested

        { new System.IDisposable with
            member _.Dispose() = dispose ()
        }
]

[<ReactComponent>]
let private LeftActionButtons (leftSidebarTarget: LeftSidebarPage) setLeftSidebarTarget =
    let leftSidebarCtx =
        Swate.Components.Composite.Layout.LeftSidebarContext.useLeftSidebarCtx ()

    let pageStateCtx = Renderer.Context.PageStateContext.usePageStateCtx ()
    let appStateCtx = Renderer.Context.AppStateContext.useAppStateCtx ()
    let errorCtx = useErrorModalCtx ()
    let newArcModalIsOpen, setNewArcModalIsOpen = React.useState false

    let onArcError =
        createErrorModalCallback errorCtx.enqueue "ARC action failed" appStateCtx

    let toggleTarget target =
        if leftSidebarTarget = target then
            leftSidebarCtx.setState (not leftSidebarCtx.state)
        else
            leftSidebarCtx.setState true
            setLeftSidebarTarget target

    let validationBrowserActive =
        match pageStateCtx.state with
        | Some PageState.ValidationPackageBrowser -> true
        | _ -> false

    let toggleValidationBrowser () =
        if validationBrowserActive then
            pageStateCtx.setState None
        else
            pageStateCtx.setState (Some PageState.ValidationPackageBrowser)

    let provenanceGraphActive =
        match pageStateCtx.state with
        | Some(PageState.ProvenanceGraphPage _) -> true
        | _ -> false

    let datahubActive =
        match pageStateCtx.state with
        | Some PageState.DataHubBrowser -> true
        | _ -> false

    // Every open gets a new request id, so the page mounts new and loads its data again.
    let toggleProvenanceGraph () =
        if provenanceGraphActive then
            pageStateCtx.setState None
        else
            pageStateCtx.setState (Some(PageState.ProvenanceGraphPage(System.Guid.NewGuid().ToString())))

    let Divider =
        Html.hr [
            prop.className "swt:mx-1 swt:my-1 swt:border-base-content/20"
        ]

    React.Fragment [
        BaseModal.BaseModal(
            newArcModalIsOpen,
            setNewArcModalIsOpen,
            InitState.CreateNewArcModalContent(fun () -> setNewArcModalIsOpen false)
        )
        Layout.LayoutBtn(
            iconClassName = "swt:fluent--folder-add-24-regular",
            tooltip = "Create a new ARC",
            onClick = fun () -> setNewArcModalIsOpen true
        )
        Layout.LayoutBtn(
            iconClassName = "swt:fluent--folder-open-24-regular",
            tooltip = "Open an existing ARC",
            onClick = fun () -> openArc onArcError |> Promise.start
        )
        Layout.LayoutBtn(
            iconClassName = "swt:fluent--cloud-beaker-24-regular",
            tooltip = "Download ARC from DataHUB",
            isActive = datahubActive,
            onClick = fun () -> pageStateCtx.setState (Some PageState.DataHubBrowser)
        )
        if appStateCtx.IsSome then

            Divider

            Layout.LayoutBtn(
                iconClassName = "swt:fluent--home-24-regular",
                tooltip = "File explorer",
                isActive = (leftSidebarTarget = LeftSidebarPage.FileExplorer),
                onClick = fun () -> toggleTarget LeftSidebarPage.FileExplorer
            )

            Layout.LayoutBtn(
                iconClassName = "swt:fluent--branch-fork-24-regular",
                tooltip = "Git",
                isActive = (leftSidebarTarget = LeftSidebarPage.Git),
                onClick = fun () -> toggleTarget LeftSidebarPage.Git
            )

            Divider

            Layout.LayoutBtn(
                iconClassName = "swt:fluent--clipboard-checkmark-24-regular",
                tooltip = "Validation packages",
                isActive = validationBrowserActive,
                onClick = fun () -> toggleValidationBrowser ()
            )

            Layout.LayoutBtn(
                iconClassName = "swt:fluent--flowchart-24-regular",
                tooltip = "Provenance Graph",
                isActive = provenanceGraphActive,
                indicator = "new",
                onClick = fun () -> toggleProvenanceGraph ()
            )
    ]

[<ReactComponent>]
let Main () =
    let model, dispatch = React.useElmish (init, update, subscribe, [||])

    React.useEffect ((fun () -> Helper.ArcVaultHelper.migrateLegacyAutoCreateNotesFolder () |> Promise.start), [||])

    let setPageState (pageState: PageState option) = dispatch (PageStateChanged pageState)

    let updatePageState =
        React.useCallback (
            (fun (update: PageState option -> PageState option) -> dispatch (PageStateUpdated update)),
            [||]
        )

    let pageCtx: StateContext<PageState option> =
        React.useMemo (
            (fun _ -> {
                state = model.PageState
                setState = setPageState
            }),
            [| box model.PageState |]
        )

    let children =
        Renderer.Components.MainContent.Main.Main(model.ArcRootPath, model.PageState)

    let setLeftSidebarTarget =
        React.useCallback ((fun leftSidebarTarget -> dispatch (SetLeftSidebarTarget leftSidebarTarget)), [||])

    let isInitializedArcVault = Option.isSome model.ArcRootPath

    let currentArcScopeId =
        model.ArcRootPath
        |> Option.map Swate.Components.Shared.PathHelpers.normalizePath
        |> Option.bind (fun path ->
            if System.String.IsNullOrWhiteSpace path then
                None
            else
                Some path
        )

    let leftSidebar =
        if isInitializedArcVault then
            Renderer.Components.LeftSidebar.Main.Main(model.LeftSidebarTarget) |> Some
        else
            None

    let leftActions = LeftActionButtons model.LeftSidebarTarget setLeftSidebarTarget

    Swate.Components.Composite.ThemeSelector.ThemeProvider.ThemeProvider(
        Swate.Components.Composite.TermSearch.TermSearchConfigProvider.TIBQueryProvider(
            Context.AppStateContext.AppStateCtx.Provider(
                model.ArcRootPath,
                Renderer.Context.FileStateContext.FileStateCtxProviderWithSnapshots(
                    {
                        arcRootPath = model.ArcRootPath
                        isRename = model.ArcRootIsRename
                        changeVersion = model.ArcRootPathLiveUpdateVersion
                    },
                    (fun () -> Api.ipcArcVaultApi.getFileTreeSnapshot ()),
                    {
                        loadActiveImport = fun () -> Api.ipcArcVaultApi.getActiveFileImport ()
                        pickAbsolutePaths = fun () -> Api.ipcArcVaultApi.pickAbsolutePaths ()
                        runImport = Api.ipcArcVaultApi.tryImportExternalFiles
                        cancelImport = Api.ipcArcVaultApi.cancelImportExternalFiles
                    },
                    Renderer.Context.PageStateContext.PageStateCtx.Provider(
                        pageCtx,
                        Renderer.Context.PageStateContext.PageStateUpdateCtx.Provider(
                            updatePageState,
                            ErrorModalProvider.ErrorModalProvider(
                                Renderer.Context.AuthStateContext.Provider(
                                    Renderer.Context.LargeObjectStore.LargeObjectStoreProviderWith(
                                        model.ArcRootPath,
                                        model.ArcRootIsRename,
                                        (fun () -> Api.ipcArcVaultApi.getLargeObjectIndex ()),
                                        Renderer.Context.LfsActivityContext.LfsActivityCtxProvider(
                                            Renderer.Context.FileTreeExpansionContext.FileTreeExpansionCtxProvider(
                                                {
                                                    loadDirectory = Api.ipcArcVaultApi.loadDirectory
                                                    revalidateDirectories = Api.ipcArcVaultApi.revalidateDirectories
                                                    statFile = Api.ipcArcVaultApi.statFile
                                                },
                                                Renderer.Context.GitStateContext.GitStateCtxProvider(
                                                    Swate
                                                        .Components
                                                        .Composite
                                                        .AnnotationTable
                                                        .AnnotationTableContextProvider
                                                        .AnnotationTableContextProvider(
                                                            Layout.Main(
                                                                children =
                                                                    React.Fragment [|
                                                                        children
                                                                        CloseWindowController.CloseWindowController()
                                                                    |],
                                                                navbar = Renderer.Components.Navbar.Main(),
                                                                ?leftSidebar = leftSidebar,
                                                                leftActions = leftActions
                                                            )
                                                        )
                                                )
                                            )
                                        )
                                    )
                                ),
                                ?scopeId = currentArcScopeId
                            )
                        )
                    )
                )
            )
        )
    )
