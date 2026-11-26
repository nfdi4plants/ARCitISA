module BuildingBlock.Helper

open Arcitisa.Components.Shared
open Model
open Messages
open ARCtrl
open Model.BuildingBlock

let isSameMajorCompositeHeaderDiscriminate (hct1: CompositeHeaderDiscriminate) (hct2: CompositeHeaderDiscriminate) =
    (hct1.IsTermColumn() = hct2.IsTermColumn())
    && (hct1.HasIOType() = hct2.HasIOType())

let selectCompositeHeaderDiscriminate (hct: CompositeHeaderDiscriminate) setUiState close (dispatch: Msg -> unit) =
    BuildingBlock.UpdateHeaderCellType hct |> BuildingBlockMsg |> dispatch
    close ()

    {
        DropdownPage = DropdownPage.Main
        DropdownIsActive = false
    }
    |> setUiState

open Fable.Core

let createCompositeHeaderFromState (state: BuildingBlock.Model) =
    let getOA () =
        state.TryHeaderOA() |> Option.defaultValue (OntologyAnnotation.empty ())

    let getIOType () =
        state.TryHeaderIO() |> Option.defaultValue (IOType.FreeText "")

    match state.HeaderCellType with
    | CompositeHeaderDiscriminate.Component -> CompositeHeader.Component <| getOA ()
    | CompositeHeaderDiscriminate.Characteristic -> CompositeHeader.Characteristic <| getOA ()
    | CompositeHeaderDiscriminate.Factor -> CompositeHeader.Factor <| getOA ()
    | CompositeHeaderDiscriminate.Parameter -> CompositeHeader.Parameter <| getOA ()
    | CompositeHeaderDiscriminate.ProtocolType -> CompositeHeader.ProtocolType
    | CompositeHeaderDiscriminate.ProtocolDescription -> CompositeHeader.ProtocolDescription
    | CompositeHeaderDiscriminate.ProtocolUri -> CompositeHeader.ProtocolUri
    | CompositeHeaderDiscriminate.ProtocolVersion -> CompositeHeader.ProtocolVersion
    | CompositeHeaderDiscriminate.ProtocolREF -> CompositeHeader.ProtocolREF
    | CompositeHeaderDiscriminate.Performer -> CompositeHeader.Performer
    | CompositeHeaderDiscriminate.Date -> CompositeHeader.Date
    | CompositeHeaderDiscriminate.Input -> CompositeHeader.Input <| getIOType ()
    | CompositeHeaderDiscriminate.Output -> CompositeHeader.Output <| getIOType ()
    | CompositeHeaderDiscriminate.Comment -> CompositeHeader.Comment state.CommentHeader
    | CompositeHeaderDiscriminate.Freetext -> failwith "Freetext header type is not yet implemented"

let tryCreateCompositeCellFromState (state: BuildingBlock.Model) =
    match state.HeaderArg, state.BodyCellType, state.BodyArg with
    | Some(U2.Case2 IOType.Data), _, _ -> CompositeCell.emptyData |> Some
    | _, CompositeCellDiscriminate.Term, Some(U2.Case2 oa) -> CompositeCell.createTerm (oa) |> Some
    | _, CompositeCellDiscriminate.Unitized, Some(U2.Case2 oa) -> CompositeCell.createUnitized ("", oa) |> Some
    | _, CompositeCellDiscriminate.Text, Some(U2.Case1 s) -> CompositeCell.createFreeText s |> Some
    | _ -> None

let isValidColumn (header: CompositeHeader) =
    header.IsFeaturedColumn
    || (header.IsTermColumn && header.ToTerm().NameText.Length > 0)
    || header.IsSingleColumn
