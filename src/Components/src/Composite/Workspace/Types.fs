module Arcitisa.Components.Composite.Workspace.Types

open System
open Fable.Core

[<StringEnum>]
[<RequireQualifiedAccess>]
type SplitDirection =
    | Horizontal
    | Vertical

[<StringEnum>]
[<RequireQualifiedAccess>]
type EdgeDirection =
    | Top
    | Bottom
    | Left
    | Right

    member this.AsSplitDirection() =
        match this with
        | Top
        | Bottom -> SplitDirection.Vertical
        | Left
        | Right -> SplitDirection.Horizontal

    static member fromString(str: string) : EdgeDirection option =
        match str with
        | "top" -> Some Top
        | "bottom" -> Some Bottom
        | "left" -> Some Left
        | "right" -> Some Right
        | _ -> None

    static member toString(dir: EdgeDirection) : string = unbox<string> dir

[<Erase>]
type TabId =
    | TabId of string

    member this.Value =
        let (TabId id) = this
        id

[<Erase>]
type PaneId =
    | PaneId of Guid

    member this.Value =
        let (PaneId id) = this
        id

[<CompiledName("Tab")>] // Without this, Fable transpiles to ``Tab$1<T>``?
type Tab<'T> = {
    Id: TabId
    Label: string
    Payload: 'T
}

type Pane<'T> = {
    Id: PaneId
    Tabs: Tab<'T> list
    FocusedTab: TabId option
}

[<Erase>]
type SplitId =
    | SplitId of Guid

    member this.Value =
        let (SplitId id) = this
        id

type Leaf = PaneId

[<RequireQualifiedAccess>]
type Layout =
    | Single of Leaf
    | Split of SplitId * SplitDirection * ratio: float * Layout * Layout

type WorkspaceModel<'T> = {
    /// This defines the visual layout of the workspace. Each pane is identified by its PaneId, which is used to look up the tabs in the Panes map.
    Layout: Layout
    /// Maps ``PaneId`` to ``Pane<'T>``. A ``Pane`` contains the tabs and tab focus.
    PanesMap: Map<PaneId, Pane<'T>>
    /// The pane, which was last worked on. This can be switched to a tab in this pane, or was freshly created after a split.
    /// This is used to determine where to place new tabs.
    FocusedPane: PaneId
}

type Msg<'T> =
    | AddTab of Tab<'T>
    | RemoveTab of TabId
    | RemoveOtherTabs of keepTabId: TabId
    | RemoveAllTabs
    | FocusTab of TabId
    | MoveTab of tab: TabId * target: PaneId
    | ReorderTabs of pane: PaneId * fromIndex: int * toIndex: int
    | SplitPaneByTabMove of TabId * PaneId * EdgeDirection
    | ClosePane of PaneId
    | SetSplitRatio of splitId: SplitId * ratio: float

type ContextMenuSpawnData = { tabId: string; paneId: string }

type IWorkspaceHandle =
    abstract activateTab: string -> unit
    abstract closeTab: string -> unit
