namespace Swate.Components.Page.ArcFileEditor.Widgets

open Feliz
open Fable.Core
open ARCtrl
open Swate.Components
open Swate.Components.Primitive
open Swate.Components.Page.ArcFileEditor
open Swate.Components.Composite.EditorToolbar
open Swate.Components.Composite.Widgets
open Swate.Components.Composite.Widgets.Context

module private WidgetsHelper =

    let widgetTypes = [
        WidgetType.BuildingBlock
        WidgetType.Template
        WidgetType.FilePicker
        WidgetType.DataAnnotator
        WidgetType.JsonImport
        WidgetType.JsonExport
    ]

    /// The label and the icon class of the toggle button of a widget.
    let widgetInfo (widgetType: WidgetType) =
        match widgetType with
        | WidgetType.BuildingBlock -> "Add Building Block", "swt:fluent--table-column-insert-24-regular"
        | WidgetType.Template -> "Add Template", "swt:fluent--table-add-24-regular"
        | WidgetType.FilePicker -> "File Picker", "swt:fluent--document-text-link-24-regular"
        | WidgetType.DataAnnotator -> "Data Annotator", "swt:fluent--document-data-link-24-regular"
        | WidgetType.JsonImport -> "Import JSON", "swt:fluent--arrow-import-24-regular swt:rotate-180"
        | WidgetType.JsonExport -> "Export JSON", "swt:fluent--arrow-export-24-regular"
        | WidgetType.Playground -> "Playground", "swt:fluent--beaker-24-regular"

open WidgetsHelper

[<Erase; Mangle(false)>]
type Main =

    /// One toggle button per widget, joined like a button group of the editor toolbar. A pressed button shows that
    /// its widget is open.
    [<ReactComponent>]
    static member WidgetToggleBtns() =

        let context = useWidgetControllerCtx ()

        Html.div [
            prop.className "swt:join"
            prop.children [
                for widgetType in widgetTypes do
                    let label, iconClass = widgetInfo widgetType

                    EditorToolbar.ToolbarButton(
                        label,
                        iconClass,
                        (fun () -> context.toggleWidget widgetType),
                        joined = true,
                        pressed = context.isActive widgetType,
                        key = label
                    )
            ]
        ]

    [<ReactComponent>]
    static member Widgets
        (
            children: ReactElement,
            buildingBlockWidget: ReactElement,
            templateWidget: ReactElement,
            filePickerWidget: ReactElement,
            dataAnnotatorWidget: ReactElement,
            jsonImportWidget: ReactElement,
            jsonExportWidget: ReactElement
        ) =
        let widgets: Map<WidgetType, WidgetDefinition> =
            React.useMemo (
                (fun () ->
                    [
                        WidgetType.BuildingBlock,
                        {|
                            prefix = "ADD_BUILDINGBLOCK"
                            content = buildingBlockWidget
                        |}
                        WidgetType.Template,
                        {|
                            prefix = "ADD_TEMPLATE"
                            content = templateWidget
                        |}
                        WidgetType.FilePicker,
                        {|
                            prefix = "FILEPICKER"
                            content = filePickerWidget
                        |}
                        WidgetType.DataAnnotator,
                        {|
                            prefix = "DATAANNOTATOR"
                            content = dataAnnotatorWidget
                        |}
                        WidgetType.JsonImport,
                        {|
                            prefix = "JSONIMPORT"
                            content = jsonImportWidget
                        |}
                        WidgetType.JsonExport,
                        {|
                            prefix = "JSONEXPORT"
                            content = jsonExportWidget
                        |}
                    ]
                    |> Map.ofList
                ),
                [|
                    box buildingBlockWidget
                    box templateWidget
                    box filePickerWidget
                    box dataAnnotatorWidget
                    box jsonImportWidget
                    box jsonExportWidget
                |]
            )

        Widget.WidgetController(widgets, children = children)
