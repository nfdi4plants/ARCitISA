namespace Arcitisa.Components.Composite.Workspace

open System
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Browser.Types
open Arcitisa.Components
open Arcitisa.Components.Primitive.ContextMenu
open Arcitisa.Components.Primitive.ContextMenu.Types
open Arcitisa.Components.Composite.Workspace.Types
open Arcitisa.Components.Composite.Workspace.Context

module private TabContextMenuHelper =

    let dataKey = "data-workspace-tab-id"

    let tryGetSpawnData (e: MouseEvent) : ContextMenuSpawnData option =
        let target = e.target :?> HTMLElement

        target.closest ($"[{dataKey}]")
        |> Option.bind (fun el ->
            let el = el :?> HTMLElement
            let tabId = el?dataset?workspaceTabId
            let paneId = el?dataset?workspacePaneId

            match tabId, paneId with
            | null, _
            | _, null -> None
            | tabId, paneId ->
                Some {
                    tabId = string tabId
                    paneId = string paneId
                }
        )

open TabContextMenuHelper

[<Erase; Mangle(false)>]
type TabContextMenu =

    [<ReactComponent>]
    static member TabContextMenu(containerRef: IRefValue<Browser.Types.HTMLElement option>) =
        let dispatchCtx = useWorkspaceDispatchCtx ()

        let onSpawn (e: MouseEvent) : obj option = tryGetSpawnData e |> Option.map box

        let childInfo (data: obj) : ContextMenuItem list =
            let spawnData = unbox<ContextMenuSpawnData> data
            let tabId = TabId spawnData.tabId
            let paneId = PaneId(Guid.Parse(spawnData.paneId))

            let close _ = dispatchCtx.dispatch (RemoveTab tabId)

            let closeOthers _ =
                dispatchCtx.dispatch (RemoveOtherTabs tabId)

            let closeAll _ = dispatchCtx.dispatch RemoveAllTabs

            let closeAllInPane _ = dispatchCtx.dispatch (ClosePane paneId)

            [
                ContextMenuItem(
                    text = Html.span "Close",
                    icon =
                        Html.i [
                            prop.className "swt:iconify swt:fluent--dismiss-12-filled swt:size-4"
                        ],
                    onClick = close
                )
                ContextMenuItem(
                    text = Html.span "Close Others",
                    icon =
                        Html.i [
                            prop.className "swt:iconify swt:fluent--tab-desktop-multiple-bottom-20-regular swt:size-4"
                        ],
                    onClick = closeOthers
                )
                ContextMenuItem(
                    text = Html.span "Close All",
                    icon =
                        Html.i [
                            prop.className "swt:iconify swt:fluent--delete-20-filled swt:size-4"
                        ],
                    onClick = closeAll
                )
                ContextMenuItem(isDivider = true)
                ContextMenuItem(
                    text = Html.span "Close All in Pane",
                    icon =
                        Html.i [
                            prop.className "swt:iconify swt:fluent--tab-desktop-20-regular swt:size-4"
                        ],
                    onClick = closeAllInPane
                )
            ]

        ContextMenu.ContextMenu(childInfo, containerRef, onSpawn = onSpawn)
