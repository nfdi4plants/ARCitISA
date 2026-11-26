namespace Model

open Arcitisa.Components.Shared
open Feliz
open Routing
open Database

type LogItem =
    | Debug of (System.DateTime * string)
    | Info of (System.DateTime * string)
    | Error of (System.DateTime * string)
    | Warning of (System.DateTime * string)

    static member ofInteropLogginMsg(msg: InteropLogging.Msg) =
        match msg.LogIdentifier with
        | InteropLogging.Info -> Info(System.DateTime.UtcNow, msg.MessageTxt)
        | InteropLogging.Debug -> Debug(System.DateTime.UtcNow, msg.MessageTxt)
        | InteropLogging.Error -> Error(System.DateTime.UtcNow, msg.MessageTxt)
        | InteropLogging.Warning -> Warning(System.DateTime.UtcNow, msg.MessageTxt)

    static member private DebugCell =
        Html.td [
            prop.className "swt:bg-info swt:text-info-content swt:font-semibold"
            prop.text "Debug"
        ]

    static member private InfoCell =
        Html.td [
            prop.className "swt:bg-primary swt:text-primary-content swt:font-semibold"
            prop.text "Info"
        ]

    static member private ErrorCell =
        Html.td [
            prop.className "swt:bg-error swt:text-error-content swt:font-semibold"
            prop.text "ERROR"
        ]

    static member private WarningCell =
        Html.td [
            prop.className "swt:bg-warning swt:text-warning-content swt:font-semibold"
            prop.text "Warning"
        ]

    static member toTableRow =
        function
        | Debug(t, m) ->
            Html.tr [
                Html.td (sprintf "[%s]" (t.ToShortTimeString()))
                LogItem.DebugCell
                Html.td m
            ]
        | Info(t, m) ->
            Html.tr [
                Html.td (sprintf "[%s]" (t.ToShortTimeString()))
                LogItem.InfoCell
                Html.td m
            ]
        | Error(t, m) ->
            Html.tr [
                Html.td (sprintf "[%s]" (t.ToShortTimeString()))
                LogItem.ErrorCell
                Html.td m
            ]
        | Warning(t, m) ->
            Html.tr [
                Html.td (sprintf "[%s]" (t.ToShortTimeString()))
                LogItem.WarningCell
                Html.td m
            ]

    static member createInfoNow(message: string) = Info(System.DateTime.UtcNow, message)

    static member createErrorNow(message: string) = Error(System.DateTime.UtcNow, message)

    static member createWarningNow(message: string) =
        Warning(System.DateTime.UtcNow, message)

    static member createDebugNow(message: string) = Debug(System.DateTime.UtcNow, message)

    static member ofStringNow (level: string) (message: string) =
        match level with
        | "Debug"
        | "debug" -> Debug(System.DateTime.UtcNow, message)
        | "Info"
        | "info" -> Info(System.DateTime.UtcNow, message)
        | "Error"
        | "error" -> Error(System.DateTime.UtcNow, message)
        | "Warning"
        | "warning" -> Warning(System.DateTime.UtcNow, message)
        | others -> Error(System.DateTime.UtcNow, sprintf "ARCitISA found an unexpected log identifier: %s" others)

module TermSearch =

    open ARCtrl

    type Model = {
        SelectedTerm: OntologyAnnotation option
        ParentTerm: OntologyAnnotation option

    } with

        static member init() = {
            SelectedTerm = None
            ParentTerm = None
        }

type DevState = {
    Log: LogItem list
    DisplayLogList: LogItem list
} with

    static member init() = { DisplayLogList = []; Log = [] }

type PersistentStorageState = {
    AppVersion: string
    Host: Arcitisahost option
    Autosave: bool
} with

    static member init() = {
        Host = Some Arcitisahost.Browser
        AppVersion = ""
        Autosave = true
    }

    member this.IsARCitect =
        match this.Host with
        | Some Arcitisahost.ARCitect -> true
        | _ -> false

type PageState = {
    SidebarPage: Routing.SidebarPage
    MainPage: Routing.MainPage
    ShowSideBar: bool
} with

    static member init() = {
        SidebarPage = SidebarPage.BuildingBlock
        MainPage = MainPage.Default
        ShowSideBar = false
    }

    member this.IsHome =
        match this.MainPage with
        | MainPage.Default -> true
        | _ -> false

module FilePicker =

    type Model = {
        FileNames: (int * string) list
    } with

        static member init() = { FileNames = [] }

open Fable.Core

module BuildingBlock =

    open ARCtrl

    [<RequireQualifiedAccess>]
    type DropdownPage =
        | Main
        | More
        | IOTypes of CompositeHeaderDiscriminate

        member this.toString =
            match this with
            | Main -> "Main Page"
            | More -> "More"
            | IOTypes t -> t.ToString()

        member this.toTooltip =
            match this with
            | More -> "More"
            | IOTypes t -> $"Per table only one {t} is allowed. The value of this column must be a unique identifier."
            | _ -> ""

    type BuildingBlockUIState = {
        DropdownIsActive: bool
        DropdownPage: DropdownPage
    } with

        static member init() = {
            DropdownIsActive = false
            DropdownPage = DropdownPage.Main
        }

    type Model = {
        HeaderCellType: CompositeHeaderDiscriminate
        HeaderArg: U2<OntologyAnnotation, IOType> option
        BodyCellType: CompositeCellDiscriminate
        BodyArg: U2<string, OntologyAnnotation> option
        CommentHeader: string
    } with

        static member init() = {
            HeaderCellType = CompositeHeaderDiscriminate.Parameter
            HeaderArg = None
            BodyCellType = CompositeCellDiscriminate.Term
            BodyArg = None
            CommentHeader = ""
        }

        member this.TryHeaderOA() =
            match this.HeaderCellType, this.HeaderArg with
            | CompositeHeaderDiscriminate.ProtocolType, _ -> CompositeHeader.ProtocolType.ToTerm() |> Some
            | _, Some(U2.Case1 oa) -> Some oa
            | _ -> None

        member this.TryHeaderIO() =
            match this.HeaderArg with
            | Some(U2.Case2 io) -> Some io
            | _ -> None

        member this.TryBodyOA() =
            match this.BodyArg with
            | Some(U2.Case2 oa) -> Some oa
            | _ -> None

        member this.TryBodyString() =
            match this.BodyArg with
            | Some(U2.Case1 s) -> Some s
            | _ -> None

module Protocol =

    [<RequireQualifiedAccess>]
    type CommunityFilter =
        | All
        | OnlyCurated
        | Community of string

        member this.ToStringRdb() =
            match this with
            | All -> "All"
            | OnlyCurated -> "DataPLANT official"
            | Community name -> name

        static member fromString(str: string) =
            match str with
            | "All" -> All
            | "DataPLANT official" -> OnlyCurated
            | anyElse -> Community anyElse

        static member CommunityFromOrganisation(org: ARCtrl.Organisation) =
            match org with
            | ARCtrl.Organisation.DataPLANT -> None
            | ARCtrl.Organisation.Other name -> Some <| Community name

    /// This model is used for both protocol insert and protocol search
    type Model = {
        // Client
        Loading: bool
        ShowImportModal: bool
        LastUpdated: System.DateTime option
        ImportConfig: FileImport.SelectiveImportConfig
        TemplatesSelected: ARCtrl.Template list
        // ------ Protocol from Database ------
        Templates: ARCtrl.Template[]
    } with

        static member init() = {
            // Client
            Loading = false
            ShowImportModal = false
            LastUpdated = None
            ImportConfig = FileImport.SelectiveImportConfig.init ()
            TemplatesSelected = []
            // ------ Protocol from Database ------
            Templates = [||]
        }

type RequestBuildingBlockInfoStates =
    | Inactive
    | RequestExcelInformation
    | RequestDataBaseInformation

    member this.toStringMsg =
        match this with
        | Inactive -> ""
        | RequestExcelInformation -> "Check Columns"
        | RequestDataBaseInformation -> "Search Database"

type Model = {
    ///PageState
    PageState: PageState
    ///Data that needs to be persistent once loaded
    PersistentStorageState: PersistentStorageState
    ///Error handling, Logging, etc.
    DevState: DevState
    ///States regarding term search
    TermSearchState: TermSearch.Model
    ///Use this in the future to model excel stuff like table data
    ExcelState: OfficeInterop.Model
    ///States regarding File picker functionality
    FilePickerState: FilePicker.Model
    ProtocolState: Protocol.Model
    ///Insert annotation columns
    AddBuildingBlockState: BuildingBlock.Model
    CytoscapeModel: Cytoscape.Model
    ///
    DataAnnotatorModel: DataAnnotator.Model
    /// Contains all information about spreadsheet view
    SpreadsheetModel: Spreadsheet.Model
    History: LocalHistory.Model
    ARCitectState: ARCitect.Model
}
