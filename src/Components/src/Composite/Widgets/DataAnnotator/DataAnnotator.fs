namespace Swate.Components.Composite.Widgets.DataAnnotator

open System
open ARCtrl
open Fable.Core
open Feliz
open Swate.Components
open Swate.Components.Shared
open Swate.Components.Primitive.BaseModal
open Swate.Components.Composite.Table
open Swate.Components.Composite.Table.Types
open Swate.Components.Primitive.Dropdown
open Swate.Components.Composite.Widgets.Context
open Swate.Components.Composite.Widgets.DataAnnotator.Types
open Swate.Components.Composite.Widgets.DataAnnotator.Helper
open Swate.Components.Composite.SortableList
open Swate.Components.Composite.SortableList.Types

[<Erase; Mangle(false)>]
type DataAnnotator =

    [<ReactComponent>]
    static member private FileMetadataComponent(file: DataFile) =
        Html.p [
            Html.strong file.DataFileName
            Html.text " - "
            Html.strong file.DataFileType
        ]

    [<ReactComponent>]
    static member private InfoText() =
        Primitive.Popover.Popover.Simple(
            Html.button [
                prop.className "swt:btn swt:btn-square swt:btn-ghost"
                prop.children [
                    Html.i [
                        prop.className "swt:iconify swt:fluent--info-24-regular swt:text-info swt:size-6"
                    ]
                ]
            ],
            Html.div [
                Html.i [
                    prop.className "swt:iconify swt:fluent--info-24-regular swt:text-info swt:size-6"
                ]
                Html.span [
                    Html.p "Load a CSV or TSV file to preview its contents as a selectable table."
                    Html.p [
                        prop.className "swt:text-xs swt:text-base-content/50"
                        prop.text
                            "Select columns, rows, or cells in the preview and add their selectors to the list. Select destination cells in your table, then insert all selectors or only the highlighted list entries."
                    ]
                ]
            ]
        )

    [<ReactComponent>]
    static member private UploadButton(uploadFile: Browser.Types.File -> unit) =

        Html.input [
            prop.type'.file
            prop.ariaLabel "Upload a CSV or TSV file to preview its contents as a selectable table."
            prop.className "swt:file-input swt:file-input-primary swt:grow"
            prop.onChange uploadFile
        ]

    [<ReactComponent>]
    static member private OpenAnnotatorTableModal(fileIsUploaded: bool, isLoading: bool, openModal: unit -> unit) =

        let isDisabled = not fileIsUploaded || isLoading

        Html.button [
            prop.className "swt:btn swt:btn-primary swt:grow"
            prop.disabled isDisabled
            if isLoading then
                prop.children [
                    Html.span [ prop.className "swt:loading swt:loading-spinner" ]
                ]
            elif not fileIsUploaded then
                prop.text "Upload a file to select targets"
            else
                prop.text "Preview and Select Targets"
                prop.onClick (fun _ -> openModal ())
        ]

    [<ReactMemoComponent(AreEqualFn.FsEqualsButFunctions)>]
    static member private TableCellButton
        (
            rowIndex: int,
            columnIndex: int,
            content: string,
            dtrgt: DataTarget option,
            isDirectlyActive: bool,
            isActive: bool,
            toggleTarget: DataTarget -> unit
        ) =
        TableCell.BaseCell(
            rowIndex,
            columnIndex,
            (match dtrgt with
             | Some dtrgt ->
                 Html.div [
                     prop.className "swt:w-full swt:h-full swt:flex swt:items-center swt:px-2 swt:py-1 swt:truncate"
                     prop.onClick (fun _ -> toggleTarget dtrgt)
                     prop.children [
                         if isDirectlyActive then
                             Html.div [
                                 prop.className "swt:absolute swt:top-0 swt:right-0 swt:has-text-success swt:m-0"
                                 prop.children [ Primitive.Icons.SquarePlus() ]
                             ]
                         Html.text content
                     ]
                 ]
             | None -> Html.p "-"),
            className =
                String.concat " " [
                    "swt:w-full swt:h-full"
                    if isDirectlyActive || isActive then
                        "swt:bg-primary swt:text-primary-content"
                ]
        )

    [<ReactComponent>]
    static member private Table
        (
            file: ParsedDataFile,
            state: Set<DataTarget>,
            setState: (Set<DataTarget> -> Set<DataTarget>) -> unit,
            isLoading: bool
        ) =
        let toggleTarget =
            React.useCallback (
                (fun (target: DataTarget) ->
                    setState (fun (currentState: Set<DataTarget>) ->
                        if currentState.Contains target then
                            currentState.Remove target
                        else
                            currentState.Add target
                    )
                ),
                [| box setState |]
            )

        let selectedRows, selectedColumns, selectedCells: Set<int> * Set<int> * Set<int * int> =
            React.useMemo (
                (fun () ->
                    state
                    |> Seq.fold
                        (fun (rows, cols, cells) target ->
                            match target with
                            | DataTarget.Row rowIndex -> (rows.Add rowIndex, cols, cells)
                            | DataTarget.Column columnIndex -> (rows, cols.Add columnIndex, cells)
                            | DataTarget.Cell(columnIndex, rowIndex) ->
                                (rows, cols, cells.Add((columnIndex, rowIndex)))
                        )
                        (Set.empty<int>, Set.empty<int>, Set.empty<int * int>)
                ),
                [| box state |]
            )

        let hasHeader = file.HeaderRow.IsSome

        let bodyMaxColumnCount =
            React.useMemo (
                (fun () -> file.BodyRows |> Array.fold (fun count row -> max count row.Length) 0),
                [| box file |]
            )

        let headerColumnCount =
            file.HeaderRow |> Option.map _.Length |> Option.defaultValue 0

        let bodyRowCount = file.BodyRows.Length
        let rowCount = max 1 (bodyRowCount + if hasHeader then 1 else 0)
        let columnCount = max 1 (max headerColumnCount bodyMaxColumnCount + 1)

        let tableRef = React.useRef<TableHandle> (null)

        let getBodyRowIndex (virtualRowIndex: int) =
            if hasHeader then virtualRowIndex - 1 else virtualRowIndex

        let mkCell (rowIndex: int) (columnIndex: int) (content: string) (target: DataTarget option) =
            let isDirectlyActive =
                match target with
                | Some(DataTarget.Row targetRowIndex) -> selectedRows.Contains targetRowIndex
                | Some(DataTarget.Column targetColumnIndex) -> selectedColumns.Contains targetColumnIndex
                | Some(DataTarget.Cell(targetColumnIndex, targetRowIndex)) ->
                    selectedCells.Contains((targetColumnIndex, targetRowIndex))
                | None -> false

            let isInheritedActive =
                match target with
                | Some(DataTarget.Cell(targetColumnIndex, targetRowIndex)) ->
                    selectedColumns.Contains targetColumnIndex
                    || selectedRows.Contains targetRowIndex
                | _ -> false

            DataAnnotator.TableCellButton(
                rowIndex,
                columnIndex,
                content,
                target,
                isDirectlyActive,
                isDirectlyActive || isInheritedActive,
                toggleTarget
            )

        Html.div [
            prop.className "swt:relative swt:overflow-hidden swt:grid swt:grid-cols-1 swt:grid-rows swt:h-[80%]"
            prop.children [
                Table.Table(
                    rowCount,
                    columnCount,
                    (fun index ->
                        if index.x = 0 then
                            if hasHeader && index.y = 0 then
                                mkCell index.y index.x "" None
                            else
                                let bodyRowIndex = getBodyRowIndex index.y

                                if bodyRowIndex < 0 || bodyRowIndex >= bodyRowCount then
                                    mkCell index.y index.x "" None
                                else
                                    let content =
                                        if hasHeader then
                                            string (bodyRowIndex + 1)
                                        else
                                            string bodyRowIndex

                                    mkCell index.y index.x content (Some(DataTarget.Row bodyRowIndex))
                        elif hasHeader && index.y = 0 then
                            let dataColumnIndex = index.x - 1

                            let headerValue =
                                file.HeaderRow |> Option.bind (fun row -> row |> Array.tryItem dataColumnIndex)

                            match headerValue with
                            | Some value -> mkCell index.y index.x value (Some(DataTarget.Column dataColumnIndex))
                            | None -> mkCell index.y index.x "" None
                        else
                            let bodyRowIndex = getBodyRowIndex index.y
                            let dataColumnIndex = index.x - 1

                            if bodyRowIndex < 0 || bodyRowIndex >= bodyRowCount then
                                mkCell index.y index.x "" None
                            else
                                let cellValue =
                                    file.BodyRows
                                    |> Array.tryItem bodyRowIndex
                                    |> Option.bind (fun row -> row |> Array.tryItem dataColumnIndex)

                                match cellValue with
                                | Some value ->
                                    mkCell index.y index.x value (Some(DataTarget.Cell(dataColumnIndex, bodyRowIndex)))
                                | None -> mkCell index.y index.x "" None

                    ),
                    (fun _ -> Html.div []),
                    tableRef
                )
                if isLoading then
                    Html.div [
                        prop.className
                            "swt:absolute swt:inset-0 swt:flex swt:items-center swt:justify-center swt:bg-base-100/60 swt:z-10"
                        prop.children [
                            Primitive.LoadingSpinner.LoadingSpinner.LoadingSpinner(
                                size = Primitive.Types.DaisyuiSize.XL
                            )
                        ]
                    ]
            ]
        ]

    [<ReactComponent>]
    static member private UpdateSeparatorButton(dataFileParseConfig: DataFileParseConfig, setDataFileParseConfig) =

        let input, setInput = React.useState (dataFileParseConfig.Separator)
        let hasError = String.IsNullOrEmpty input

        let applySeperatorInputToConfig () =
            if not hasError then
                setDataFileParseConfig {
                    dataFileParseConfig with
                        Separator = input
                }
            else
                console.warn "Cannot set separator to an empty string."

        let PresetButton (text: string, separator: string) =
            Html.button [
                prop.className "swt:btn swt:join-item"
                prop.text text
                prop.onClick (fun _ -> setInput separator)
            ]

        Html.div [
            prop.className "swt:join"
            prop.children [
                PresetButton("Tab (\\t)", "\\t")
                PresetButton(",", ",")
                PresetButton(";", ";")
                PresetButton("|", "|")
                Html.input [
                    prop.className "swt:input swt:join-item"
                    prop.placeholder ".. update separator"
                    prop.value input
                    prop.onChange (fun s -> setInput s)
                    prop.onKeyDown (key.enter, fun _ -> applySeperatorInputToConfig ())
                ]
                Html.button [
                    prop.className "swt:btn swt:join-item"
                    prop.text "Update"
                    prop.disabled hasError
                    prop.onClick (fun _ -> applySeperatorInputToConfig ())
                ]
            ]
        ]

    [<ReactComponent>]
    static member private UpdateIsHeaderCheckbox(dataFileParseConfig: DataFileParseConfig, setDataFileParseConfig) =

        Html.button [
            if dataFileParseConfig.HasHeader then
                prop.className "swt:btn swt:btn-primary"
            else
                prop.className "swt:btn"
            prop.onClick (fun _ ->
                setDataFileParseConfig {
                    dataFileParseConfig with
                        HasHeader = not dataFileParseConfig.HasHeader
                }
            )
            prop.children [
                Html.p [
                    if not dataFileParseConfig.HasHeader then
                        prop.className "swt:line-through"
                    prop.text "Has Header"
                ]
            ]
        ]

    [<ReactComponent>]
    static member private DataFileConfigComponent
        (dataFileParseConfig: DataFileParseConfig, setDataFileParseConfig: (DataFileParseConfig -> unit))
        =
        Html.div [
            prop.className "swt:flex swt:flex-row swt:gap-4"
            prop.children [
                DataAnnotator.UpdateSeparatorButton(dataFileParseConfig, setDataFileParseConfig)
                DataAnnotator.UpdateIsHeaderCheckbox(dataFileParseConfig, setDataFileParseConfig)
            ]
        ]

    [<ReactComponent>]
    static member private Modal
        (
            dataFile: DataFile,
            parsedDataFile: ParsedDataFile,
            dataFileParseConfig,
            setDataFileParseConfig,
            isOpen,
            setIsOpen,
            isLoading,
            submit: AnnotationInput -> unit
        ) =
        let state, setState: Set<DataTarget> * (((Set<DataTarget> -> Set<DataTarget>) -> unit)) =
            React.useStateWithUpdater (Set.empty<DataTarget>)

        let modalActivity =
            Html.div [
                DataAnnotator.DataFileConfigComponent(dataFileParseConfig, setDataFileParseConfig)
                DataAnnotator.FileMetadataComponent dataFile
            ]

        let content = DataAnnotator.Table(parsedDataFile, state, setState, isLoading)

        let footer =
            Html.div [
                prop.className "swt:w-full swt:flex swt:flex-col swt:gap-2"
                prop.children [
                    Html.div [
                        prop.className "swt:w-full swt:flex swt:justify-between swt:items-center swt:gap-2"
                        prop.children [
                            Html.div [
                                prop.className "swt:ml-auto swt:flex swt:gap-2"
                                prop.style [ style.marginLeft length.auto ]
                                prop.children [
                                    Html.button [
                                        prop.className "swt:btn swt:btn-outline"
                                        prop.text "Cancel"
                                        prop.onClick (fun _ -> setIsOpen false)
                                    ]
                                    Html.button [
                                        prop.className "swt:btn swt:btn-primary"
                                        prop.text "Add selectors"
                                        prop.disabled state.IsEmpty
                                        prop.onClick (fun _ ->
                                            let selectors = selectorsFromTargets parsedDataFile.HeaderRow.IsSome state

                                            let name = dataFile.DataFileName
                                            let dt = dataFile.DataFileType

                                            let input: AnnotationInput = {
                                                Selectors = selectors
                                                FileName = name
                                                FileType = dt
                                            }

                                            submit input
                                        )
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]

        BaseModal.Modal(
            isOpen,
            setIsOpen,
            Html.p "Data Annotator",
            content,
            modalActions = modalActivity,
            footer = footer,
            className = "swt:max-w-none",
            modalActionsClassName = "swt:z-9999"
        )

    [<ReactComponent(true)>]
    static member Main(?onInsert: AnnotationInput -> Result<int, string>, ?canInsert: bool, ?onError: string -> unit) =

        let isLoading, setIsLoading = React.useState false
        let dataFile, setDataFile = React.useState (None: DataFile option)

        let dataFileParseConfig, setDataFileParseConfig =
            React.useState (None: DataFileParseConfig option)

        let annotation, setAnnotation =
            React.useStateWithUpdater (None: AnnotationInput option)

        let selectedSelectors, setSelectedSelectors =
            React.useStateWithUpdater ([]: string list)

        let errorMessage, setErrorMessage = React.useState (None: string option)

        let reportError message =
            setErrorMessage (Some message)
            onError |> Option.iter (fun handler -> handler message)

        let parsedFile =
            React.useMemo (
                (fun () ->
                    match dataFile, dataFileParseConfig with
                    | Some dtf, Some config ->
                        let splitRows = dtf.SplitBySeparator(separator = config.Separator)

                        match config.HasHeader with
                        | true when splitRows.Length > 1 ->
                            let parsed = {
                                HeaderRow = Some splitRows.[0]
                                BodyRows = splitRows.[1..]
                            }

                            Some parsed
                        | _ ->
                            let parsed = {
                                HeaderRow = None
                                BodyRows = splitRows
                            }

                            Some parsed
                    | _ -> None
                ),
                [| box dataFile; box dataFileParseConfig |]
            )

        let showModal, setShowModal = React.useState false

        let handleShowModal =
            fun () ->
                match parsedFile with
                | Some _ -> setShowModal true
                | None -> reportError "No parsed file available to show in the modal."

        let pickFile (file: Browser.Types.File) =
            promise {
                setDataFile None
                setDataFileParseConfig None
                setAnnotation (fun _ -> None)
                setSelectedSelectors (fun _ -> [])
                setErrorMessage None
                setIsLoading true

                try
                    try
                        let! content = file.text ()

                        let name = file.name

                        let loadedDataFile =
                            DataFile.create (name, fileTypeFromName name, content, float file.size)

                        let parseConfig = DataFileParseConfig.createDefault (loadedDataFile)


                        setDataFile (Some loadedDataFile)
                        setDataFileParseConfig (Some parseConfig)
                    with ex ->
                        reportError $"Failed to read file: {ex.Message}"
                finally
                    setIsLoading false
            }
            |> Promise.start

        let setDataFileParseConfig =
            React.useCallback (
                (fun (config: DataFileParseConfig) -> setDataFileParseConfig (Some config)),
                [| box setDataFileParseConfig |]
            )

        let items: SortableListItem<string>[] =
            React.useMemo (
                (fun () ->
                    annotation
                    |> Option.map (fun ann ->
                        ann.Selectors
                        |> Array.map (fun selector -> SortableListItem.create (selector, selector, Some selector))
                    )
                    |> Option.defaultValue [||]
                ),
                [| box annotation |]
            )

        let setSelectors selectors =
            setAnnotation (Option.map (fun ann -> { ann with Selectors = selectors }))
            setSelectedSelectors (List.filter (fun selector -> Array.contains selector selectors))

        let addAnnotation next =
            setAnnotation (fun current -> Some(appendAnnotation current next))
            setErrorMessage None
            setShowModal false

        let insertSelectors () =
            match annotation, onInsert with
            | Some ann, Some insert ->
                let selectors =
                    if List.isEmpty selectedSelectors then
                        ann.Selectors
                    else
                        ann.Selectors
                        |> Array.filter (fun selector -> List.contains selector selectedSelectors)

                match insert { ann with Selectors = selectors } with
                | Ok _ -> setErrorMessage None
                | Error message -> reportError message
            | _ -> ()

        let hasSelection = not (List.isEmpty selectedSelectors)
        let hasInsertionTarget = defaultArg canInsert true

        let canInsert =
            hasInsertionTarget && onInsert.IsSome && items.Length > 0 && not isLoading

        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-4 swt:grow"
            prop.children [

                Html.div [
                    prop.className "swt:flex swt:flex-row swt:gap-2"
                    prop.children [
                        DataAnnotator.UploadButton(pickFile)
                        DataAnnotator.OpenAnnotatorTableModal(dataFile.IsSome, isLoading, handleShowModal)
                        DataAnnotator.InfoText()
                    ]
                ]

                // Selector Modal
                match dataFile, parsedFile, dataFileParseConfig, showModal with
                | Some df, Some pdf, Some dfpc, true ->
                    DataAnnotator.Modal(
                        df,
                        pdf,
                        dfpc,
                        setDataFileParseConfig,
                        showModal,
                        setShowModal,
                        isLoading,
                        addAnnotation
                    )
                | _, _, _, _ -> Html.none

                // Sortable List for annotations
                if items.Length > 0 then
                    Html.div [
                        prop.className "swt:join"
                        prop.children [
                            Html.button [
                                prop.className "swt:btn swt:btn-sm swt:join-item"
                                prop.title "Sort selectors ascending"
                                prop.onClick (fun _ -> items |> Array.map _.id |> Array.sort |> setSelectors)
                                prop.children [ Primitive.Icons.ArrowDownAZ() ]
                            ]
                            Html.button [
                                prop.className "swt:btn swt:btn-sm swt:join-item"
                                prop.title "Sort selectors descending"
                                prop.onClick (fun _ -> items |> Array.map _.id |> Array.sortDescending |> setSelectors)
                                prop.children [ Primitive.Icons.ArrowDownZA() ]
                            ]
                        ]
                    ]

                Html.div [
                    prop.className "swt:max-h-[45vh] swt:overflow-auto"
                    prop.children [
                        SortableList.SortableList(
                            items,
                            (fun nextItems -> nextItems |> Array.map _.id |> setSelectors),
                            selectedIds = selectedSelectors,
                            setSelectedIds = setSelectedSelectors
                        )
                    ]
                ]

                if items.Length > 0 then
                    Html.div [
                        prop.className "swt:flex swt:gap-2 swt:w-full"
                        prop.children [
                            Html.button [
                                prop.className "swt:btn swt:btn-outline"
                                prop.text "Select more targets"
                                prop.disabled isLoading
                                prop.onClick (fun _ -> handleShowModal ())
                            ]
                            Html.button [
                                prop.className "swt:btn swt:btn-neutral"
                                prop.text (if hasSelection then "Clear Selected" else "Clear")
                                prop.title (
                                    if hasSelection then
                                        "Clear only the selected selectors from the list"
                                    else
                                        "Clear all selectors from the list"
                                )
                                prop.onClick (fun _ ->
                                    if hasSelection then
                                        items
                                        |> Array.map _.id
                                        |> Array.filter (fun selector ->
                                            not (List.contains selector selectedSelectors)
                                        )
                                        |> setSelectors
                                    else
                                        setSelectors [||]
                                )
                            ]
                            Html.div [
                                prop.className "swt:ml-auto"
                                prop.children [
                                    if not hasInsertionTarget then
                                        Html.span [
                                            prop.className "swt:text-sm swt:text-error/70 swt:mr-1"
                                            prop.text "Select table cells"
                                        ]
                                    Html.button [
                                        prop.className "swt:btn swt:btn-primary"
                                        prop.text (
                                            if hasSelection then
                                                "Insert selected"
                                            else
                                                "Insert selectors"
                                        )
                                        prop.title
                                            "Insert selectors into the currently selected table or DataMap cells."
                                        prop.disabled (not canInsert)
                                        prop.onClick (fun _ -> insertSelectors ())
                                    ]
                                ]
                            ]
                        ]
                    ]

                if errorMessage.IsSome then
                    Html.div [
                        prop.role "alert"
                        prop.className "swt:alert swt:alert-error swt:text-sm"
                        prop.text errorMessage.Value
                    ]

            ]

        ]
