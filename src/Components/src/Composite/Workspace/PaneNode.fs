namespace Arcitisa.Components.Composite.Workspace

open Fable.Core
open Browser.Types
open Feliz
open Arcitisa.Components
open Arcitisa.Components.Composite.Workspace.Types
open Arcitisa.Components.Composite.Workspace.Context
open Arcitisa.Components.Composite.Widgets.Types
open Arcitisa.Components.Primitive.SplitResizer

[<Erase; Mangle(false)>]
type PaneNode =

    [<ReactMemoComponent(AreEqualFn.FsEqualsButFunctions)>]
    static member LeafNode(paneId: PaneId, ?key: string) =
        let paneStateCtx = useWorkspacePaneStateCtx ()
        let paneIdKey = paneId.Value.ToString("N")

        let pane =
            paneStateCtx.panesMap
            |> Map.tryFind paneId
            |> Option.defaultValue {
                Id = paneId
                Tabs = []
                FocusedTab = None
            }

        let paneCtxValue: PaneContext<_> = {
            paneId = paneId
            tabs = pane.Tabs |> Array.ofList
            focusedTab = pane.FocusedTab
            isFocusedPane = paneStateCtx.focusedPane = paneId
        }

        PaneCtx.Provider(
            box paneCtxValue,
            Html.div [
                prop.key (defaultArg key paneIdKey)
                prop.className
                    "swt:flex swt:flex-col swt:min-w-0 swt:min-h-0 swt:flex-1 swt:overflow-hidden swt:border-l swt:border-t swt:border-base-content/20"
                prop.custom ("data-workspace-pane", paneIdKey)
                if paneStateCtx.debug then
                    prop.testId $"workspace-pane-{paneIdKey}"
                prop.children [ TabBar.TabBar(paneId); ContentArea.ContentArea(paneId) ]
            ]
        )

    [<ReactComponent>]
    static member SplitNodeResizer(splitId, splitContainerRef: IRefValue<option<HTMLElement>>, dir: SplitDirection) =

        let dispatchCtx = useWorkspaceDispatchCtx ()

        SplitResizer.SplitResizer(
            splitContainerRef,
            (dir = SplitDirection.Vertical),
            (fun ratio -> dispatchCtx.dispatch (SetSplitRatio(splitId, ratio)))
        )

    [<ReactMemoComponent(AreEqualFn.FsEqualsButFunctions)>]
    static member SplitNode(splitId: SplitId, dir: SplitDirection, ratio: float, l1: Layout, l2: Layout, ?key: string) =
        let paneStateCtx = useWorkspacePaneStateCtx ()

        let splitContainerRef = React.useElementRef ()

        let flexDir =
            match dir with
            | SplitDirection.Horizontal -> "swt:flex-row"
            | SplitDirection.Vertical -> "swt:flex-col"

        let size1 = ratio * 100.0
        let size2 = 100.0 - size1

        let splitIdKey = splitId.Value.ToString("N")

        Html.div [
            prop.key (defaultArg key splitIdKey)
            prop.ref splitContainerRef
            prop.className $"swt:flex {flexDir} swt:min-w-0 swt:min-h-0 swt:flex-1 swt:overflow-hidden"
            if paneStateCtx.debug then
                prop.testId $"workspace-split-{splitIdKey}"
            prop.children [
                Html.div [
                    prop.className "swt:flex swt:flex-col swt:min-w-0 swt:min-h-0 swt:overflow-hidden"
                    prop.style [
                        match dir with
                        | SplitDirection.Horizontal -> style.width (length.perc size1)
                        | SplitDirection.Vertical -> style.height (length.perc size1)
                    ]
                    prop.children [ PaneNode.PaneNode l1 ]
                ]
                Html.div [
                    prop.className "swt:flex swt:min-w-0 swt:min-h-0 swt:relative"
                    prop.style [
                        match dir with
                        | SplitDirection.Horizontal -> style.width (length.perc size2)
                        | SplitDirection.Vertical -> style.height (length.perc size2)
                    ]
                    prop.children [
                        PaneNode.SplitNodeResizer(splitId, splitContainerRef, dir)
                        Html.div [
                            prop.className
                                "swt:overflow-hidden swt:flex swt:flex-col swt:min-w-0 swt:min-h-0 swt:flex-1 swt:grow"
                            prop.children [ PaneNode.PaneNode l2 ]
                        ]
                    ]
                ]
            ]
        ]

    [<ReactMemoComponent(AreEqualFn.FsEqualsButFunctions)>]
    static member PaneNode(layout: Layout, ?key: string) =

        match layout with
        | Layout.Single leaf -> PaneNode.LeafNode(leaf, ?key = key)

        | Layout.Split(splitId, dir, ratio, l1, l2) -> PaneNode.SplitNode(splitId, dir, ratio, l1, l2, ?key = key)
