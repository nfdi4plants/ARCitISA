module BuildingBlock.Core

open Model
open Messages
open Messages.BuildingBlock
open Arcitisa.Components.Shared
open SearchComponent

open Elmish
open ARCtrl

let update
    (addBuildingBlockMsg: BuildingBlock.Msg)
    (state: BuildingBlock.Model)
    : BuildingBlock.Model * Cmd<Messages.Msg> =
    match addBuildingBlockMsg with
    | UpdateBodyArg next ->
        let nextState = { state with BodyArg = next }
        nextState, Cmd.none
    | UpdateCommentHeader header ->
        let nextState = { state with CommentHeader = header }
        nextState, Cmd.none
    | UpdateHeaderArg next ->
        let nextState = { state with HeaderArg = next }
        nextState, Cmd.none
    | UpdateHeaderCellType next ->
        let nextState =
            if Helper.isSameMajorCompositeHeaderDiscriminate state.HeaderCellType next then
                { state with HeaderCellType = next }
            else
                let nextBodyCellType =
                    if next.IsTermColumn() then
                        CompositeCellDiscriminate.Term
                    else
                        CompositeCellDiscriminate.Text

                {
                    state with
                        HeaderCellType = next
                        BodyCellType = nextBodyCellType
                        HeaderArg = None
                        BodyArg = None
                }

        nextState, Cmd.none
    | UpdateHeaderWithIO(hct, iotype) ->
        let nextState = {
            state with
                HeaderCellType = hct
                HeaderArg = Some(Fable.Core.U2.Case2 iotype)
                BodyArg = None
                BodyCellType = CompositeCellDiscriminate.Text
        }

        nextState, Cmd.none
    | UpdateBodyCellType next ->
        let nextState = { state with BodyCellType = next }
        nextState, Cmd.none

open Feliz

let addBuildingBlockComponent (model: Model) (dispatch: Messages.Msg -> unit) =
    SidebarComponents.SidebarLayout.Container [
        SidebarComponents.SidebarLayout.Header "Building Blocks"

        // Input forms, etc related to add building block.
        SidebarComponents.SidebarLayout.Description "Add annotation building blocks (columns) to the annotation table."
        SidebarComponents.SidebarLayout.LogicContainer [ SearchComponent.Main(model, dispatch) ]
        match model.PersistentStorageState.Host with
        | Some Arcitisahost.Excel ->
            Html.p "Convert existing Building Block."
            SidebarComponents.SidebarLayout.LogicContainer [ CellConvertComponent.Main() ]
        | _ -> Html.none
    ]
