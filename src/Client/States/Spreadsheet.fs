namespace Spreadsheet

open Swate.Components
open ARCtrl
open FileImport
open Swate.Components.Shared
open Swate.Components.Composite.TermSearch.Types


type ActiveView = Swate.Components.Page.ArcFileEditor.Types.ActiveView

type ColumnType =
    | Main
    | Unit
    | TSR
    | TAN
    | DataSelector
    | DataFormat
    | DataSelectorFormat

    member this.AsNumber =
        match this with
        | Main -> 0
        | Unit
        | DataSelector -> 1
        | TSR
        | DataFormat -> 2
        | TAN
        | DataSelectorFormat -> 3

    /// <summary>
    /// Use this function to get static column header names, such as "Unit", "Data Selector", etc.
    /// </summary>
    member this.ToColumnHeader() =
        match this with
        | Unit -> "Unit"
        | DataSelector -> "Data Selector"
        | DataFormat -> "Data Format"
        | DataSelectorFormat -> "Data Selector Format"
        | anyElse -> failwithf "Error. Unable to call `ColumnType.ToColumnHeader()` on %A!" anyElse

    member this.IsMainColumn =
        match this with
        | Main -> true
        | _ -> false

    member this.IsRefColumn = not this.IsMainColumn

///<summary>If you change this model, it will kill caching for users! if you apply changes to it, make sure to keep a version
///of it and add a try case for it to `tryInitFromLocalStorage` in Spreadsheet/LocalStorage.fs .</summary>
type Model = {
    ActiveView: ActiveView
    ArcFile: ArcFiles option
} with

    static member init(?arcfile, ?activeView) = {
        ActiveView = defaultArg activeView ActiveView.Metadata
        ArcFile = arcfile
    }

    member this.FileType =
        match this.ArcFile with
        | Some(ArcFiles.Assay _) -> "Assay"
        | Some(ArcFiles.Study _) -> "Study"
        | Some(ArcFiles.Investigation _) -> "Investigation"
        | Some(ArcFiles.Run _) -> "Run"
        | Some(ArcFiles.Workflow _) -> "Workflow"
        | Some(ArcFiles.DataMap _) -> "Datamap"
        | Some(ArcFiles.Template _) -> "Template"
        | None -> "No File"

    member this.Tables =
        match this.ArcFile with
        | Some(arcfile) -> arcfile.ArcTables()
        | None -> ResizeArray() |> ArcTables

    member this.ActiveTable =
        match this.ActiveView with
        | ActiveView.Table i -> this.Tables.GetTableAt(i)
        | ActiveView.Metadata
        | ActiveView.DataMap ->
            let t = ArcTable.init ("NULL_TABLE") //return NULL_TABLE-named table for easier handling of return value

            t.AddColumn(
                CompositeHeader.FreeText "WARNING",
                [|
                    CompositeCell.FreeText "If you see this table view, pls contact a developer and report it."
                |]
                |> ResizeArray
            )

            t

    member this.HasMetadata() =
        match this.ArcFile with
        | Some(arcFile) -> arcFile.HasMetadata()
        | None -> false

    member this.DataMapOrDefault =
        match this.ArcFile with
        | Some(ArcFiles.Assay a) when a.Datamap.IsSome -> a.Datamap.Value
        | Some(ArcFiles.Run r) when r.Datamap.IsSome -> r.Datamap.Value
        | Some(ArcFiles.Workflow w) when w.Datamap.IsSome -> w.Datamap.Value
        | Some(ArcFiles.Study(s, _)) when s.Datamap.IsSome -> s.Datamap.Value
        | Some(ArcFiles.DataMap(_, d)) -> d
        | _ -> Datamap.init ()

    member this.GetAssay() =
        match this.ArcFile with
        | Some(ArcFiles.Assay a) -> a
        | _ -> ArcAssay.init ("ASSAY_NULL")

    member this.CanHaveTables() =
        match this.ArcFile with
        | Some(ArcFiles.Assay _)
        | Some(ArcFiles.Study _)
        | Some(ArcFiles.Run _) -> true
        | _ -> false

    member this.TableViewIsActive() =
        match this.ActiveView with
        | ActiveView.Table _ -> true
        | _ -> false

type Msg =
    // <--> UI <-->
    | UpdateState of Model
    | UpdateCell of CellCoordinate * CompositeCell
    | UpdateCells of (CellCoordinate * CompositeCell)[]
    | UpdateHeader of columIndex: int * CompositeHeader
    | UpdateActiveView of ActiveView
    | MoveColumn of current: int * next: int
    | UpdateDatamap of Datamap option
    | UpdateDataMapDataContextAt of index: int * DataContext
    | AddTable of ArcTable
    | UpdateTable of ArcTable
    | RemoveTable of index: int
    | RenameTable of index: int * name: string
    | UpdateTableOrder of pre_index: int * new_index: int
    | AddRows of int
    | DeleteRow of int
    | DeleteRows of int[]
    | DeleteColumn of int
    | SetColumn of index: int * column: CompositeColumn
    | SetCell of CellCoordinate * term: Term option
    | Clear of index: CellCoordinate[]
    | FillColumnWithTerm of index: CellCoordinate
    // /// Update column of index to new column type defined by given SwateCell.emptyXXX
    // | EditColumn of index: int * newType: SwateCell * b_type: BuildingBlockType option
    /// This will reset Spreadsheet.Model to Spreadsheet.Model.init() and clear all webstorage.
    | Reset
    | ImportXlsx of byte[]
    // <--> INTEROP <-->
    | CreateAnnotationTable of tryUsePrevOutput: bool
    | AddAnnotationBlock of index: int option * CompositeColumn
    | AddAnnotationBlocks of index: int option * CompositeColumn[]
    | AddDataAnnotation of
        {|
            fragmentSelectors: string[]
            fileName: string
            fileType: string
            targetColumn: DataAnnotator.TargetColumn
        |}
    | AddTemplates of ArcTable[] * SelectiveImportConfig
    | JoinTable of ArcTable * index: int option * options: TableJoinOptions option * string option
    | UpdateArcFile of ArcFiles
    | InitFromArcFile of ArcFiles
    | InsertOntologyAnnotation of CellCoordinateRange * OntologyAnnotation
    | InsertOntologyAnnotations of OntologyAnnotation[]
    /// Starts chain to export active table to isa json
    | ExportJson of ArcFiles * JsonExportFormat
    | ImportJsonRaw of SpreadsheetInterface.ImportJsonRawDTO
    /// Starts chain to export all tables to xlsx swate tables.
    | ExportXlsx of ArcFiles
    | ExportXlsxDownload of filename: string * byte[]
// <--> Result Messages <-->
///// This message will save `Model` to local storage and to session storage for history
//| Success of Model
///// This message will save `Model` to local storage
//| SuccessNoHistory of Model
