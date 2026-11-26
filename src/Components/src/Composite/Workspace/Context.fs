module Arcitisa.Components.Composite.Workspace.Context

open Fable.Core
open Feliz
open Arcitisa.Components.Composite.Workspace.Types
open Arcitisa.Components.JsBindings

// -- DnD context --

type WorkspaceDndContext = {
    onDragStart: DndKit.IDndKitEvent -> unit
    handleDragEnd: DndKit.IDndKitEvent -> unit
    isDragging: bool
}

let WorkspaceDndCtx =
    React.createContext<WorkspaceDndContext> (Unchecked.defaultof<WorkspaceDndContext>)

[<Hook>]
let useWorkspaceDndCtx () = React.useContext WorkspaceDndCtx

// -- Dispatch context --

type WorkspaceDispatchContext<'T> = { dispatch: Msg<'T> -> unit }

let WorkspaceDispatchCtx = React.createContext<obj> (null)

[<Hook>]
let useWorkspaceDispatchCtx<'T> () : WorkspaceDispatchContext<'T> =
    React.useContext WorkspaceDispatchCtx |> unbox

// -- Layout context --

type WorkspaceLayoutContext = { layout: Layout }

let WorkspaceLayoutCtx =
    React.createContext<WorkspaceLayoutContext> (Unchecked.defaultof<WorkspaceLayoutContext>)

[<Hook>]
let useWorkspaceLayoutCtx () = React.useContext WorkspaceLayoutCtx

// -- Pane state context --

type WorkspacePaneStateContext<'T> = {
    panesMap: Map<PaneId, Pane<'T>>
    focusedPane: PaneId
    renderTabContent: Tab<'T> -> ReactElement
    renderTab: Tab<'T> -> ReactElement
    debug: bool
}

let WorkspacePaneStateCtx = React.createContext<obj> (null)

[<Hook>]
let useWorkspacePaneStateCtx<'T> () : WorkspacePaneStateContext<'T> =
    React.useContext WorkspacePaneStateCtx |> unbox

// -- Per-pane context (created fresh per LeafNode) --

type PaneContext<'T> = {
    paneId: PaneId
    tabs: Tab<'T> array
    focusedTab: TabId option
    isFocusedPane: bool
}

let PaneCtx = React.createContext<obj> (null)

[<Hook>]
let usePaneCtx<'T> () : PaneContext<'T> = React.useContext PaneCtx |> unbox

// -- Sortable active context --

type SortableActiveContext = { isActiveRef: IRefValue<bool> }

let SortableActiveCtx =
    React.createContext<SortableActiveContext> (Unchecked.defaultof<SortableActiveContext>)

[<Hook>]
let useSortableActiveCtx () = React.useContext SortableActiveCtx
