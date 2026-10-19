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

module private MainTypes =
    [<RequireQualifiedAccess>]
    type SetupView =
        | Choices
        | BuildingBlock
        | Templates

open MainTypes

[<Erase; Mangle(false)>]
type Main =

    [<ReactComponent>]
    static member private Choices
        (
            arcFile: ArcFiles,
            setArcFile: ArcFiles -> unit,
            activeTableIndex: int option,
            openSetup: SetupView -> unit,
            openPreviousOutput: unit -> unit
        ) =
        let isDisabled = Helper.tryGetActiveTable arcFile activeTableIndex |> Option.isNone

        let canUsePreviousOutput =
            not isDisabled && not (Helper.getOutputTables arcFile |> Array.isEmpty)

        Html.div [
            prop.className "swt:flex swt:flex-1 swt:min-h-0 swt:items-center swt:justify-center swt:overflow-auto"
            prop.children [
                CardGrid.CardGrid(
                    React.Fragment [
                        CardGrid.CardGridButton(
                            Icons.Templates(),
                            "Start with template!",
                            "Select a full template as a starting point.",
                            (fun _ -> openSetup SetupView.Templates),
                            disabled = isDisabled
                        )
                        CardGrid.CardGridButton(
                            Icons.BuildingBlock(),
                            "Start from scratch!",
                            "Select a building block as a starting point.",
                            (fun _ -> openSetup SetupView.BuildingBlock),
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
                            "Utilize prior output!",
                            "Select an output column of one table as new input column.",
                            (fun _ -> openPreviousOutput ()),
                            disabled = not canUsePreviousOutput
                        )
                    ]
                )
            ]
        ]

    [<ReactComponent>]
    static member private SetupPage(title: string, back: unit -> unit, children: ReactElement, scrollBody: bool) =
        let heading = React.useElementRef ()
        React.useLayoutEffectOnce (fun () -> heading.current |> Option.iter (fun element -> element.focus ()))

        Html.section [
            prop.className "swt:flex swt:flex-1 swt:min-h-0 swt:min-w-0 swt:flex-col swt:gap-3 swt:p-4"
            prop.children [
                Html.div [
                    prop.className "swt:flex swt:shrink-0 swt:flex-wrap swt:items-center swt:gap-3"
                    prop.children [
                        Html.button [
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
                            prop.ref heading
                            prop.tabIndex -1
                            prop.className "swt:text-lg swt:font-semibold swt:outline-hidden"
                            prop.text title
                        ]
                    ]
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
    static member private Session(arcFile: ArcFiles, setArcFile: ArcFiles -> unit, activeTableIndex: int option) =
        let view, setView = React.useState SetupView.Choices
        let previousOutputOpen, setPreviousOutputOpen = React.useState false

        let back () = setView SetupView.Choices

        Html.div [
            prop.className "swt:flex swt:flex-1 swt:h-full swt:min-h-0 swt:min-w-0 swt:flex-col"
            prop.children [
                match view with
                | SetupView.Choices ->
                    Main.Choices(arcFile, setArcFile, activeTableIndex, setView, (fun () -> setPreviousOutputOpen true))
                | SetupView.BuildingBlock ->
                    Main.SetupPage(
                        "Select a building block",
                        back,
                        BuildingBlockWidget.Main(arcFile, activeTableIndex, setArcFile),
                        scrollBody = true
                    )
                | SetupView.Templates ->
                    Main.SetupPage(
                        "Select template(s)",
                        back,
                        TemplateWidget.TemplateWidget(arcFile, activeTableIndex, setArcFile, onImportCompleted = back),
                        scrollBody = false
                    )

                Modals.PreviousTableSelect(
                    arcFile,
                    activeTableIndex,
                    setArcFile,
                    previousOutputOpen,
                    setPreviousOutputOpen
                )
            ]
        ]

    [<ReactComponent(true)>]
    static member EmptyTableView(arcFile: ArcFiles, setArcFile: ArcFiles -> unit, activeTableIndex: int option) =
        let tableKey =
            Helper.tryGetActiveTable arcFile activeTableIndex
            |> Option.map (fun (index, table) -> $"{index}:{table.Name}")
            |> Option.defaultValue "no-active-table"

        React.KeyedFragment(tableKey, [ Main.Session(arcFile, setArcFile, activeTableIndex) ])
