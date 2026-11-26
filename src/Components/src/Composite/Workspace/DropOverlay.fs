namespace Arcitisa.Components.Composite.Workspace

open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Arcitisa.Components
open Arcitisa.Components.JsBindings
open Arcitisa.Components.Composite.Workspace.Types
open Arcitisa.Components.Composite.Workspace.Context
open Arcitisa.Components.Composite.Workspace.Helper.DndId
open Arcitisa.Components.Composite.Workspace.Helper.DropTarget

[<Erase; Mangle(false)>]
type DropOverlay =

    [<ReactComponent>]
    static member DropOverlay(workspaceRef: IRefValue<HTMLElement option>, sortableActiveRef: IRefValue<bool>) =
        let dispatchCtx = useWorkspaceDispatchCtx ()

        let isDragging, setIsDragging = React.useState false
        let isDraggingRef = React.useRef false
        let dragInfoRef = React.useRef (None: (string * string) option)

        let dropTarget, setDropTarget = React.useState (None: DropTarget option)
        let dropTargetRef = React.useRef (None: DropTarget option)

        let targetRect, setTargetRect =
            React.useState (
                None
                : {|
                    left: float
                    top: float
                    width: float
                    height: float
                |} option
            )

        let targetRectRef =
            React.useRef (
                None
                : {|
                    left: float
                    top: float
                    width: float
                    height: float
                |} option
            )

        let findPaneResult, setFindPaneResult = React.useState "unknown"
        let resolveAtCalled, setResolveAtCalled = React.useState false

        let computeTargetRect
            (workspaceEl: HTMLElement)
            (target: DropTarget)
            : {|
                  left: float
                  top: float
                  width: float
                  height: float
              |} option
            =

            let wr = workspaceEl.getBoundingClientRect ()

            match target with
            | TabBarDrop paneId ->
                match workspaceEl.querySelector ($"[data-workspace-tabbar=\"{paneId}\"]") with
                | :? HTMLElement as tabBarEl ->
                    let r = tabBarEl.getBoundingClientRect ()

                    Some {|
                        left = r.left - wr.left
                        top = r.top - wr.top
                        width = r.width
                        height = r.height
                    |}
                | _ -> None

            | EdgeDrop(paneId, dir) ->
                match workspaceEl.querySelector ($"[data-workspace-pane=\"{paneId}\"]") with
                | :? HTMLElement as paneEl ->
                    let r = paneEl.getBoundingClientRect ()

                    let tabBarHeight =
                        match workspaceEl.querySelector ($"[data-workspace-tabbar=\"{paneId}\"]") with
                        | :? HTMLElement as tb -> tb.getBoundingClientRect().height
                        | _ -> 0.0

                    let contentTop = r.top + tabBarHeight
                    let contentHeight = max 0.0 (r.bottom - contentTop)
                    let relativeLeft = r.left - wr.left
                    let relativeContentTop = contentTop - wr.top

                    match dir with
                    | EdgeDirection.Top ->
                        Some {|
                            left = relativeLeft
                            top = relativeContentTop
                            width = r.width
                            height = contentHeight * 0.25
                        |}
                    | EdgeDirection.Bottom ->
                        Some {|
                            left = relativeLeft
                            top = relativeContentTop + contentHeight * 0.75
                            width = r.width
                            height = contentHeight * 0.25
                        |}
                    | EdgeDirection.Left ->
                        Some {|
                            left = relativeLeft
                            top = relativeContentTop
                            width = r.width * 0.25
                            height = contentHeight
                        |}
                    | EdgeDirection.Right ->
                        Some {|
                            left = relativeLeft + r.width * 0.75
                            top = relativeContentTop
                            width = r.width * 0.25
                            height = contentHeight
                        |}
                | _ -> None

        let resolveAt (x: float) (y: float) =
            setResolveAtCalled true

            match workspaceRef.current, dragInfoRef.current with
            | Some workspaceEl, Some(sourcePaneId, _) ->
                match findPaneElement x y workspaceEl with
                | Some foundEl ->
                    setFindPaneResult "some"
                    let isOverTabBar = not (isNull (foundEl.getAttribute "data-workspace-tabbar"))
                    let target = resolveDropTarget foundEl x y workspaceEl sourcePaneId
                    let rect = target |> Option.bind (computeTargetRect workspaceEl)

                    sortableActiveRef.current <- isOverTabBar

                    setDropTarget target
                    dropTargetRef.current <- target
                    setTargetRect rect
                    targetRectRef.current <- rect
                | None ->
                    setFindPaneResult "none"
                    sortableActiveRef.current <- false

                    setDropTarget None
                    dropTargetRef.current <- None
                    setTargetRect None
                    targetRectRef.current <- None
            | _ -> ()

        DndKit.useDndMonitor (
            {|
                onDragStart =
                    fun (event: DndKit.IDndKitEvent) ->
                        if not (isNull event.active) then
                            let activeId = string event.active.id

                            match DndId.read activeId with
                            | Some(Tab(sourcePaneId, tabId)) ->
                                dragInfoRef.current <- Some(sourcePaneId, tabId)
                                setFindPaneResult "unknown"
                                setResolveAtCalled false
                                setIsDragging true
                                isDraggingRef.current <- true
                            | _ -> ()

                onDragEnd =
                    fun (_: DndKit.IDndKitEvent) ->
                        match dragInfoRef.current, dropTargetRef.current with
                        | Some(_, tabId), Some(TabBarDrop targetPaneId) ->
                            dispatchCtx.dispatch (MoveTab(TabId tabId, PaneId(System.Guid.Parse targetPaneId)))
                        | Some(_, tabId), Some(EdgeDrop(targetPaneId, dir)) ->
                            dispatchCtx.dispatch (
                                SplitPaneByTabMove(TabId tabId, PaneId(System.Guid.Parse targetPaneId), dir)
                            )
                        | _ -> ()

                        isDraggingRef.current <- false
                        dragInfoRef.current <- None
                        setFindPaneResult "unknown"
                        setResolveAtCalled false
                        setIsDragging false
                        setDropTarget None
                        setTargetRect None
                        dropTargetRef.current <- None
                        targetRectRef.current <- None

                onDragCancel =
                    fun (_: DndKit.IDndKitEvent) ->
                        isDraggingRef.current <- false
                        dragInfoRef.current <- None
                        setFindPaneResult "unknown"
                        setResolveAtCalled false
                        setIsDragging false
                        setDropTarget None
                        setTargetRect None
                        dropTargetRef.current <- None
                        targetRectRef.current <- None
            |}
        )

        React.useEffect (
            (fun () ->
                if not isDragging then
                    FsReact.createDisposable (fun () -> ())
                else
                    let mutable handlerRef: (PointerEvent -> unit) option = None

                    let handler (e: PointerEvent) =
                        if isDraggingRef.current then
                            resolveAt e.clientX e.clientY

                    handlerRef <- Some handler
                    Browser.Dom.document.addEventListener ("pointermove", unbox handler)

                    FsReact.createDisposable (fun () ->
                        match handlerRef with
                        | Some h -> Browser.Dom.document.removeEventListener ("pointermove", unbox h)
                        | None -> ()
                    )
            ),
            [| box isDragging |]
        )

        React.Fragment [
            if isDragging then
                Html.div [
                    prop.testId "drag-debug"
                    prop.custom ("data-is-dragging", "true")
                    prop.custom (
                        "data-drop-target-state",
                        match dropTarget with
                        | Some(TabBarDrop _) -> "tabbar"
                        | Some(EdgeDrop(_, d)) ->
                            match d with
                            | EdgeDirection.Top -> "edge-top"
                            | EdgeDirection.Bottom -> "edge-bottom"
                            | EdgeDirection.Left -> "edge-left"
                            | EdgeDirection.Right -> "edge-right"
                        | None -> "none"
                    )
                    prop.custom (
                        "data-target-rect-state",
                        match targetRect with
                        | Some _ -> "some"
                        | None -> "none"
                    )
                    prop.custom ("data-find-pane-result", findPaneResult)
                    prop.custom ("data-resolve-at-called", if resolveAtCalled then "true" else "false")
                    prop.style [ style.custom ("display", "none") ]
                ]
            match dropTarget, targetRect with
            | Some target, Some rect ->
                let transitionStyle =
                    "top 100ms ease-out, left 100ms ease-out, width 100ms ease-out, height 100ms ease-out"

                let dirStr =
                    match target with
                    | TabBarDrop _ -> "tabbar"
                    | EdgeDrop(_, d) ->
                        match d with
                        | EdgeDirection.Top -> "edge-top"
                        | EdgeDirection.Bottom -> "edge-bottom"
                        | EdgeDirection.Left -> "edge-left"
                        | EdgeDirection.Right -> "edge-right"

                let paneStr =
                    match target with
                    | TabBarDrop p -> p
                    | EdgeDrop(p, _) -> p

                match target with
                | TabBarDrop _ ->
                    Html.div [
                        prop.testId "drop-overlay"
                        prop.custom ("data-overlay-target", dirStr)
                        prop.custom ("data-overlay-pane", paneStr)
                        prop.className "swt:absolute swt:bg-primary/10 swt:pointer-events-none swt:z-30 swt:rounded"
                        prop.style [
                            style.left (length.px rect.left)
                            style.top (length.px rect.top)
                            style.width (length.px rect.width)
                            style.height (length.px rect.height)
                            style.custom ("transition", transitionStyle)
                        ]
                    ]
                | EdgeDrop(_, dir) ->
                    Html.div [
                        prop.testId "drop-overlay"
                        prop.custom ("data-overlay-target", dirStr)
                        prop.custom ("data-overlay-pane", paneStr)
                        prop.className "swt:absolute swt:bg-primary/20 swt:pointer-events-none swt:z-30 swt:rounded"
                        prop.style [
                            style.left (length.px rect.left)
                            style.top (length.px rect.top)
                            style.width (length.px rect.width)
                            style.height (length.px rect.height)
                            match dir with
                            | EdgeDirection.Top -> style.borderBottomWidth (length.px 4)
                            | EdgeDirection.Bottom -> style.borderTopWidth (length.px 4)
                            | EdgeDirection.Left -> style.borderRightWidth (length.px 4)
                            | EdgeDirection.Right -> style.borderLeftWidth (length.px 4)
                            style.custom ("borderColor", "oklch(var(--p))")
                            style.custom ("transition", transitionStyle)
                        ]
                    ]
            | _ -> Html.none
        ]
