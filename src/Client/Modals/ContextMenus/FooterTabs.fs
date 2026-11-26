namespace Modals.ContextMenus

open ARCtrl
open Feliz
open Arcitisa.Components
open Arcitisa.Components.Primitive
open Arcitisa.Components.Primitive.ContextMenu
open Arcitisa.Components.Primitive.ContextMenu.Types
open Arcitisa.Components.Shared

type FooterTabs =
    static member Table(tableIndex: int, startEdit: _ -> unit, dispatch: Messages.Msg -> unit, tabRef) =
        let delete =
            fun _ -> Spreadsheet.RemoveTable tableIndex |> Messages.SpreadsheetMsg |> dispatch

        let rename = fun _ -> startEdit ()

        let children =
            fun _ -> [
                ContextMenuItem(Html.span "Rename", icon = Icons.PenToSquare(), onClick = rename)
                ContextMenuItem(Html.span "Delete", icon = Icons.Delete(), onClick = delete)
            ]

        ContextMenu.ContextMenu(children, ref = tabRef)

    static member Plus(dispatch: Messages.Msg -> unit, tabRef) =
        let addTable =
            fun _ ->
                SpreadsheetInterface.CreateAnnotationTable false
                |> Messages.InterfaceMsg
                |> dispatch

        // let addDataMap =
        //     fun _ ->
        //         SpreadsheetInterface.UpdateDatamap(Datamap.init () |> Some)
        //         |> Messages.InterfaceMsg
        //         |> dispatch

        let children =
            fun _ -> [
                ContextMenuItem(Html.text "Add Table", icon = Icons.Table(), onClick = addTable)
            // Modals.ContextMenus.Base.Item("Add Datamap", addDataMap >> rmv, Icons.Map())
            ]

        ContextMenu.ContextMenu(children, ref = tabRef)

    [<ReactComponent>]
    static member DataMap(dispatch: Messages.Msg -> unit, tabRef) =
        let delete =
            fun _ -> SpreadsheetInterface.UpdateDatamap None |> Messages.InterfaceMsg |> dispatch

        let children =
            fun _ -> [
                ContextMenuItem(Html.text "Delete", icon = Icons.Delete(), onClick = delete)
            ]

        ContextMenu.ContextMenu(children, ref = tabRef)

// let mouseY = mouseY - 10
// Base.Main(mouseX, mouseY, children, dispatch)
