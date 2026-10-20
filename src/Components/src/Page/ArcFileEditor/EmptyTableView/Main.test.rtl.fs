module internal Swate.Components.Page.ArcFileEditor.EmptyTableView.MainTests

open ARCtrl
open Browser.Dom
open Browser.Types
open Fable.Core
open Feliz
open Vitest
open Swate.Components
open Swate.Components.Shared
open Swate.Components.Composite.Template.TemplateCacheContext
open Swate.Components.Composite.Widgets.Context
open Swate.Components.Page.ArcFileEditor.EmptyTableView
open Swate.Components.Page.ArcFileEditor.EmptyTableViewSample
open Swate.Components.Shared.ProvenanceGrouping.ARCtrlConverter

let private templates = Fixtures.templates ()
let private leafTemplateId = templates.[0].Id

let private longTest = TestOptions(timeout = 10_000)

let private waitFor (check: unit -> 'T) =
    RTL.waitFor (check, RTLWaitForOptions(timeout = 5_000))

module private Queries =

    let get (testId: string) = RTL.screen.getByTestId testId

    let present (testId: string) =
        (RTL.screen.queryByTestId testId).IsSome

    let click testId = RTL.fireEvent.click (get testId)

    let openTemplates () = click "empty-table-start-template"

    let selectLeafTemplate () = click $"template-row-{leafTemplateId}"

    let leafSelected () =
        (get $"template-selection-{leafTemplateId}" :?> HTMLInputElement).``checked``

    let importButton () =
        get "template-browser-import" :?> HTMLButtonElement

    let confirmImport () = click "template-import-confirm"

let private providers (children: ReactElement) =
    // Keep template identities stable across rerenders and independent of the displayed names.
    let cache = {
        Templates = templates
        IsLoading = false
        RefreshTemplates = ignore
    }

    Main.Providers(TemplateCacheCtx.Provider(cache, children))

let private view arcFile setArcFile activeIndex =
    providers (Swate.Components.Page.ArcFileEditor.EmptyTableView.Main.EmptyTableView(arcFile, setArcFile, activeIndex))

let private viewWithArcNodes arcFile setArcFile activeIndex fetchArcNodes =
    providers (
        Swate.Components.Page.ArcFileEditor.EmptyTableView.Main.EmptyTableView(
            arcFile,
            setArcFile,
            activeIndex,
            fetchArcNodes = fetchArcNodes
        )
    )

module private PriorTables =

    let ioTable (name: string) (input: string) (output: string) =
        let table = ArcTable.init name
        table.AddColumn(CompositeHeader.Input IOType.Source, ResizeArray [ CompositeCell.createFreeText input ])
        table.AddColumn(CompositeHeader.Output IOType.Sample, ResizeArray [ CompositeCell.createFreeText output ])
        table

    /// The open assay holds one filled table and one empty table at index 1.
    let arcFile () =
        let assay = ArcAssay.init "Inline setup"
        assay.AddTable(ioTable "Filled" "local-source" "local-sample")
        assay.AddTable(ArcTable.init "Empty")
        ArcFiles.Assay assay

    let arcNode scope parentIdentifier (table: ArcTable) : Types.ArcNodeTable = {
        Location = {
            Scope = scope
            ParentIdentifier = parentIdentifier
            TableName = table.Name
        }
        Table = table
    }

    let openPage () =
        Queries.click "empty-table-use-existing-tables"

    let columnSelect () =
        Queries.get "empty-table-existing-column-select" :?> HTMLSelectElement

    let optionCount () = columnSelect().options.length

    let selectOption testId =
        let select = columnSelect ()
        let option = Queries.get testId :?> HTMLOptionElement

        RTL.fireEvent.change (
            select,
            {|
                target = {| value = option.value |}
            |}
        )

    let importButton () =
        Queries.get "empty-table-existing-column-import" :?> HTMLButtonElement

Vitest.afterEach (fun () -> RTL.cleanup ())

Vitest.test (
    "setup pages focus their heading and Back returns to the choices",
    longTest,
    fun () -> promise {
        RTL.render (view (PriorTables.arcFile ()) ignore (Some 1)) |> ignore

        for entryId, pageId in
            [|
                "empty-table-start-scratch", "empty-table-building-block-page"
                "empty-table-start-template", "empty-table-templates-page"
                "empty-table-use-existing-tables", "empty-table-existing-tables-page"
            |] do
            Queries.click entryId
            Queries.get pageId |> ignore
            Vitest.expect(Queries.present "empty-table-choices").toBe false
            let heading = Queries.get "empty-table-setup-heading"
            do! waitFor (fun () -> Vitest.expect(box document.activeElement).toBe (box heading))

            Queries.click "empty-table-setup-back"
            Queries.get "empty-table-choices" |> ignore
            Vitest.expect(Queries.present pageId).toBe false
    }
)

Vitest.test (
    "switching empty tables resets the inline route and template selection",
    longTest,
    fun () -> promise {
        let arcFile = Fixtures.arcFile ()
        let rendered = RTL.render (view arcFile ignore (Some 0))
        Queries.openTemplates ()
        Queries.selectLeafTemplate ()
        Vitest.expect(Queries.leafSelected ()).toBe true

        rendered.rerender (view arcFile ignore (Some 1))

        Vitest.expect(Queries.present "empty-table-templates-page").toBe false
        Queries.get "empty-table-choices" |> ignore

        Queries.openTemplates ()
        Vitest.expect(Queries.leafSelected ()).toBe false
        Vitest.expect(Queries.importButton().disabled).toBe true
        Vitest.expect(arcFile.Tables().[0].ColumnCount).toBe 0
        Vitest.expect(arcFile.Tables().[1].ColumnCount).toBe 0
    }
)

Vitest.test (
    "adding a building block invokes the file callback with the selected column",
    longTest,
    fun () -> promise {
        let arcFile = Fixtures.arcFile ()
        let mutable updated: ArcFiles option = None
        RTL.render (view arcFile (fun next -> updated <- Some next) (Some 0)) |> ignore
        Queries.click "empty-table-start-scratch"
        Queries.click "building-block-header-select"
        Queries.click "building-block-submenu-more"
        // A single-column header makes the observable callback independent of ontology search.
        Queries.click "building-block-header-Date"
        Queries.click "building-block-add-column"

        Vitest.expect(updated.IsSome).toBe true
        Vitest.expect(updated.Value.Tables().[0].Headers.[0]).toEqual CompositeHeader.Date
        Vitest.expect(updated.Value.Tables().[1].ColumnCount).toBe 0
    }
)

Vitest.test (
    "canceling import retains selection and confirmed append updates only the active table",
    longTest,
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
        RTL.fireEvent.click (Queries.importButton ())
        Queries.get "modal_template-import" |> ignore
        Queries.click "template-import-cancel"

        Vitest.expect(updated.IsNone).toBe true
        Vitest.expect(Queries.present "modal_template-import").toBe false
        Vitest.expect(Queries.leafSelected ()).toBe true
        Queries.get "empty-table-templates-page" |> ignore
        RTL.fireEvent.click (Queries.importButton ())
        Queries.confirmImport ()

        Vitest.expect(updated.IsSome).toBe true
        let tables = updated.Value.Tables()
        Vitest.expect(tables.[0].ColumnCount).toBe 3

        Vitest
            .expect(tables.[0].Headers.[1])
            .toEqual (CompositeHeader.Parameter(OntologyAnnotation("Leaf temperature")))

        Vitest.expect(tables.[1].ColumnCount).toBe 0
        Vitest.expect(closedWidgets.Count).toBe 0
        Queries.get "empty-table-choices" |> ignore
        Vitest.expect(Queries.present "empty-table-templates-page").toBe false
    }
)

Vitest.test (
    "import as a new table returns to choices while preserving the empty active table",
    longTest,
    fun () -> promise {
        let arcFile = Fixtures.arcFile ()
        let mutable updated: ArcFiles option = None
        RTL.render (view arcFile (fun next -> updated <- Some next) (Some 0)) |> ignore
        Queries.openTemplates ()
        Queries.selectLeafTemplate ()
        RTL.fireEvent.click (Queries.importButton ())
        Queries.click $"template-import-action-{leafTemplateId}-ImportAsNewTable"

        Queries.confirmImport ()

        Vitest.expect(updated.IsSome).toBe true
        Vitest.expect(updated.Value.Tables().Count).toBe 3
        Vitest.expect(updated.Value.Tables().[0].ColumnCount).toBe 0
        Vitest.expect(updated.Value.Tables().[2].ColumnCount).toBe 3
        Queries.get "empty-table-choices" |> ignore
    }
)

Vitest.test (
    "OnlyLocal: local columns can be selected, previewed and imported without fetching",
    longTest,
    fun () -> promise {
        let arcFile = PriorTables.arcFile ()
        let mutable updated: ArcFiles option = None
        RTL.render (view arcFile (fun next -> updated <- Some next) (Some 1)) |> ignore

        PriorTables.openPage ()

        do!
            waitFor (fun () ->
                Vitest.expect(PriorTables.columnSelect().disabled).toBe false
                Vitest.expect(PriorTables.optionCount ()).toBe 2
                Vitest.expect(Queries.present "empty-table-existing-column-loading").toBe false
                Vitest.expect(Queries.present "empty-table-existing-tables-empty").toBe false
                Vitest.expect(Queries.present "empty-table-existing-tables-error").toBe false
            )

        PriorTables.selectOption "empty-table-existing-column-Assay Inline setup (this file)-Filled-Input"

        do!
            waitFor (fun () ->
                Vitest.expect((Queries.get "empty-table-existing-column-preview").textContent).toContain "local-source"
            )

        PriorTables.selectOption "empty-table-existing-column-Assay Inline setup (this file)-Filled-Output"

        do!
            waitFor (fun () ->
                Vitest.expect((Queries.get "empty-table-existing-column-preview").textContent).toContain "local-sample"
                Vitest.expect(PriorTables.importButton().disabled).toBe false
            )

        RTL.fireEvent.click (PriorTables.importButton ())

        do! waitFor (fun () -> Vitest.expect(updated.IsSome).toBe true)
        let imported = updated.Value.Tables().[1]
        Vitest.expect(imported.Headers |> Seq.toArray).toEqual ([| CompositeHeader.Input IOType.Sample |])
        Vitest.expect(imported.GetColumn(0).Cells.[0]).toEqual (CompositeCell.createFreeText "local-sample")
        Vitest.expect(updated.Value.Tables().[0].ColumnCount).toBe 2

        Vitest.expect(Queries.present "empty-table-existing-tables-page").toBe false
        Queries.get "empty-table-choices" |> ignore
    }
)

Vitest.test (
    "FetchArcNodes: an empty select stays disabled while loading, then fetched columns can be imported",
    longTest,
    fun () -> promise {
        let arcFile = Fixtures.arcFile ()
        let mutable updated: ArcFiles option = None
        let mutable resolve = ignore
        let pending = Promise.create (fun resolveFetch _ -> resolve <- resolveFetch)

        RTL.render (viewWithArcNodes arcFile (fun next -> updated <- Some next) (Some 0) (fun () -> pending))
        |> ignore

        PriorTables.openPage ()

        do!
            waitFor (fun () ->
                Vitest.expect(PriorTables.columnSelect().disabled).toBe true
                Vitest.expect(PriorTables.importButton().disabled).toBe true

                Vitest.expect((Queries.get "empty-table-existing-column-loading" :?> HTMLOptionElement).disabled).toBe
                    true

                Vitest.expect(Queries.present "empty-table-existing-tables-empty").toBe false
            )

        do!
            RTL.act (fun () -> promise {
                resolve [|
                    PriorTables.arcNode ArcTableScope.Study "Field" (PriorTables.ioTable "Harvest" "plant-1" "leaf-1")
                    // The ARC copy of the open file must not offer stale columns.
                    PriorTables.arcNode
                        ArcTableScope.Assay
                        "Inline setup"
                        (PriorTables.ioTable "Filled" "stale-source" "stale-sample")
                |]
            })

        do!
            waitFor (fun () ->
                Vitest.expect(PriorTables.columnSelect().disabled).toBe false
                Vitest.expect(PriorTables.optionCount ()).toBe 2
                Vitest.expect(Queries.present "empty-table-existing-column-loading").toBe false
                Vitest.expect(Queries.present "empty-table-existing-tables-empty").toBe false
                Vitest.expect(Queries.present "empty-table-existing-tables-error").toBe false
            )

        PriorTables.selectOption "empty-table-existing-column-Study Field-Harvest-Input"

        do!
            waitFor (fun () ->
                Vitest.expect((Queries.get "empty-table-existing-column-preview").textContent).toContain "plant-1"
                Vitest.expect(PriorTables.importButton().disabled).toBe false
            )

        RTL.fireEvent.click (PriorTables.importButton ())

        do! waitFor (fun () -> Vitest.expect(updated.IsSome).toBe true)
        let imported = updated.Value.Tables().[0]
        Vitest.expect(imported.Headers |> Seq.toArray).toEqual ([| CompositeHeader.Input IOType.Source |])
        Vitest.expect(imported.GetColumn(0).Cells.[0]).toEqual (CompositeCell.createFreeText "plant-1")
    }
)

Vitest.test (
    "FetchArcNodesEmpty: completed loading with no nodes shows a blankslate and a disabled select",
    longTest,
    fun () -> promise {
        RTL.render (viewWithArcNodes (Fixtures.arcFile ()) ignore (Some 0) Fixtures.arcNodesEmpty)
        |> ignore

        PriorTables.openPage ()

        do!
            waitFor (fun () ->
                Vitest.expect((Queries.get "empty-table-existing-tables-empty").textContent).toContain
                    "Unable to find existing input or output columns"

                Vitest.expect(PriorTables.columnSelect().disabled).toBe true
                Vitest.expect(PriorTables.optionCount ()).toBe 0
                Vitest.expect(PriorTables.importButton().disabled).toBe true
                Vitest.expect(Queries.present "empty-table-existing-column-loading").toBe false
                Vitest.expect(Queries.present "empty-table-existing-column-preview").toBe false
                Vitest.expect(Queries.present "empty-table-existing-tables-error").toBe false
            )
    }
)

Vitest.test (
    "FetchArcNodesFailed: a failed fetch shows an error alert and retry can populate the select",
    longTest,
    fun () -> promise {
        let mutable attempts = 0

        let fetchArcNodes () = promise {
            attempts <- attempts + 1

            if attempts = 1 then
                return! Fixtures.arcNodesFailed ()
            else
                return [|
                    PriorTables.arcNode ArcTableScope.Run "Measure" (PriorTables.ioTable "Readout" "in" "out")
                |]
        }

        RTL.render (viewWithArcNodes (Fixtures.arcFile ()) ignore (Some 0) fetchArcNodes)
        |> ignore

        PriorTables.openPage ()

        do!
            waitFor (fun () ->
                let alert = Queries.get "empty-table-existing-tables-error"
                Vitest.expect(alert.getAttribute "role").toBe "alert"
                Vitest.expect(alert.textContent).toContain "Failed to fetch ARC nodes"
                Vitest.expect(PriorTables.columnSelect().disabled).toBe true
                Vitest.expect(PriorTables.importButton().disabled).toBe true
                Vitest.expect(Queries.present "empty-table-existing-column-loading").toBe false
            )

        Queries.click "empty-table-existing-tables-retry"

        do!
            waitFor (fun () ->
                Queries.get "empty-table-existing-column-Run Measure-Readout-Output" |> ignore
                Vitest.expect(PriorTables.columnSelect().disabled).toBe false
                Vitest.expect(PriorTables.optionCount ()).toBe 2
                Vitest.expect(attempts).toBe 2
                Vitest.expect(Queries.present "empty-table-existing-tables-error").toBe false
                Vitest.expect(Queries.present "empty-table-existing-tables-empty").toBe false
            )
    }
)

Vitest.test (
    "FetchArcNodesFailed: local nodes exist and failing fetch still shows preview",
    longTest,
    fun () -> promise {
        let mutable reject = ignore
        let pending = Promise.create (fun _ rejectFetch -> reject <- rejectFetch)

        RTL.render (viewWithArcNodes (PriorTables.arcFile ()) ignore (Some 1) (fun () -> pending))
        |> ignore

        PriorTables.openPage ()

        let optionId =
            "empty-table-existing-column-Assay Inline setup (this file)-Filled-Output"

        PriorTables.selectOption optionId

        do!
            waitFor (fun () ->
                Vitest.expect((Queries.get "empty-table-existing-column-preview").textContent).toContain "local-sample"
                Queries.get "empty-table-existing-column-loading" |> ignore
            )

        do! RTL.act (fun () -> promise { reject (exn "Failed to fetch ARC nodes") })

        do!
            waitFor (fun () ->
                let alert = Queries.get "empty-table-existing-tables-error"
                Vitest.expect(alert.getAttribute "role").toBe "alert"
                Vitest.expect(alert.textContent).toContain "Failed to fetch ARC nodes"
                Vitest.expect(PriorTables.columnSelect().disabled).toBe false
                Vitest.expect(PriorTables.optionCount ()).toBe 2

                Vitest
                    .expect(PriorTables.columnSelect().value)
                    .toBe ((Queries.get optionId :?> HTMLOptionElement).value)

                Vitest.expect((Queries.get "empty-table-existing-column-preview").textContent).toContain "local-sample"
                Vitest.expect(PriorTables.importButton().disabled).toBe false
                Vitest.expect(Queries.present "empty-table-existing-column-loading").toBe false
                Vitest.expect(Queries.present "empty-table-existing-tables-empty").toBe false
            )
    }
)

Vitest.test (
    "FetchArcNodesLongLoading: local columns remain selectable and importable while fetching is pending",
    longTest,
    fun () -> promise {
        let arcFile = PriorTables.arcFile ()
        let mutable updated: ArcFiles option = None

        RTL.render (viewWithArcNodes arcFile (fun next -> updated <- Some next) (Some 1) Fixtures.arcNodesLongLoading)
        |> ignore

        PriorTables.openPage ()

        do!
            waitFor (fun () ->
                Vitest.expect(PriorTables.columnSelect().disabled).toBe false
                Vitest.expect(PriorTables.optionCount ()).toBe 3

                Vitest.expect((Queries.get "empty-table-existing-column-loading" :?> HTMLOptionElement).disabled).toBe
                    true

                Vitest.expect(Queries.present "empty-table-existing-tables-empty").toBe false
                Vitest.expect(Queries.present "empty-table-existing-tables-error").toBe false
            )

        let optionId =
            "empty-table-existing-column-Assay Inline setup (this file)-Filled-Input"

        PriorTables.selectOption optionId

        do!
            waitFor (fun () ->
                Vitest
                    .expect(PriorTables.columnSelect().value)
                    .toBe ((Queries.get optionId :?> HTMLOptionElement).value)

                Vitest.expect(PriorTables.importButton().disabled).toBe false
                // The fetch is still pending when we select and import a local column.
                Queries.get "empty-table-existing-column-loading" |> ignore
            )

        RTL.fireEvent.click (PriorTables.importButton ())
        do! waitFor (fun () -> Vitest.expect(updated.IsSome).toBe true)
        let imported = updated.Value.Tables().[1]
        Vitest.expect(imported.Headers |> Seq.toArray).toEqual ([| CompositeHeader.Input IOType.Source |])
        Vitest.expect(imported.GetColumn(0).Cells.[0]).toEqual (CompositeCell.createFreeText "local-source")
        Vitest.expect(Queries.present "empty-table-existing-tables-page").toBe false
        Queries.get "empty-table-choices" |> ignore
    }
)
