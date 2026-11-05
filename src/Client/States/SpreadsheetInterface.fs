namespace SpreadsheetInterface

open Swate.Components
open ARCtrl
open FileImport
open Swate.Components.Shared


type ImportJsonRawDTO = {|
    jsonString: string
    jsonType: JsonExportFormat option
    filetype: ArcFilesDiscriminate option
    fileName: string option
|}

///<summary>This type is used to interface between standalone, electron and excel logic and will forward the command to the correct logic.</summary>
type Msg =
    | Initialize of Swatehost
    | CreateAnnotationTable of tryUsePrevOutput: bool
    | RemoveBuildingBlock of index: CellCoordinateRange option
    | UpdateDatamap of Datamap option
    | UpdateDataMapDataContextAt of index: int * DataContext
    | AddTable of ArcTable
    | ValidateBuildingBlock
    | AddAnnotationBlock of columnIndex: int option * CompositeColumn
    | AddAnnotationBlocks of columnIndex: int option * CompositeColumn[]
    | AddDataAnnotation of
        {|
            fragmentSelectors: string[]
            fileName: string
            fileType: string
            targetColumn: DataAnnotator.TargetColumn
        |}
    /// This function will do preprocessing on the tables to join
    | AddTemplates of ArcTable list * SelectiveImportConfig
    | JoinTable of ArcTable * columnIndex: int option * options: TableJoinOptions option
    | UpdateArcFile of ArcFiles
    /// Inserts TermMinimal to selected fields of one column
    | InsertOntologyAnnotation of range: CellCoordinateRange option * OntologyAnnotation
    | InsertFileNames of range: CellCoordinateRange option * string list
    | ImportXlsx of byte[]
    | ImportRawJson of ImportJsonRawDTO
    | ImportJson of
        {|
            importState: SelectiveImportConfig
            importedFile: ArcFiles
            deselectedColumns: Set<int * int>
        |}
    /// Starts chain to export active table to isa json
    | ExportJson of ArcFiles * JsonExportFormat
    | UpdateUnitForCells
    | RectifyTermColumns
