namespace Arcitisa.Components.Composite.Workspace

open Fable.Core
open Feliz
open Arcitisa.Components
open Arcitisa.Components.JsBindings
open Arcitisa.Components.Composite.Workspace.Types
open Arcitisa.Components.Composite.Workspace.Context
open Arcitisa.Components.Composite.Workspace.Helper.DndId

[<Erase; Mangle(false)>]
type TabBar =

    [<ReactMemoComponent(AreEqualFn.FsEqualsButFunctions)>]
    static member private Tab
        (tab: Tab<_>, index: int, paneIdKey: string, isActive: bool, isFocusedPane: bool, ?key: string)
        =
        let dispatchCtx = useWorkspaceDispatchCtx ()
        let paneStateCtx = useWorkspacePaneStateCtx ()
        let sortableActiveCtx = useSortableActiveCtx ()
        let dragId = DndId.write (DndId.Tab(paneIdKey, tab.Id.Value))

        let sortable = DndKit.useSortable ({| id = dragId |})

        let style = [
            if sortableActiveCtx.isActiveRef.current then
                style.custom ("transform", DndKit.CSS.Transform.toString sortable.transform)
                style.custom ("transition", sortable.transition)
            style.cursor.grab
        ]

        let dndProps = [
            prop.ref sortable.setNodeRef
            prop.style style
            yield! prop.spread sortable.attributes
            yield! prop.spread sortable.listeners
        ]

        let tabClass = [
            "swt:h-full swt:flex swt:px-2 swt:py-0.5 swt:items-center swt:min-w-fit swt:gap-1 swt:flex-nowrap swt:select-none "
            "swt:border-r swt:border-r-base-content/50 swt:border-t-2"
            if isActive then
                "swt:bg-base-100 swt:border-primary"
            else
                "swt:border-transparent"
            // if isActive && isFocusedPane then
            //     "swt:bg-primary/70"
            if sortable.isDragging then
                "swt:opacity-30"
        ]

        Html.div [
            match key with
            | Some k -> prop.key k
            | None -> ()
            prop.className tabClass
            prop.custom ("data-workspace-tab-id", tab.Id.Value)
            prop.custom ("data-workspace-pane-id", paneIdKey)
            yield! dndProps
            prop.onMouseUp (fun e ->
                match e.button with
                | 0. -> dispatchCtx.dispatch (FocusTab tab.Id)
                | 1. -> dispatchCtx.dispatch (RemoveTab tab.Id)
                | _ -> ()
            )
            prop.children [
                paneStateCtx.renderTab tab
                Html.button [
                    prop.type'.button
                    prop.className
                        "swt:ml-1 swt:rounded-sm swt:p-0.5 swt:hover:bg-base-300 swt:inline-flex swt:items-center swt:justify-center"
                    prop.ariaLabel $"Close {tab.Label}"
                    prop.onClick (fun e ->
                        e.stopPropagation ()
                        dispatchCtx.dispatch (RemoveTab tab.Id)
                    )
                    prop.onMouseUp (fun e -> e.stopPropagation ())
                    prop.children [
                        Html.i [
                            prop.className "swt:iconify swt:fluent--dismiss-12-filled swt:size-3.5"
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member TabBar(paneId: PaneId, ?key: string) =
        let paneCtx = usePaneCtx ()
        let paneStateCtx = useWorkspacePaneStateCtx ()

        let tabs = paneCtx.tabs
        let paneIdKey = paneId.Value.ToString("N")

        let dragIds =
            React.useMemo (
                (fun () ->
                    tabs
                    |> Array.map (fun tab -> DndId.write (DndId.Tab(paneIdKey, tab.Id.Value)))
                    |> ResizeArray
                ),
                [| box tabs; box paneIdKey |]
            )

        Html.div [
            prop.custom ("data-workspace-tabbar", paneIdKey)
            prop.className [
                "swt:overflow-x-auto swt:flex swt:flex-row swt:items-center swt:justify-start swt:flex-nowrap swt:gap-0 swt:scrollbar-thin swt:w-full"
                if paneCtx.isFocusedPane then
                    "swt:bg-primary/10"
                else
                    "swt:bg-base-300"
            ]
            if paneStateCtx.debug then
                prop.testId $"workspace-tabbar-{paneIdKey}"
            prop.style [ style.minHeight 35 ]
            prop.children [
                DndKit.SortableContext(
                    items = dragIds,
                    strategy = DndKit.horizontalListSortingStrategy,
                    children =
                        React.Fragment [
                            for i in 0 .. tabs.Length - 1 do
                                let tab = tabs.[i]
                                let index = i
                                let isActive = paneCtx.focusedTab = Some tab.Id

                                TabBar.Tab(
                                    tab,
                                    index,
                                    paneIdKey,
                                    isActive,
                                    paneCtx.isFocusedPane,
                                    key = $"{paneIdKey}:{tab.Id.Value}"
                                )
                        ]
                )
            ]
        ]
