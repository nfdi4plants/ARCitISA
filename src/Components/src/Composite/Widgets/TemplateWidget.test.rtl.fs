module internal Swate.Components.Composite.Widgets.TemplateWidgetTests

open ARCtrl
open Browser.Types
open Feliz
open Vitest
open Swate.Components.Shared
open Swate.Components.Composite.Widgets
open Swate.Components.Composite.Widgets.Context
open Swate.Components.Composite.Widgets.Types

Vitest.afterEach (fun () -> RTL.cleanup ())

let private tableWithInput (name: string) =
    let table = ArcTable.init name
    table.AddColumn(CompositeHeader.Input IOType.Source)
    table

/// The template browser and the import dialog both have an "Import" button; this is the one in the dialog.
let private dialogImportButton () =
    RTL.screen.getAllByRole ("button", ByRoleOptions(name = Text "Import"))
    |> Seq.find (fun button -> (button.closest "[role=dialog]").IsSome)
    :?> HTMLButtonElement

Vitest.test (
    "importing a template whose name is taken adds it under a free name, then closes the dialog and the widget",
    fun () -> promise {
        let arcFile = ArcFiles.Assay(ArcAssay.init "Assay")
        arcFile.ArcTables().AddTable(tableWithInput "Sugar extraction")

        let template = Template.init "Sugar extraction"
        template.Table.AddColumn(CompositeHeader.Input IOType.Source)
        // By default the template browser only shows DataPLANT templates.
        template.Organisation <- Organisation.DataPLANT

        let savedFiles = ResizeArray<ArcFiles>()
        let closedWidgets = ResizeArray<WidgetType>()

        RTL.render (
            WidgetControllerCtx.Provider(
                {
                    WidgetControllerContext.init () with
                        closeWidget = closedWidgets.Add
                },
                [
                    TemplateWidget.Entry(
                        arcFile,
                        Some 0,
                        savedFiles.Add,
                        {
                            loadTemplates = fun () -> async { return Ok [| template |] }
                        }
                    )
                ]
            )
        )
        |> ignore

        let! templateRow = RTL.screen.findByText "Sugar extraction"
        RTL.fireEvent.click templateRow
        RTL.fireEvent.click (RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Import")))
        RTL.fireEvent.click (RTL.screen.getByLabelText "Import (new table)")
        RTL.fireEvent.click (dialogImportButton ())

        Vitest.expect(savedFiles.Count).toBe 1

        Vitest.expect(savedFiles.[0].Tables() |> Seq.map _.Name |> Array.ofSeq).toEqual [|
            "Sugar extraction"
            "Sugar extraction 1"
        |]

        Vitest.expect(arcFile.Tables() |> Seq.map _.Name |> Array.ofSeq).toEqual [| "Sugar extraction" |]
        Vitest.expect(Array.ofSeq closedWidgets).toEqual [| WidgetType.Template |]
        Vitest.expect(RTL.screen.queryByLabelText("New table name").IsNone).toBe true
    }
)
