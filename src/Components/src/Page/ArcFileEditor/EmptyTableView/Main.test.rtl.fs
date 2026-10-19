module internal Swate.Components.Page.ArcFileEditor.EmptyTableView.MainTests

open ARCtrl
open Browser.Dom
open Browser.Types
open Fable.Core
open Feliz
open Vitest
open Swate.Components
open Swate.Components.Shared
open Swate.Components.Composite.Widgets.Context
open Swate.Components.Page.ArcFileEditor.EmptyTableView
open Swate.Components.Page.ArcFileEditor.EmptyTableViewSample

module private Queries =

    [<Import("getByRole", "@testing-library/dom")>]
    let inRole (container: HTMLElement, role: string, options: ByRoleOptions) : HTMLElement = jsNative

    let button name =
        RTL.screen.getByRole ("button", ByRoleOptions(name = Text name))

    let card name =
        RTL.screen.getByRole (
            "button",
            ByRoleOptions(name = TextMatch.Regex(System.Text.RegularExpressions.Regex name))
        )

    let heading name =
        RTL.screen.getByRole ("heading", ByRoleOptions(name = Text name))

    let dialog name =
        RTL.screen.getByRole ("dialog", ByRoleOptions(name = Text name))

    let clickButton name = RTL.fireEvent.click (button name)

    let openTemplates () =
        RTL.fireEvent.click (card "Start with template!")

    let selectLeafTemplate () =
        RTL.fireEvent.click (RTL.screen.getByText ("Leaf template", ByTextOptions(exact = true)))

    let confirmImport () =
        RTL.fireEvent.click (inRole (dialog "Import templates", "button", ByRoleOptions(name = Text "Import")))

let private view arcFile setArcFile activeIndex =
    Main.Providers(
        Swate.Components.Page.ArcFileEditor.EmptyTableView.Main.EmptyTableView(arcFile, setArcFile, activeIndex)
    )

Vitest.afterEach (fun () -> RTL.cleanup ())

Vitest.test (
    "inline setup focuses its heading and Back returns to the choices",
    fun () -> promise {
        RTL.render (view (Fixtures.arcFile ()) ignore (Some 0)) |> ignore

        RTL.fireEvent.click (Queries.card "Start from scratch!")
        let heading = Queries.heading "Select a building block"

        Vitest
            .expect(RTL.screen.queryByRole("dialog", ByRoleOptions(name = Text "Select a building block")).IsNone)
            .toBe
            true

        do! RTL.waitFor (fun () -> Vitest.expect(box document.activeElement).toBe (box heading))

        Queries.clickButton "Back"
        Queries.card "Start from scratch!" |> ignore

        Queries.openTemplates ()
        let templateHeading = Queries.heading "Select template(s)"
        do! RTL.waitFor (fun () -> Vitest.expect(box document.activeElement).toBe (box templateHeading))
        Queries.clickButton "Back"
        Queries.card "Start with template!" |> ignore
    }
)

Vitest.test (
    "switching empty tables resets the inline route and template selection",
    fun () -> promise {
        let arcFile = Fixtures.arcFile ()
        let rendered = RTL.render (view arcFile ignore (Some 0))
        Queries.openTemplates ()
        Queries.selectLeafTemplate ()
        RTL.screen.getByText "1 selected" |> ignore

        rendered.rerender (view arcFile ignore (Some 1))

        Vitest.expect(RTL.screen.queryByRole("heading", ByRoleOptions(name = Text "Select template(s)")).IsNone).toBe
            true

        Queries.openTemplates ()
        Vitest.expect((Queries.button "Import" :?> HTMLButtonElement).disabled).toBe true
        Vitest.expect(arcFile.Tables().[0].ColumnCount).toBe 0
        Vitest.expect(arcFile.Tables().[1].ColumnCount).toBe 0
    }
)

Vitest.test (
    "adding a building block invokes the file callback with the selected column",
    fun () -> promise {
        let arcFile = Fixtures.arcFile ()
        let mutable updated: ArcFiles option = None
        RTL.render (view arcFile (fun next -> updated <- Some next) (Some 0)) |> ignore
        RTL.fireEvent.click (Queries.card "Start from scratch!")
        Queries.clickButton "Parameter"
        let picker = Queries.dialog "Building block type"
        RTL.fireEvent.click (RTL.screen.getByText ("More", ByTextOptions(exact = true)))
        // A single-column header makes the observable callback independent of ontology search.
        RTL.fireEvent.click (Queries.inRole (picker, "button", ByRoleOptions(name = Text "Date")))
        Queries.clickButton "Add Column"

        Vitest.expect(updated.IsSome).toBe true
        Vitest.expect(updated.Value.Tables().[0].Headers.[0]).toEqual CompositeHeader.Date
        Vitest.expect(updated.Value.Tables().[1].ColumnCount).toBe 0
    }
)

Vitest.test (
    "canceling import retains selection and confirmed append updates only the active table",
    fun () -> promise {
        let arcFile = Fixtures.arcFile ()
        let mutable updated: ArcFiles option = None
        let closedWidgets = ResizeArray<WidgetType>()

        let widgetController = {
            WidgetControllerContext.init () with
                activeWidgets = [ WidgetType.Template ]
                closeWidget = closedWidgets.Add
        }

        RTL.render (
            WidgetControllerCtx.Provider(widgetController, view arcFile (fun next -> updated <- Some next) (Some 0))
        )
        |> ignore

        Queries.openTemplates ()
        Queries.selectLeafTemplate ()
        Queries.clickButton "Import"

        RTL.fireEvent.click (
            Queries.inRole (Queries.dialog "Import templates", "button", ByRoleOptions(name = Text "Cancel"))
        )

        Vitest.expect(updated.IsNone).toBe true
        RTL.screen.getByText "1 selected" |> ignore
        Queries.heading "Select template(s)" |> ignore
        Queries.clickButton "Import"
        Queries.confirmImport ()

        Vitest.expect(updated.IsSome).toBe true
        let tables = updated.Value.Tables()
        Vitest.expect(tables.[0].ColumnCount).toBe 3

        Vitest
            .expect(tables.[0].Headers.[1])
            .toEqual (CompositeHeader.Parameter(OntologyAnnotation("Leaf temperature")))

        Vitest.expect(tables.[1].ColumnCount).toBe 0
        Vitest.expect(closedWidgets.Count).toBe 0
    }
)

Vitest.test (
    "import as a new table returns to choices while preserving the empty active table",
    fun () -> promise {
        let arcFile = Fixtures.arcFile ()
        let mutable updated: ArcFiles option = None
        RTL.render (view arcFile (fun next -> updated <- Some next) (Some 0)) |> ignore
        Queries.openTemplates ()
        Queries.selectLeafTemplate ()
        Queries.clickButton "Import"

        RTL.fireEvent.click (
            Queries.inRole (Queries.dialog "Import templates", "radio", ByRoleOptions(name = Text "Import (new table)"))
        )

        Queries.confirmImport ()

        Vitest.expect(updated.IsSome).toBe true
        Vitest.expect(updated.Value.Tables().Count).toBe 3
        Vitest.expect(updated.Value.Tables().[0].ColumnCount).toBe 0
        Vitest.expect(updated.Value.Tables().[2].ColumnCount).toBe 3
        Queries.card "Start with template!" |> ignore
    }
)
