module Arcitisa.Components.Composite.Workspace.Helper.DndId

open Fable.Core
open Arcitisa.Components.Composite.Workspace.Types

type DndId =
    | Tab of paneId: string * tabId: string
    | TabBar of paneId: string
    | EdgeZone of paneId: string * direction: EdgeDirection

type DndId with

    static member write(id: DndId) : string =
        match id with
        | Tab(paneId, tabId) -> $"tab::{paneId}::{tabId}"
        | TabBar(paneId) -> $"pane-bar::{paneId}"
        | EdgeZone(paneId, dir) -> $"pane::{paneId}::edge::{EdgeDirection.toString dir}"

    static member read(str: string) : DndId option =
        if str.StartsWith "tab::" then
            let rest = str.Substring("tab::".Length)
            let idx = rest.LastIndexOf "::"

            if idx > 0 then
                let paneId = rest.Substring(0, idx)
                let tabId = rest.Substring(idx + 2)
                Some(Tab(paneId, tabId))
            else
                None
        elif str.StartsWith "pane-bar::" then
            Some(TabBar(str.Substring("pane-bar::".Length)))
        elif str.StartsWith "pane::" && str.Contains "::edge::" then
            let afterPane = str.Substring("pane::".Length)
            let edgeIdx = afterPane.IndexOf "::edge::"

            if edgeIdx > 0 then
                let paneId = afterPane.Substring(0, edgeIdx)
                let dirStr = afterPane.Substring(edgeIdx + "::edge::".Length)

                match EdgeDirection.fromString dirStr with
                | Some dir -> Some(EdgeZone(paneId, dir))
                | None -> None
            else
                None
        else
            None

    member this.edgeToSplitDirection() : SplitDirection option =
        match this with
        | EdgeZone(_, dir) ->
            match dir with
            | EdgeDirection.Top
            | EdgeDirection.Bottom -> Some SplitDirection.Vertical
            | EdgeDirection.Left
            | EdgeDirection.Right -> Some SplitDirection.Horizontal
        | _ -> None
