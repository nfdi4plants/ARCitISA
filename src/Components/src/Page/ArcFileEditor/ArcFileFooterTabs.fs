namespace Swate.Components.Page.ArcFileEditor

open Feliz
open Fable.Core
open ARCtrl
open Swate.Components
open Swate.Components.Page.ArcFileEditor.Types
open Swate.Components.Shared
open Swate.Components.JsBindings
open Swate.Components.Primitive.ContextMenu.Types
open Fable.Core.JsInterop

module private ArcFileFooterTabsHelper =

    [<Literal>]
    let FooterTabIdDataKey = "footertabid"

    let mkTableDragId (index: int) = Helper.tableDragId index

    let tryParseTableDragId (i: string) =
        if i.StartsWith Helper.TableDragIdPrefix then
            match System.Int32.TryParse(i.Substring(Helper.TableDragIdPrefix.Length)) with
            | true, index -> Some index
            | _ -> None
        else
            None

    let tryGetDndEventId (eventNode: obj) =
        if isNull eventNode then
            None
        else
            let idObj: obj = eventNode?``id``

            if isNull idObj then None else Some(string idObj)

    let dndObjectProps (obj: Swate.Components.JsBindings.IObject) = [
        for key in Swate.Components.JsBindings.Object.keys obj do
            prop.custom (key, obj.get key)
    ]

    type prop with
        static member dataFooterTabId(arctiveView: ActiveView) : IReactProperty =
            match arctiveView with
            | ActiveView.DataMap -> "datamap"
            | ActiveView.Metadata -> "metadata"
            | ActiveView.Table index -> mkTableDragId index
            |> fun v -> unbox ($"data-{FooterTabIdDataKey}", v)

    let tryParseDataFooterTabId (i: string) =
        match i with
        | "metadata" -> Some ActiveView.Metadata
        | "datamap" -> Some ActiveView.DataMap
        | _ -> tryParseTableDragId i |> Option.map ActiveView.Table

    module ContextMenu =

        let onSpawn (elementRef: IRefValue<option<Browser.Types.HTMLElement>>) =
            (fun (e: Browser.Types.MouseEvent) ->
                let target = e.target :?> Browser.Types.HTMLElement

                match target.closest ($"[data-{FooterTabIdDataKey}]"), elementRef.current with
                | Some cell, Some container when container.contains (cell) ->
                    let cell = cell :?> Browser.Types.HTMLElement
                    let tabId: string = cell?dataset?footertabid
                    unbox (tryParseDataFooterTabId tabId)
                | _ -> None
            )

open ArcFileFooterTabsHelper

type private TableTabViewModel = {
    index: int
    tableName: string
    isEditorMode: bool
    isActive: bool
}

[<Erase; Mangle(false)>]
type ArcFileFooterTabs =

    static member private BaseTab
        (
            children: ReactElement,
            onClick: Browser.Types.MouseEvent -> unit,
            ?iconClass: string,
            ?isActive: bool,
            ?key,
            ?extraProps: IReactProperty list,
            // This arg is used to get tab information for context menu
            ?activeView: ActiveView
        ) =
        Html.button [
            if extraProps.IsSome then
                yield! extraProps.Value
            if activeView.IsSome then
                prop.dataFooterTabId activeView.Value
            prop.className [
                "swt:tab swt:items-center swt:min-w-fit"
                match isActive with
                | Some true -> "swt:tab-active"
                | _ -> ()
            ]
            prop.onClick onClick
            prop.children [
                if iconClass.IsSome then
                    Html.i [ prop.className [ iconClass.Value; "swt:min-w-4" ] ]
                children
            ]
        ]

    [<ReactMemoComponent(AreEqualFn.FsEqualsButFunctions)>]
    static member private TableTab
        (index: int, tableName: string, onClick, isActive: bool, ?onDoubleClick, ?key: string)
        =

        let sortable = DndKit.useSortable ({| id = mkTableDragId index |})

        let style = [
            style.custom ("transform", DndKit.CSS.Transform.toString sortable.transform)
            style.custom ("transition", sortable.transition)
            style.cursor.grab
        ]

        let dndProps = [
            prop.id (mkTableDragId index)
            prop.ref sortable.setNodeRef
            prop.style style
        ]

        let dndProps =
            dndProps
            @ dndObjectProps sortable.attributes
            @ dndObjectProps sortable.listeners
            @ [
                prop.onDoubleClick (fun e ->
                    match onDoubleClick with
                    | Some handler -> handler e
                    | None -> ()
                )
            ]

        ArcFileFooterTabs.BaseTab(
            Html.span [ prop.text tableName ],
            onClick,
            "swt:iconify swt:fluent--table-24-regular",
            isActive = isActive,
            activeView = ActiveView.Table index,
            extraProps = dndProps,
            ?key = key
        )

    [<ReactMemoComponent(AreEqualFn.FsEqualsButFunctions)>]
    static member private EditTableNameTab
        (
            currentName: string,
            otherTableNames: string[],
            onNameChange: string -> unit,
            closeEdit: unit -> unit,
            isActive: bool
        ) =
        let localInput, setLocalInput = React.useState currentName

        let error =
            if localInput.Trim() = currentName then
                None
            else
                tryTableNameError otherTableNames localInput

        let submit =
            fun (newName: string) ->
                let trimmed = newName.Trim()

                match error.IsSome || trimmed = currentName with
                | true -> ()
                | _ -> onNameChange trimmed

        ArcFileFooterTabs.BaseTab(
            Html.div [
                prop.className "swt:flex swt:items-center swt:gap-2"
                prop.children [
                    Html.input [
                        prop.autoFocus true
                        prop.className [
                            "swt:input swt:input-ghost swt:input-xs swt:w-full swt:max-w-xs"
                            if error.IsSome then
                                "swt:input-error"
                        ]
                        prop.value localInput
                        prop.onChange (fun (e: string) -> setLocalInput e)
                        prop.onKeyDown (fun (e: Browser.Types.KeyboardEvent) ->
                            match e.code with
                            | kbdEventCode.enter ->
                                e.preventDefault ()
                                submit localInput
                            | kbdEventCode.escape ->
                                e.preventDefault ()
                                setLocalInput currentName
                                closeEdit ()
                            | _ -> ()
                        )
                        prop.onBlur (fun _ -> closeEdit ())
                    ]
                    match error with
                    | Some message ->
                        Html.span [
                            prop.role.alert
                            prop.className
                                "swt:flex swt:items-center swt:gap-1 swt:text-xs swt:text-error swt:whitespace-nowrap"
                            prop.children [
                                Html.i [
                                    prop.className "swt:iconify swt:fluent--warning-12-filled"
                                ]
                                Html.span message
                            ]
                        ]
                    | None -> Html.none
                ]
            ],
            ignore,
            isActive = isActive
        )

    [<ReactMemoComponent(AreEqualFn.FsEqualsButFunctions)>]
    static member private MetadataTab(label: string, onClick, isActive: bool) =
        ArcFileFooterTabs.BaseTab(
            Html.span label,
            onClick,
            "swt:iconify swt:fluent--info-20-filled",
            isActive = isActive,
            activeView = ActiveView.Metadata,
            key = "MetadataTab"
        )

    [<ReactMemoComponent(AreEqualFn.FsEqualsButFunctions)>]
    static member private DataMapTab(onClick, isActive: bool) =

        ArcFileFooterTabs.BaseTab(
            Html.span "DataMap",
            onClick,
            "swt:iconify swt:fluent--map-16-regular",
            isActive = isActive,
            activeView = ActiveView.DataMap,
            key = "DataMapTab"
        )

    [<ReactComponent>]
    static member private ContextMenu
        (
            elementRef: IRefValue<option<Browser.Types.HTMLElement>>,
            setEditorMode: int option -> unit,
            deleteTable: int -> unit,
            onDeleteDataMap: (unit -> unit) option
        ) =


        let delete (tableIndex: int) = fun _ -> deleteTable tableIndex

        let rename (tableIndex: int) =
            fun _ -> setEditorMode (Some tableIndex)

        let children =
            fun e ->
                let activeView = unbox<ActiveView option> e

                match activeView with
                | Some(ActiveView.Table index) -> [
                    ContextMenuItem(
                        Html.span "Rename Table",
                        icon =
                            Html.i [
                                prop.className "swt:iconify swt:fluent--slide-text-title-edit-20-regular swt:size-4"
                            ],
                        onClick = rename index
                    )
                    ContextMenuItem(
                        Html.span "Delete Table",
                        icon =
                            Html.i [
                                prop.className "swt:iconify swt:fluent--delete-20-filled swt:size-4"
                            ],
                        onClick = delete index
                    )
                  ]
                | Some ActiveView.DataMap ->
                    match onDeleteDataMap with
                    | Some deleteDataMap -> [
                        ContextMenuItem(
                            Html.span "Delete DataMap",
                            icon =
                                Html.i [
                                    prop.className "swt:iconify swt:fluent--delete-20-filled swt:size-4"
                                ],
                            onClick = (fun _ -> deleteDataMap ())
                        )
                      ]
                    | None -> []
                | _ -> []


        Swate.Components.Primitive.ContextMenu.ContextMenu.ContextMenu(
            children,
            ref = elementRef,
            onSpawn = ContextMenu.onSpawn elementRef
        )

    [<ReactComponent>]
    static member private DragAndDropContainer(tableIds: ResizeArray<string>, handleDragEnd, children: ReactElement) =

        let pointerSensor =
            DndKit.useSensor (
                DndKit.PointerSensor,
                {|
                    activationConstraint = {| distance = 8 |}
                |}
            )

        let sensors = DndKit.useSensors [| pointerSensor |]

        DndKit.DndContext(
            sensors = sensors,
            onDragEnd = handleDragEnd,
            collisionDetection = DndKit.closestCenter,
            children =
                DndKit.SortableContext(
                    items = tableIds,
                    strategy = DndKit.horizontalListSortingStrategy,
                    children = children
                )
        )

    [<ReactComponent>]
    static member Main
        (
            arcFile: ArcFiles,
            activeView: ActiveView,
            setActiveView: ActiveView -> unit,
            setArcFile: ArcFiles -> unit,
            onDeleteDataMap: (unit -> unit) option
        ) =
        let tables = arcFile.ArcTables()
        let canAddTable = arcFile.CanCreateTables()
        let canRenderDataMap = arcFile.CanRenderDataMapView()
        let tabsRef = React.useElementRef ()

        let updateArcFile update =
            let nextArcFile = ArcFiles.refreshRef arcFile
            let result = update nextArcFile
            setArcFile nextArcFile
            result

        let isEditorModeTableTab, setIsEditorModeTableTab =
            React.useState (None: int option)

        let metadataTabLabel =
            match arcFile with
            | ArcFiles.Assay _ -> "Assay"
            | ArcFiles.Study _ -> "Study"
            | ArcFiles.Investigation _ -> "Investigation"
            | ArcFiles.Run _ -> "Run"
            | ArcFiles.Workflow _ -> "Workflow"
            | ArcFiles.Template _ -> "Template"
            | ArcFiles.DataMap _ -> "Datamap"

        let setEditorMode =
            React.useCallback (
                (fun (nextMode: int option) -> setIsEditorModeTableTab nextMode),
                [| box setIsEditorModeTableTab |]
            )

        let closeEditorMode =
            React.useCallback ((fun () -> setIsEditorModeTableTab None), [| box setIsEditorModeTableTab |])

        let activateMetadataView =
            React.useCallback ((fun _ -> setActiveView ActiveView.Metadata), [| box setActiveView |])

        let activateDataMapView =
            React.useCallback ((fun _ -> setActiveView ActiveView.DataMap), [| box setActiveView |])

        let activateTableView =
            React.useCallback (
                (fun (tableIndex: int) -> setActiveView (ActiveView.Table tableIndex)),
                [| box setActiveView |]
            )

        let openTableNameEditor =
            React.useCallback (
                (fun (tableIndex: int) -> setIsEditorModeTableTab (Some tableIndex)),
                [| box setIsEditorModeTableTab |]
            )

        let tableNamesKey =
            tables |> Seq.map (fun table -> table.Name) |> String.concat "||"

        let addNewTable =
            fun _ ->
                closeEditorMode ()

                if canAddTable then
                    let nextTableIndex =
                        updateArcFile (fun nextArcFile ->
                            let nextTables = nextArcFile.ArcTables()
                            let nextName = createNewTableName nextTables.Tables
                            nextTables.AddTable(ArcTable.init nextName)
                            nextTables.TableCount - 1
                        )

                    setActiveView (ActiveView.Table nextTableIndex)

        let deleteTable (tableIndex: int) =
            closeEditorMode ()

            updateArcFile (fun nextArcFile -> nextArcFile.ArcTables().RemoveTableAt tableIndex)

            match activeView with
            | ActiveView.Table i when i = tableIndex -> setActiveView ActiveView.Metadata
            | ActiveView.Table i when i > tableIndex -> setActiveView (ActiveView.Table(i - 1))
            | _ -> ()

        let updateTableOrder (oldIndex: int, newIndex: int) =
            closeEditorMode ()

            updateArcFile (fun nextArcFile -> nextArcFile.ArcTables().MoveTable(oldIndex, newIndex))

            let nextActiveIndex = max 0 (min newIndex (tables.TableCount - 1))
            setActiveView (ActiveView.Table nextActiveIndex)

        let handleDragEnd =
            React.useCallback (
                (fun (event: DndKit.IDndKitEvent) ->

                    if isEditorModeTableTab.IsSome then
                        ()
                    else
                        match tryGetDndEventId (box event.active), tryGetDndEventId (box event.over) with
                        | Some activeId, Some overId when activeId <> overId ->
                            match tryParseTableDragId activeId, tryParseTableDragId overId with
                            | Some oldIndex, Some newIndex when oldIndex <> newIndex ->
                                updateTableOrder (oldIndex, newIndex)
                            | _ -> ()
                        | _ -> ()
                ),
                [| box isEditorModeTableTab; box updateTableOrder |]
            )


        let tableIds =
            React.useMemo ((fun () -> Helper.tableDragIds tables.TableCount), [| box tables.TableCount |])

        let tableTabModels: TableTabViewModel[] =
            React.useMemo (
                (fun () -> [|
                    for index = 0 to tables.TableCount - 1 do
                        {
                            index = index
                            tableName = tables.[index].Name
                            isEditorMode = isEditorModeTableTab = Some index
                            isActive = activeView = ActiveView.Table index
                        }
                |]),
                [|
                    box tables.TableCount
                    box tableNamesKey
                    box isEditorModeTableTab
                    box activeView
                |]
            )

        let otherTableNames (tableIndex: int) =
            arcFile.ArcTables().TableNames
            |> Seq.indexed
            |> Seq.filter (fun (index, _) -> index <> tableIndex)
            |> Seq.map snd
            |> Array.ofSeq

        let renameTable =
            React.useCallback (
                (fun (tableIndex: int) (newName: string) ->

                    let currentTables = arcFile.ArcTables()
                    let newName = newName.Trim()

                    match
                        newName = currentTables.[tableIndex].Name
                        || (tryTableNameError (otherTableNames tableIndex) newName).IsSome
                    with
                    | true -> ()
                    | false ->
                        updateArcFile (fun nextArcFile -> nextArcFile.ArcTables().RenameTableAt(tableIndex, newName))
                        closeEditorMode ()
                ),
                [|
                    box arcFile
                    box setArcFile
                    box closeEditorMode
                    box tableNamesKey
                |]
            )

        React.Fragment [
            ArcFileFooterTabs.ContextMenu(tabsRef, setEditorMode, deleteTable, onDeleteDataMap)
            Html.div [
                prop.className "swt:bg-base-300"
                prop.children [

                    Html.div [
                        prop.ref tabsRef
                        prop.className
                            "swt:*:[--tab-border-color:var(--color-base-content)] swt:tabs swt:tabs-lift swt:w-full \
                                swt:overflow-x-auto swt:overflow-y-hidden swt:flex swt:flex-row swt:items-center \
                                swt:justify-start swt:pt-1 swt:*:border-b-0! swt:*:gap-1 swt:flex-nowrap swt:*:flex-nowrap"
                        prop.children [

                            Html.div [ // This element is a spacer to create some whitespace between the tabs and the left edge of the container.
                                prop.className "swt:tab swt:max-w-min swt:px-2!"
                                prop.style [ style.custom ("order", -99) ]
                            ]

                            ArcFileFooterTabs.MetadataTab(
                                metadataTabLabel,
                                activateMetadataView,
                                isActive = (activeView = ActiveView.Metadata)
                            )

                            if canRenderDataMap then
                                ArcFileFooterTabs.DataMapTab(
                                    activateDataMapView,
                                    isActive = (activeView = ActiveView.DataMap)
                                )

                            ArcFileFooterTabs.DragAndDropContainer(
                                tableIds,
                                handleDragEnd,
                                children =
                                    React.Fragment [

                                        for tableModel in tableTabModels do

                                            match tableModel.isEditorMode with
                                            | true ->
                                                ArcFileFooterTabs.EditTableNameTab(
                                                    tableModel.tableName,
                                                    otherTableNames tableModel.index,
                                                    renameTable tableModel.index,
                                                    closeEditorMode,
                                                    isActive = tableModel.isActive
                                                )
                                            | false ->
                                                ArcFileFooterTabs.TableTab(
                                                    tableModel.index,
                                                    tableModel.tableName,
                                                    (fun _ -> activateTableView tableModel.index),
                                                    isActive = tableModel.isActive,
                                                    onDoubleClick = (fun _ -> openTableNameEditor tableModel.index),
                                                    key = tableModel.tableName
                                                )

                                    ]
                            )


                            if canAddTable then
                                ArcFileFooterTabs.BaseTab(
                                    Html.none,
                                    addNewTable,
                                    "swt:iconify swt:fluent--add-12-filled",
                                    extraProps = [ prop.ariaLabel "Add new table" ]
                                )

                            Html.div [ // This element is a spacer to create some whitespace between the tabs and the right edge of the container.
                                prop.className "swt:tab swt:max-w-min swt:px-2!"
                                prop.style [ style.custom ("order", System.Int32.MaxValue) ]
                            ]
                        ]
                    ]
                ]
            ]
        ]
