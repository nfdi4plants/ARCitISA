namespace Swate.Components.Page.ArcFileEditor.EmptyTableView

open Feliz
open ARCtrl
open Fable.Core
open Swate.Components
open Swate.Components.Primitive
open Swate.Components.Primitive.CardGrid
open Swate.Components.Composite.Widgets
open Swate.Components.Composite.Widgets.BuildingBlockWidget
open Swate.Components.Shared
open Swate.Components.Page.ArcFileEditor.EmptyTableView.Types

module private MainTypes =
    [<RequireQualifiedAccess>]
    type SetupView =
        | Choices
        | BuildingBlock
        | Templates
        | UtilizeExistingTables

    [<RequireQualifiedAccess>]
    type ArcNodesState =
        | Loading
        | Loaded of ArcNodeOption[]
        | Failed of string

open MainTypes

[<Erase; Mangle(false)>]
type Main =

    [<ReactComponent>]
    static member private InputColumnImportController
        (
            nodesFetchState: ArcNodesState,
            nodeOptions: ArcNodeOption[],
            activeTable: bool,
            importSelected: ArcNodeOption option -> unit
        ) =

        let selectedKey, setSelectedKey = React.useState (None: string option)

        let selectedNode =
            selectedKey
            |> Option.bind (fun key -> nodeOptions |> Array.tryFind (fun node -> node.Key = key))
            |> Option.orElse (Array.tryHead nodeOptions)

        let importedColumn = selectedNode |> Option.bind Helper.importedColumn

        let canImport = activeTable && importedColumn.IsSome

        React.Fragment [
            Html.div [
                prop.className "swt:flex swt:items-center swt:gap-2"
                prop.children [
                    Html.label [
                        prop.className "swt:select swt:min-w-0 swt:w-auto swt:grow"
                        prop.children [
                            Html.span [ prop.className "swt:label"; prop.text "Column:" ]
                            Html.select [
                                prop.testId "empty-table-existing-column-select"
                                prop.value (selectedKey |> Option.defaultValue "")
                                prop.onChange (fun (key: string) -> setSelectedKey (Some key))
                                prop.disabled (nodeOptions.Length = 0)
                                prop.children [
                                    for group, nodes in nodeOptions |> Array.groupBy _.Group do
                                        Html.optgroup [
                                            prop.key group
                                            prop.label group
                                            prop.children [
                                                for node in nodes do
                                                    Html.option [
                                                        prop.testId
                                                            $"empty-table-existing-column-{node.Group}-{node.TableName}-{node.Kind}"
                                                        prop.key node.Key
                                                        prop.value node.Key
                                                        prop.text $"{node.TableName} – {node.Column.Header}"
                                                    ]
                                            ]
                                        ]
                                    if nodesFetchState.IsLoading then
                                        Html.option [
                                            prop.testId "empty-table-existing-column-loading"
                                            prop.text "Loading..."
                                            prop.disabled true
                                        ]
                                ]
                            ]
                            Html.span [
                                prop.testId "empty-table-existing-tables-loading"
                                prop.className [
                                    if not nodesFetchState.IsLoading then
                                        "swt:hidden"
                                    "swt:loading swt:loading-spinner"
                                ]
                            ]
                        ]
                    ]
                    Html.button [
                        prop.testId "empty-table-existing-column-import"
                        prop.type'.button
                        prop.className "swt:btn swt:btn-primary swt:ml-auto"
                        prop.text "Import as input column"
                        prop.disabled (not canImport)
                        prop.onClick (fun _ -> importSelected selectedNode)
                    ]
                ]
            ]


            match nodeOptions with
            // as long as nodeOptions are 0 we do not want to show preview
            | [||] ->
                match nodesFetchState with
                // don't show anything while loading and no local nodes exist
                | ArcNodesState.Loading -> Html.none
                // if loading is done and no nodes exist show blankslate
                | ArcNodesState.Failed _
                | ArcNodesState.Loaded _ ->
                    Blankslate.Blankslate.Blankslate(
                        "These aren't the nodes you're looking for.",
                        "Unable to find existing input or output columns in all available tables.",
                        "swt:iconify swt:fluent--search-info-24-regular",
                        testId = "empty-table-existing-tables-empty"
                    )
            // in any case in which we have nodes to show we want to display the preview
            | _ ->
                let column = importedColumn.Value
                let previewCells = Helper.previewCells column.Cells

                Html.div [
                    prop.className "swt:overflow-auto swt:max-h-64"
                    prop.children [
                        Html.table [
                            prop.testId "empty-table-existing-column-preview"
                            prop.className "swt:table swt:table-sm"
                            prop.children [
                                Html.caption [
                                    prop.className "swt:text-lg swt:font-semibold swt:pb-2 swt:text-left"
                                    prop.text "Preview:"
                                ]
                                Html.thead [
                                    Html.tr [
                                        Html.th [
                                            prop.className "swt:truncate"
                                            prop.text (column.Header.ToString())
                                            prop.title (column.Header.ToString())
                                        ]
                                    ]
                                ]
                                Html.tbody [
                                    if previewCells.Length = 0 then
                                        Html.tr [
                                            Html.td [
                                                prop.className "swt:opacity-70"
                                                prop.text
                                                    "No cells found in the first 10 rows. Check if this column contains data."
                                            ]
                                        ]
                                    else
                                        for cellText in previewCells do
                                            Html.tr [
                                                Html.td [
                                                    prop.className "swt:truncate"
                                                    prop.text cellText
                                                    prop.title cellText
                                                ]
                                            ]
                                ]
                            ]
                        ]
                    ]
                ]
        ]



    /// Lets the user pick an input or output column of a prior table as input column of the active table.
    /// Mount it only while it is open. It loads the ARC nodes once per mount.
    [<ReactComponent>]
    static member private PriorTableSelect
        (
            arcFile: ArcFiles,
            activeTableIndex: int option,
            setArcFile: ArcFiles -> unit,
            back: unit -> unit,
            ?fetchArcNodes: unit -> JS.Promise<ArcNodeTable[]>
        ) =

        let arcNodesState, setArcNodesState =
            React.useState (
                if fetchArcNodes.IsSome then
                    ArcNodesState.Loading
                else
                    ArcNodesState.Loaded [||]
            )

        let requestVersion = React.useRef 0

        let invalidate () =
            requestVersion.current <- requestVersion.current + 1

        let dismiss () =
            invalidate ()
            back ()

        let loadArcNodes () =
            match fetchArcNodes with
            | None -> ()
            | Some fetchArcNodes ->
                invalidate ()
                let version = requestVersion.current
                setArcNodesState ArcNodesState.Loading

                promise {
                    try
                        let! tables = fetchArcNodes ()

                        if requestVersion.current = version then
                            setArcNodesState (ArcNodesState.Loaded(Helper.arcNodeOptions arcFile tables))
                    with error ->
                        if requestVersion.current = version then
                            setArcNodesState (ArcNodesState.Failed error.Message)
                }
                |> Promise.start

        React.useEffectOnce (fun () ->
            loadArcNodes ()
            FsReact.createDisposable invalidate
        )

        let nodeOptions = [|
            yield! Helper.localNodeOptions arcFile activeTableIndex
            match arcNodesState with
            | ArcNodesState.Loaded arcNodes -> yield! arcNodes
            | ArcNodesState.Loading
            | ArcNodesState.Failed _ -> ()
        |]

        let importSelected =
            fun (selectedNode: ArcNodeOption option) ->
                match selectedNode with
                | Some node when Helper.importNodeOption arcFile activeTableIndex node setArcFile -> dismiss ()
                | _ -> ()

        Html.div [
            prop.className "swt:space-y-4"
            prop.children [
                Html.div [
                    prop.className "swt:flex swt:flex-col swt:gap-2"
                    prop.children [
                        match arcNodesState with
                        | ArcNodesState.Failed message ->
                            Html.div [
                                prop.testId "empty-table-existing-tables-error"
                                prop.role.alert
                                prop.className "swt:alert swt:alert-error"
                                prop.children [
                                    Html.div [] // Required to make buttons below align right via daisyui styling
                                    Html.span [
                                        prop.text $"Could not load tables from the ARC: {message}"
                                    ]
                                    Html.div [
                                        Html.button [
                                            prop.testId "empty-table-existing-tables-retry"
                                            prop.type'.button
                                            prop.className "swt:btn swt:btn-sm"
                                            prop.onClick (fun _ -> loadArcNodes ())
                                            prop.text "Retry"
                                        ]
                                    ]

                                ]
                            ]
                        | _ -> Html.none

                        Main.InputColumnImportController(
                            arcNodesState,
                            nodeOptions,
                            activeTableIndex.IsSome,
                            importSelected
                        )
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member private Choices
        (
            arcFile: ArcFiles,
            setArcFile: ArcFiles -> unit,
            activeTableIndex: int option,
            openSetup: SetupView -> unit,
            canFetchArcNodes: bool
        ) =
        let isDisabled = Helper.tryGetActiveTable arcFile activeTableIndex |> Option.isNone

        let canUsePriorTables =
            not isDisabled
            && (canFetchArcNodes
                || not (Helper.localNodeOptions arcFile activeTableIndex |> Array.isEmpty))

        Html.div [
            prop.testId "empty-table-choices"
            prop.className "swt:flex swt:flex-1 swt:min-h-0 swt:items-center swt:justify-center swt:overflow-auto"
            prop.children [
                CardGrid.CardGrid(
                    React.Fragment [
                        CardGrid.CardGridButton(
                            Icons.Templates(),
                            "Start with template!",
                            "Select a full template as a starting point.",
                            (fun _ -> openSetup SetupView.Templates),
                            testId = "empty-table-start-template",
                            disabled = isDisabled
                        )
                        CardGrid.CardGridButton(
                            Icons.BuildingBlock(),
                            "Start from scratch!",
                            "Select a building block as a starting point.",
                            (fun _ -> openSetup SetupView.BuildingBlock),
                            testId = "empty-table-start-scratch",
                            disabled = isDisabled
                        )
                        CardGrid.CardGridButton(
                            Icons.BasicTable(),
                            "Create basic table!",
                            "Create a table with columns: Input, Protocol, Output.",
                            (fun _ -> Helper.createMinimalTable arcFile activeTableIndex setArcFile),
                            disabled = isDisabled
                        )
                        CardGrid.CardGridButton(
                            Icons.OutputColumn(),
                            "Utilize existing tables!",
                            (if canFetchArcNodes then
                                 "Reuse an input or output column of any table in your ARC as new input column."
                             else
                                 "Reuse an input or output column of an existing table as new input column."),
                            (fun _ -> openSetup SetupView.UtilizeExistingTables),
                            testId = "empty-table-use-existing-tables",
                            disabled = not canUsePriorTables
                        )
                    ]
                )
            ]
        ]

    [<ReactComponent>]
    static member private SetupPage
        (
            testId: string,
            title: string,
            description: ReactElement,
            back: unit -> unit,
            children: ReactElement,
            scrollBody: bool
        ) =
        let heading = React.useElementRef ()
        React.useLayoutEffectOnce (fun () -> heading.current |> Option.iter (fun element -> element.focus ()))

        Html.section [
            prop.testId testId
            prop.className "swt:flex swt:flex-1 swt:min-h-0 swt:min-w-0 swt:flex-col swt:gap-3 swt:p-4"
            prop.children [
                Html.div [
                    prop.className "swt:flex swt:shrink-0 swt:flex-wrap swt:items-center swt:gap-3"
                    prop.children [
                        Html.button [
                            prop.testId "empty-table-setup-back"
                            prop.type'.button
                            prop.className "swt:btn swt:btn-ghost swt:btn-sm"
                            prop.onClick (fun _ -> back ())
                            prop.children [
                                Html.i [
                                    prop.className "swt:iconify swt:fluent--arrow-left-20-regular swt:size-4"
                                ]
                                Html.text "Back"
                            ]
                        ]
                        Html.h2 [
                            prop.testId "empty-table-setup-heading"
                            prop.ref heading
                            prop.tabIndex -1
                            prop.className "swt:text-lg swt:font-semibold swt:outline-hidden"
                            prop.text title
                        ]
                    ]
                ]
                Html.p [
                    prop.className "swt:text-sm swt:text-base-content/70"
                    prop.children description
                ]
                Html.div [
                    prop.className [
                        "swt:flex swt:flex-1 swt:min-h-0 swt:min-w-0 swt:flex-col"
                        if scrollBody then
                            "swt:overflow-auto"
                        else
                            "swt:overflow-hidden"
                    ]
                    prop.children children
                ]
            ]
        ]

    [<ReactComponent>]
    static member private Session
        (
            arcFile: ArcFiles,
            setArcFile: ArcFiles -> unit,
            activeTableIndex: int option,
            fetchArcNodes: (unit -> JS.Promise<ArcNodeTable[]>) option
        ) =
        let view, setView = React.useState SetupView.Choices

        let back () = setView SetupView.Choices

        Html.div [
            prop.className "swt:flex swt:flex-1 swt:h-full swt:min-h-0 swt:min-w-0 swt:flex-col"
            prop.children [
                match view with
                | SetupView.Choices ->
                    Main.Choices(arcFile, setArcFile, activeTableIndex, setView, fetchArcNodes.IsSome)
                | SetupView.BuildingBlock ->
                    Main.SetupPage(
                        "empty-table-building-block-page",
                        "Select a building block",
                        Html.text "Choose a building block to add to the new table.",
                        back,
                        BuildingBlockWidget.Main(arcFile, activeTableIndex, setArcFile),
                        scrollBody = true
                    )
                | SetupView.Templates ->
                    Main.SetupPage(
                        "empty-table-templates-page",
                        "Select template(s)",
                        Html.text "Select one or more templates to apply to the new table.",
                        back,
                        TemplateWidget.TemplateWidget(arcFile, activeTableIndex, setArcFile, onImportCompleted = back),
                        scrollBody = false
                    )
                | SetupView.UtilizeExistingTables ->
                    Main.SetupPage(
                        "empty-table-existing-tables-page",
                        "Utilize existing tables",
                        Html.text "Reuse an input or output column of an existing table as the new input column.",
                        back,
                        Main.PriorTableSelect(
                            arcFile,
                            activeTableIndex,
                            setArcFile,
                            back,
                            ?fetchArcNodes = fetchArcNodes
                        ),
                        scrollBody = true
                    )
            ]
        ]

    /// <param name="fetchArcNodes">
    /// Loads tables from the whole ARC. Their input and output columns can be reused as input column.
    /// Without it, only the other tables of the open file are offered. Reject the promise to show an error.
    /// </param>
    [<ReactComponent(true)>]
    static member EmptyTableView
        (
            arcFile: ArcFiles,
            setArcFile: ArcFiles -> unit,
            activeTableIndex: int option,
            ?fetchArcNodes: unit -> JS.Promise<ArcNodeTable[]>
        ) =
        let tableKey =
            Helper.tryGetActiveTable arcFile activeTableIndex
            |> Option.map (fun (index, table) -> $"{index}:{table.Name}")
            |> Option.defaultValue "no-active-table"

        React.KeyedFragment(
            tableKey,
            [
                Main.Session(arcFile, setArcFile, activeTableIndex, fetchArcNodes)
            ]
        )
