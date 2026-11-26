namespace Model

// open Arcitisa.Components.Shared
// open ARCtrl

// open Feliz

// open Model

// module ModalState =

//     type TableModals =
//         | EditColumn of columIndex: int
//         | MoveColumn of columnIndex: int
//         | BatchUpdateColumnValues of columIndex: int * column: CompositeColumn
//         | SelectiveFileImport of ArcFiles
//         | TemplateImport
//         | TermDetails of OntologyAnnotation
//         | TableCellDetailsAtIndex of columnIndex: int * rowIndex: int
//         | ResetTable

//     type ExcelModals = | InteropLogging

//     type GeneralModals =
//         | Error of exn
//         | Warning of string
//         | Loading

//     type ModalTypes =
//         | TableModal of TableModals
//         | ExcelModal of ExcelModals
//         | GeneralModal of GeneralModals
//         | Force of ReactElement

// open ModalState

// type ModalState = {
//     ActiveModal: ModalTypes option
// } with

//     static member init() = { ActiveModal = None }
