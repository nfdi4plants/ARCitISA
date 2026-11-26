module Arcitisa.Components.Composite.Workspace.Helper.DropTarget

open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open Arcitisa.Components.Composite.Workspace.Types


/// This is required because fantomas produced some issues with dynamic bindings:
/// System.Exception: cannot determine if Expr PrefixApp
///   ExprPrefixAppNode((39,40--39,62)
///   SingleTextNode((39,40--39,42), "!!") (...) is uppercase or lowercase

module private BrowserTypesExtensions =

    type Document with
        [<Emit("$0.elementsFromPoint($1, $2)")>]
        member _.elementsFromPoint(x: float, y: float) : Element[] = jsNative


open BrowserTypesExtensions

type DropTarget =
    | TabBarDrop of paneId: string
    | EdgeDrop of paneId: string * direction: EdgeDirection

let findPaneElement (x: float) (y: float) (workspaceEl: HTMLElement) : HTMLElement option =

    let walkFrom (el: HTMLElement) : HTMLElement option =
        let mutable current: HTMLElement option = Some el
        let mutable result: HTMLElement option = None

        while current.IsSome do
            let c = current.Value

            if obj.ReferenceEquals(c, workspaceEl) then
                current <- None
            elif not (isNull (c.getAttribute "data-workspace-pane")) then
                result <- Some c
                current <- None
            elif not (isNull (c.getAttribute "data-workspace-tabbar")) then
                result <- Some c
                current <- None
            else
                current <- Option.ofObj c.parentElement

        result

    match Browser.Dom.document.elementFromPoint (x, y) with
    | :? HTMLElement as el when not (isNull el) ->
        match walkFrom el with
        | Some _ as result -> result
        | None ->
            let elements: Element[] = Browser.Dom.document.elementsFromPoint (x, y)

            elements
            |> Array.tryPick (fun e ->
                match e with
                | :? HTMLElement as h when workspaceEl.contains (h) -> walkFrom h
                | _ -> None
            )
    | _ -> None

let resolveDropTarget
    (element: HTMLElement)
    (pointerX: float)
    (pointerY: float)
    (workspaceEl: HTMLElement)
    (sourcePaneId: string)
    : DropTarget option =

    let mutable current: HTMLElement option = Some element
    let mutable targetPaneId: string option = None
    let mutable isTabBar = false
    let mutable paneElement: HTMLElement option = None

    while current.IsSome do
        let el = current.Value

        if obj.ReferenceEquals(el, workspaceEl) then
            current <- None
        else
            let paneAttr = el.getAttribute "data-workspace-pane"

            if not (isNull paneAttr) && targetPaneId.IsNone then
                targetPaneId <- Some paneAttr
                paneElement <- Some el

            let tabBarAttr = el.getAttribute "data-workspace-tabbar"

            if not (isNull tabBarAttr) then
                isTabBar <- true

            current <- Option.ofObj el.parentElement

    match targetPaneId with
    | None -> None
    | Some paneId when isTabBar && paneId = sourcePaneId -> None
    | Some paneId when isTabBar -> Some(TabBarDrop paneId)
    | Some paneId ->
        let paneEl =
            Option.orElseWith
                (fun () ->
                    let found = workspaceEl.querySelector ($"[data-workspace-pane=\"{paneId}\"]")

                    match found with
                    | :? HTMLElement as h -> Some h
                    | _ -> None
                )
                paneElement

        match paneEl with
        | Some el ->
            let rect = el.getBoundingClientRect ()

            let relX = (pointerX - rect.left) / rect.width
            let relY = (pointerY - rect.top) / rect.height

            let distances = [
                EdgeDirection.Top, relY
                EdgeDirection.Bottom, 1.0 - relY
                EdgeDirection.Left, relX
                EdgeDirection.Right, 1.0 - relX
            ]

            let closest = distances |> List.minBy snd
            Some(EdgeDrop(paneId, fst closest))

        | None -> None
