namespace Swate.Components.Composite.Widgets

open ARCtrl
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Swate.Components
open Swate.Components.Composite.DataMapTable.ClipboardTarget
open Swate.Components.Composite.SortableList
open Swate.Components.Composite.SortableList.Types
open Swate.Components.Shared
open Swate.Components.Primitive
open Swate.Components.Primitive.Buttons
open Swate.Components.Primitive.LoadingSpinner
open Swate.Components.Composite.AnnotationTable.Context
open Swate.Components.Composite.DataMapTable
open Swate.Components.Composite.DataMapTable.Types
open Swate.Components.Composite.Widgets.Types

/// This context is designed to be used only internally in this file.
module private FilePickerWidgetContext =

    let SelectedPathsCtx =
        React.createContext<StateUpdaterContext<string list>> (unbox null)

    [<Hook>]
    let useSelectedPathsCtx () = React.useContext SelectedPathsCtx

module private FilePickerWidgetHelper =
    let appendPickedPaths (setPaths: (string[] -> string[]) -> unit) =
        fun (paths: string[]) ->
            if paths.Length > 0 then
                setPaths (fun existingPaths ->

                    Array.append existingPaths paths |> Array.distinct
                )

    let insertPathsIntoSelectedCells (arcFile: ArcFiles) setArcFile (paths: string[]) (target: InsertTarget option) =
        let paths = paths |> Array.map (toArcRootRelativeFilePath arcFile)
        let nextArcFile = ArcFiles.refreshRef arcFile

        match target with
        | Some(InsertTarget.Table(tableIndex, selection)) ->
            let nextTable = nextArcFile.TryGetActiveTable(Some tableIndex).Value |> snd
            let columnIndex = selection.xStart
            let mutable rowIndex = selection.yStart

            // GetCellAt also resolves cells that were never stored, for example the Input and
            // Output cells of a freshly imported template row, which TryGetCellAt reports as missing.
            let cellsToInsert = [|
                for path in paths do
                    if columnIndex < nextTable.ColumnCount && rowIndex < nextTable.RowCount then
                        let cell = nextTable.GetCellAt(columnIndex, rowIndex)
                        let nextCell = cell.UpdateMainField path
                        let coordinate: CellCoordinate = {| x = columnIndex; y = rowIndex |}
                        coordinate, nextCell
                        rowIndex <- rowIndex + 1
            |]

            if cellsToInsert.Length = 0 then
                failwith "No valid cells to insert paths into. Please check the selected range and try again."
            else
                nextTable.SetCellsAt cellsToInsert
                setArcFile nextArcFile
        | Some(InsertTarget.DataMap selection) ->
            match nextArcFile.TryGetDataMap() with
            | Some dataMap ->
                let anchor: CellCoordinate = {|
                    x = selection.xStart
                    y = selection.yStart
                |}

                dataMap.PasteTabText(anchor, [| anchor |], String.concat System.Environment.NewLine paths)

                setArcFile nextArcFile
            | None -> ()
        | None -> ()


[<Erase; Mangle(false)>]
type FilePickerWidget =


    [<ReactMemoComponent(AreEqualFn.FsEqualsButFunctions)>]
    static member private Table(paths: string[], setPaths: (string[] -> string[]) -> unit) =
        let selectedPathsCtx = FilePickerWidgetContext.useSelectedPathsCtx ()

        let items: SortableListItem<unit>[] =
            React.useMemo (
                (fun () ->
                    paths
                    |> Array.map (fun path -> {|
                        id = path
                        label = path
                        data = None
                    |})
                ),
                [| box paths |]
            )

        Html.div [
            prop.className "swt:max-h-[45vh] swt:overflow-auto"
            prop.children [
                SortableList.SortableList(
                    items,
                    (fun nextItems -> setPaths (fun _ -> nextItems |> Array.map _.label)),
                    className = "swt:max-h-[45vh]",
                    selectedIds = selectedPathsCtx.state,
                    setSelectedIds = selectedPathsCtx.setStateUpdater
                )
            ]

        ]

    [<ReactMemoComponent(AreEqualFn.FsEqualsButFunctions)>]
    static member private SortPathsButtons(setPaths: (string[] -> string[]) -> unit) =

        let sortAscending = fun () -> setPaths (fun current -> current |> Array.sortBy id)

        let sortDescending =
            fun () -> setPaths (fun current -> current |> Array.sortByDescending id)

        Html.div [
            prop.className "swt:join"
            prop.children [
                Html.button [
                    prop.className "swt:btn swt:btn-sm swt:join-item"
                    prop.onClick (fun _ -> sortAscending ())
                    prop.children [ Icons.ArrowDownAZ() ]
                ]
                Html.button [
                    prop.className "swt:btn swt:btn-sm swt:join-item"
                    prop.onClick (fun _ -> sortDescending ())
                    prop.children [ Icons.ArrowDownZA() ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member private ActionButtonClear(hasSelection: bool, clearAll, clearSelected) =
        let text = if hasSelection then "Clear Selected" else "Clear"

        let title =
            if hasSelection then
                "Clear only the currently selected paths from the list"
            else
                "Clear all file paths from the list"

        let onClick = fun _ -> if hasSelection then clearSelected () else clearAll ()

        Html.button [
            prop.className "swt:btn swt:btn-neutral"
            prop.text text
            prop.title title
            prop.onClick (fun _ -> onClick ())
        ]


    [<ReactComponent>]
    static member private ActionButtonInsert(hasSelection: bool, insertPaths: bool -> unit, tableCellSelected: bool) =
        let text =
            if hasSelection then
                "Insert selected file names"
            else
                "Insert file names"

        let title =
            if hasSelection then
                "Insert only the currently selected file names into the currently selected table or DataMap cells."
            else
                "Insert all file names into the currently selected table or DataMap cells."

        Html.div [
            prop.className "swt:flex swt:items-center swt:ml-auto"
            prop.children [
                if not tableCellSelected then
                    Html.span [
                        prop.className "swt:text-sm swt:text-error/70 swt:mr-1"
                        prop.text "Select table cells"
                    ]
                Html.button [
                    prop.className "swt:btn swt:btn-primary"
                    prop.disabled (not tableCellSelected)
                    prop.text text
                    prop.title title
                    prop.type'.button
                    prop.onClick (fun _ -> insertPaths hasSelection)
                ]
            ]
        ]

    [<ReactComponent>]
    static member private ActionButtons
        (
            setPaths: (string[] -> string[]) -> unit,
            pickPaths: PathPickerKind -> unit,
            insertPaths: bool -> unit,
            tableCellSelected: bool
        ) =
        let selectedPathsCtx = FilePickerWidgetContext.useSelectedPathsCtx ()
        let isPickMenuOpen, setIsPickMenuOpen = React.useState false

        let hasSelection = selectedPathsCtx.state |> List.isEmpty |> not

        let clearAll = fun () -> setPaths (fun _ -> [||])

        let clearSelected =
            fun () ->
                setPaths (fun current ->
                    let selected = selectedPathsCtx.state
                    current |> Array.filter (fun path -> not (List.contains path selected))
                )

                selectedPathsCtx.setStateUpdater (fun _ -> [])

        Html.div [
            prop.className "swt:flex swt:flex-wrap swt:gap-2 swt:w-full"
            prop.children [
                Html.div [
                    prop.className "swt:dropdown"
                    prop.children [
                        Html.button [
                            prop.type'.button
                            prop.className "swt:btn swt:btn-outline swt:gap-2"
                            prop.ariaExpanded isPickMenuOpen
                            prop.onClick (fun _ -> setIsPickMenuOpen (not isPickMenuOpen))
                            prop.children [
                                Html.span "Add paths"
                                Html.i [
                                    prop.className "swt:iconify swt:fluent--chevron-down-20-regular swt:size-4"
                                ]
                            ]
                        ]
                        if isPickMenuOpen then
                            Html.ul [
                                prop.className
                                    "swt:dropdown-content swt:menu swt:bg-base-200 swt:rounded-box swt:z-10 swt:w-52 swt:p-2 swt:shadow-sm"
                                prop.children [
                                    Html.li [
                                        Html.button [
                                            prop.type'.button
                                            prop.onClick (fun _ ->
                                                setIsPickMenuOpen false
                                                pickPaths PathPickerKind.File
                                            )
                                            prop.children [
                                                Html.i [
                                                    prop.className
                                                        "swt:iconify swt:fluent--document-24-regular swt:size-4"
                                                ]
                                                Html.span "Pick files"
                                            ]
                                        ]
                                    ]
                                    Html.li [
                                        Html.button [
                                            prop.type'.button
                                            prop.onClick (fun _ ->
                                                setIsPickMenuOpen false
                                                pickPaths PathPickerKind.Folder
                                            )
                                            prop.children [
                                                Html.i [
                                                    prop.className
                                                        "swt:iconify swt:fluent--folder-24-regular swt:size-4"
                                                ]
                                                Html.span "Pick folders"
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                    ]
                ]
                FilePickerWidget.ActionButtonClear(hasSelection, clearAll, clearSelected)
                FilePickerWidget.ActionButtonInsert(hasSelection, insertPaths, tableCellSelected)
            ]
        ]

    [<ReactComponent>]
    static member private EmptyState
        (
            onPickPaths: PathPickerKind -> unit,
            onDropFiles: File[] -> unit,
            isDropActive: bool,
            setIsDropActive: bool -> unit
        ) =
        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-3"
            prop.children [
                Html.div [
                    prop.testId "file-picker-dropzone"
                    prop.className [
                        "swt:flex swt:min-h-36 swt:flex-col swt:items-center swt:justify-center swt:gap-2 swt:rounded-box swt:border-2 swt:border-dashed swt:p-6 swt:text-center swt:transition-colors"
                        if isDropActive then
                            "swt:border-primary swt:bg-primary/10"
                        else
                            "swt:border-base-300 swt:bg-base-200/50"
                    ]
                    prop.onDragEnter (fun (event: DragEvent) ->
                        event.preventDefault ()
                        event.stopPropagation ()
                        setIsDropActive true
                    )
                    prop.onDragLeave (fun (event: DragEvent) ->
                        event.preventDefault ()
                        event.stopPropagation ()
                        setIsDropActive false
                    )
                    prop.onDragOver (fun (event: DragEvent) ->
                        event.preventDefault ()
                        event.stopPropagation ()
                    )
                    prop.onDrop (fun (event: DragEvent) ->
                        event.preventDefault ()
                        event.stopPropagation ()
                        setIsDropActive false
                        emitJsExpr event.dataTransfer.files "Array.from($0)" |> onDropFiles
                    )
                    prop.children [
                        Html.i [
                            prop.className "swt:iconify swt:fluent--document-arrow-down-24-regular swt:size-8"
                        ]
                        Html.span [
                            prop.className "swt:font-semibold"
                            prop.text "Drop files or folders here"
                        ]
                        Html.span [
                            prop.className "swt:text-sm swt:opacity-70"
                            prop.text "Their ARC-relative paths will be added to the list."
                        ]
                        Html.div [
                            prop.className "swt:flex swt:w-full swt:max-w-sm swt:gap-2 swt:pt-2"
                            prop.children [
                                Html.button [
                                    prop.type'.button
                                    prop.className "swt:btn swt:btn-sm swt:btn-primary swt:flex-1"
                                    prop.onClick (fun _ -> onPickPaths PathPickerKind.File)
                                    prop.children [
                                        Html.i [
                                            prop.className "swt:iconify swt:fluent--document-24-regular swt:size-4"
                                        ]
                                        Html.span "Pick files"
                                    ]
                                ]
                                Html.button [
                                    prop.type'.button
                                    prop.className "swt:btn swt:btn-sm swt:btn-primary swt:flex-1"
                                    prop.onClick (fun _ -> onPickPaths PathPickerKind.Folder)
                                    prop.children [
                                        Html.i [
                                            prop.className "swt:iconify swt:fluent--folder-24-regular swt:size-4"
                                        ]
                                        Html.span "Pick folders"
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent(true)>]
    static member Main
        (
            arcFile: ArcFiles,
            activeTableIndex: int option,
            setArcFile: ArcFiles -> unit,
            onPickPaths: PathPickerKind -> JS.Promise<string[]>,
            ?onDropFiles: File[] -> JS.Promise<string[]>
        ) =

        let paths, setPaths = React.useStateWithUpdater ([||]: string[])
        let selectedPaths, setSelectedPaths = React.useStateWithUpdater ([]: string list)
        let isLoading, setIsLoading = React.useState false
        let isDropActive, setIsDropActive = React.useState false

        let annotationCtx = useAnnotationTableStateCtx ()

        let insertionTarget =
            CellInsertion.tryGetTarget arcFile activeTableIndex annotationCtx.state

        let hasPaths = paths.Length > 0

        let pickPaths kind =
            promise {
                setIsLoading true

                try
                    let! paths = onPickPaths kind
                    FilePickerWidgetHelper.appendPickedPaths setPaths paths
                finally
                    setIsLoading false
            }
            |> Promise.start

        let dropFiles (files: File[]) =
            match onDropFiles with
            | Some resolveFiles when files.Length > 0 ->
                promise {
                    setIsLoading true

                    try
                        let! paths = resolveFiles files
                        FilePickerWidgetHelper.appendPickedPaths setPaths paths
                    finally
                        setIsLoading false
                }
                |> Promise.start
            | _ -> ()

        let insertPaths =
            fun (useSelectedPaths: bool) ->
                let paths =
                    if useSelectedPaths then
                        paths |> Array.filter (fun path -> List.contains path selectedPaths)
                    else
                        paths

                FilePickerWidgetHelper.insertPathsIntoSelectedCells arcFile setArcFile paths insertionTarget

        let selectContextState =
            React.useMemo (
                (fun () -> {
                    state = selectedPaths
                    setStateUpdater = setSelectedPaths
                }),
                [| selectedPaths |]
            )

        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-2 swt:min-w-sm"
            prop.children [
                if isLoading then
                    LoadingSpinner.LoadingSpinner("Loading Paths...", DaisyuiSize.LG)

                else if hasPaths then
                    FilePickerWidgetContext.SelectedPathsCtx.Provider(
                        selectContextState,
                        React.Fragment [
                            FilePickerWidget.SortPathsButtons(setPaths)

                            FilePickerWidget.Table(paths, setPaths)

                            FilePickerWidget.ActionButtons(setPaths, pickPaths, insertPaths, insertionTarget.IsSome)
                        ]
                    )
                else
                    FilePickerWidget.EmptyState(pickPaths, dropFiles, isDropActive, setIsDropActive)
            ]
        ]
