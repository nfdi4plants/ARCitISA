module MainComponents.FooterTabs

open Feliz
open ARCtrl
open Model
open Arcitisa.Components
open Arcitisa.Components.Primitive
open Arcitisa.Components.Primitive.BaseModal
open Arcitisa.Components.Shared


type private FooterTab = {
    IsEditable: bool
    IsDraggedOver: bool
    Name: string
} with

    static member init(?name: string) = {
        IsEditable = false
        IsDraggedOver = false
        Name = Option.defaultValue "" name
    }

open Spreadsheet.Types

///<summary>This must be used on dragover events to enable dropping elements on them.</summary>
let private drag_preventdefault =
    fun (e: Browser.Types.DragEvent) ->
        e.preventDefault ()
        e.stopPropagation ()

///<summary>This is fired from the element on which something is dropped. Gets the data set during dragstart and uses it to update order.</summary>
let private drop_handler (eleOrder, state, setState, dispatch) =
    fun (e: Browser.Types.DragEvent) ->
        // This event fire on the element on which something is dropped! Not on the element which is dropped!
        let data = e.dataTransfer.getData ("text")
        let getData = FooterReorderData.ofJson data
        setState { state with IsDraggedOver = false }

        match getData with
        | Ok data ->
            let prev_index = data.OriginOrder
            let next_index = eleOrder

            Spreadsheet.UpdateTableOrder(prev_index, next_index)
            |> Messages.SpreadsheetMsg
            |> dispatch
        | _ -> ()

///<summary>Sets styling on event, styling must then be removed ondragleave and ondrop.</summary>
let private dragenter_handler (state, setState) =
    fun (e: Browser.Types.DragEvent) ->
        e.preventDefault ()
        e.stopPropagation ()
        setState { state with IsDraggedOver = true }

///<summary>Removes dragenter styling.</summary>
let private dragleave_handler (state, setState) =
    fun (e: Browser.Types.DragEvent) ->
        e.preventDefault ()
        e.stopPropagation ()
        setState { state with IsDraggedOver = false }

[<Literal>]
let private DragOverClass =
    "swt:border! swt:border-dashed! swt:border-base-content!"

[<ReactComponent>]
let Main (index: int, tables: ArcTables, model: Model, dispatch: Messages.Msg -> unit) =
    let table = tables.GetTableAt(index)
    let state, setState = React.useState (FooterTab.init (table.Name))
    let id = $"ReorderMe_{index}_{table.Name}"
    let tabRef = React.useElementRef ()

    /// https://github.com/nfdi4plants/ARCitISA/issues/925
    let startEdit = fun _ -> { state with IsEditable = true } |> setState

    let updateName =
        fun e ->
            if state.Name <> table.Name then
                Spreadsheet.RenameTable(index, state.Name)
                |> Messages.SpreadsheetMsg
                |> dispatch

            setState { state with IsEditable = false }

    React.Fragment [
        Modals.ContextMenus.FooterTabs.Table(index, startEdit, dispatch, tabRef)
        BaseModal.Modal(
            state.IsEditable,
            (fun b -> setState { state with IsEditable = b }),
            header = Html.h1 "Edit Table Name",
            children =
                Html.input [
                    prop.className "swt:input swt:w-full"
                    prop.autoFocus (true)
                    prop.id (id + "input")
                    prop.onChange (fun e -> setState { state with Name = e })
                    // .. when pressing "ENTER". "ESCAPE" will negate changes.
                    prop.onKeyDown (fun e ->
                        match e.code with
                        | Arcitisa.Components.kbdEventCode.enter -> updateName e
                        | Arcitisa.Components.kbdEventCode.escape ->
                            setState {
                                state with
                                    IsEditable = false
                                    Name = table.Name
                            }
                        | _ -> ()
                    )
                    prop.defaultValue table.Name
                ],
            footer =
                Html.button [
                    prop.className "swt:btn swt:btn-sm swt:btn-primary swt:ml-auto"
                    prop.text "Save"
                    prop.onClick updateName
                ]
        )
        Html.div [
            prop.ref tabRef
            prop.className [
                "swt:tab"
                if not (state.IsEditable) then
                    "swt:*:pointer-events-none"
                if state.IsDraggedOver then
                    DragOverClass
                if model.SpreadsheetModel.ActiveView = Spreadsheet.ActiveView.Table index then
                    "swt:tab-active"
            ]
            prop.draggable true
            prop.onDrop <| drop_handler (index, state, setState, dispatch)
            prop.onDragLeave <| dragleave_handler (state, setState)
            prop.onDragStart (fun e ->
                e.dataTransfer.clearData () |> ignore
                let data = FooterReorderData.create index id
                let dataJson = data.toJson ()
                e.dataTransfer.setData ("text", dataJson) |> ignore
                ()
            )
            prop.onDragEnter <| dragenter_handler (state, setState)
            prop.onDragOver drag_preventdefault
            // This will determine the position of the tab
            prop.style [ style.custom ("order", index) ]
            // Use this to ensure updating reactelement correctly
            prop.key id
            prop.id id
            prop.onClick (fun _ ->
                Spreadsheet.UpdateActiveView(Spreadsheet.ActiveView.Table index)
                |> Messages.SpreadsheetMsg
                |> dispatch
            )
            prop.draggable true
            prop.children [
                Icons.Table()
                Html.span [ prop.className "swt:truncate"; prop.text table.Name ]
            ]
        ]
    ]

[<ReactComponent>]
let MainMetadata (model: Model, dispatch: Messages.Msg -> unit) =
    let id = "Metadata-Tab"
    let nav = Spreadsheet.ActiveView.Metadata
    let order = nav.ViewIndex

    Html.div [
        prop.className [
            "swt:tab"
            if model.SpreadsheetModel.ActiveView = nav then
                "swt:tab-active"
        ]
        prop.key id
        prop.id id
        prop.onClick (fun _ -> Spreadsheet.UpdateActiveView nav |> Messages.SpreadsheetMsg |> dispatch)
        prop.style [
            style.custom ("order", order)
            style.height (length.percent 100)
            style.cursor.pointer
        ]
        prop.children [
            Icons.InfoCircle([||])
            Html.text model.SpreadsheetModel.FileType
        ]
    ]

[<ReactComponent>]
let MainDataMap (model: Model, dispatch: Messages.Msg -> unit) =
    let id = "Metadata-Tab"
    let nav = Spreadsheet.ActiveView.DataMap
    let order = nav.ViewIndex
    let tabRef = React.useElementRef ()

    React.Fragment [
        Modals.ContextMenus.FooterTabs.DataMap(dispatch, tabRef)
        Html.div [
            prop.className [
                "swt:tab"
                if model.SpreadsheetModel.ActiveView = nav then
                    "swt:tab-active"
            ]
            prop.ref tabRef
            prop.key id
            prop.id id
            prop.onClick (fun _ -> Spreadsheet.UpdateActiveView nav |> Messages.SpreadsheetMsg |> dispatch)
            prop.style [
                style.custom ("order", order)
                style.height (length.percent 100)
                style.cursor.pointer
            ]
            prop.children [ Icons.Map(); Html.text "Data Map" ]
        ]
    ]

[<ReactComponent>]
let MainPlus (model: Model, dispatch: Messages.Msg -> unit) =
    let state, setState = React.useState (FooterTab.init ())
    let order = System.Int32.MaxValue - 1 // MaxValue will be sidebar toggle
    let id = "Add-Spreadsheet-Button"
    let tabRef = React.useElementRef ()

    React.Fragment [
        Modals.ContextMenus.FooterTabs.Plus(dispatch, tabRef)
        Html.div [
            prop.className [
                "swt:tab swt:*:pointer-events-none"
                if state.IsDraggedOver then
                    DragOverClass
            ]
            prop.key id
            prop.id id
            prop.ref tabRef
            prop.onDragEnter <| dragenter_handler (state, setState)
            prop.onDragLeave <| dragleave_handler (state, setState)
            prop.onDragOver drag_preventdefault
            prop.onDrop <| drop_handler (order, state, setState, dispatch)
            prop.title "New Table"
            prop.onClick (fun e ->
                SpreadsheetInterface.CreateAnnotationTable e.ctrlKey
                |> Messages.InterfaceMsg
                |> dispatch
            )
            prop.style [
                style.custom ("order", order)
                style.height (length.percent 100)
                style.cursor.pointer
            ]
            prop.children [ Icons.Plus(); Html.span [ prop.text "New Table" ] ]
        ]
    ]

[<ReactComponent>]
let ToggleSidebar (model: Model, dispatch: Messages.Msg -> unit) =
    let show = model.PageState.ShowSideBar
    let id = "toggle-sidebar-button"

    Html.div [
        prop.id id
        prop.onClick (fun _ ->
            Messages.PageState.UpdateShowSidebar(not show)
            |> Messages.PageStateMsg
            |> dispatch
        )
        prop.className "swt:h-full swt:cursor-pointer swt:ml-auto swt:overflow-hidden"
        prop.children [
            Html.label [
                // prop.htmlFor "split-window-drawer"
                prop.className
                    "swt:drawer swt:drawer-button swt:btn swt:btn-sm swt:px-2 swt:py-2 swt:swap swt:swap-rotate swt:rounded-none swt:h-full"
                prop.children [
                    Html.input [ prop.type'.checkbox ]
                    Html.div [
                        prop.className [ "swt:swap-off" ]
                        prop.children [ Icons.ChevronLeft() ]
                    ]
                    Html.div [
                        prop.className [ "swt:swap-on" ]
                        prop.children [ Icons.ChevronRight() ]
                    ]
                ]
            ]
        ]
    ]

[<ReactComponent>]
let SpreadsheetSelectionFooter (model: Model) dispatch =
    Html.div [
        prop.className "swt:sticky swt:bottom-0 swt:flex swt:flex-row swt:border-t-2"
        prop.children [
            Html.div [
                prop.className
                    "swt:*:[--tab-border-color:var(--color-base-content)] swt:tabs swt:tabs-lift swt:w-full \
                    swt:overflow-x-auto swt:overflow-y-hidden swt:flex swt:flex-row swt:items-center \
                    swt:justify-start swt:pt-1 swt:*:border-b-0! swt:*:gap-1 swt:flex-nowrap swt:*:flex-nowrap"
                prop.children [
                    Html.div [
                        prop.className "swt:tab swt:max-w-min swt:px-2!"
                        prop.style [ style.custom ("order", -2) ]
                    ]

                    if model.SpreadsheetModel.HasMetadata() then
                        MainMetadata(model, dispatch)
                    match model.SpreadsheetModel.ArcFile with
                    | Some(ArcFiles.DataMap _) -> MainDataMap(model, dispatch)
                    | _ -> ()
                    // if model.SpreadsheetModel.HasDataMap() then
                    //     MainDataMap(model, dispatch)
                    for index in 0 .. (model.SpreadsheetModel.Tables.TableCount - 1) do
                        Main(index, model.SpreadsheetModel.Tables, model, dispatch)
                    if model.SpreadsheetModel.CanHaveTables() then
                        MainPlus(model, dispatch)
                ]
            ]
            ToggleSidebar(model, dispatch)
        ]
    ]
