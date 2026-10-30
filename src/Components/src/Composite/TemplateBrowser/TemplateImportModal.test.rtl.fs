module internal Swate.Components.Composite.Template.TemplateImportModalTests

open ARCtrl
open Browser.Types
open Fable.Core
open Vitest
open Swate.Components.Composite.Template
open Swate.Components.Composite.Template.Types

Vitest.afterEach (fun () -> RTL.cleanup ())

let private modal
    (selectedTemplates: Template[])
    (existingTableNames: string[])
    (submitImport: ImportModalConfirmPayload -> unit)
    =
    TemplateImportModal.TemplateImportModal(
        isOpen = true,
        importType = TableJoinOptions.Headers,
        selectedTemplates = selectedTemplates,
        existingTableNames = existingTableNames,
        setIsOpen = ignore,
        setImportType = ignore,
        submitImport = submitImport
    )

let private renderModal (template: Template) (existingTableNames: string[]) submitImport =
    RTL.render (modal [| template |] existingTableNames submitImport) |> ignore

let private importButton () =
    RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Import"))

let private setNewTableName (name: string) =
    RTL.fireEvent.change (RTL.screen.getByLabelText "New table name", {| target = {| value = name |} |})

Vitest.test (
    "a template imported as a new table is renamed with a notice and cannot be imported under a taken name",
    fun () ->
        let template = Template.init "SugarExtraction"
        let mutable submitted: ImportModalConfirmPayload option = None
        renderModal template [| "SugarExtraction" |] (fun payload -> submitted <- Some payload)

        RTL.fireEvent.click (RTL.screen.getByLabelText "Create new table")
        let nameInput = RTL.screen.getByLabelText "New table name" :?> HTMLInputElement
        Vitest.expect(nameInput.value).toBe "SugarExtraction 1"
        let notice = "\"SugarExtraction\" already exists, so the new table is renamed."
        RTL.screen.getByText notice |> ignore
        Vitest.expect((importButton () :?> HTMLButtonElement).disabled).toBe false

        setNewTableName "SugarExtraction"
        Vitest.expect(RTL.screen.queryByText(notice).IsNone).toBe true

        RTL.screen.getByText "A table named \"SugarExtraction\" already exists."
        |> ignore

        Vitest.expect((importButton () :?> HTMLButtonElement).disabled).toBe true

        setNewTableName "Sugar extraction day 2"
        Vitest.expect((importButton () :?> HTMLButtonElement).disabled).toBe false
        RTL.fireEvent.click (importButton ())

        Vitest.expect(Map.toArray submitted.Value.NewTableNames).toEqual [| template.Id, "Sugar extraction day 2" |]
)

Vitest.test (
    "an import error is shown for the submitted inputs and hidden once the user changes them",
    fun () ->
        let template = Template.init "SugarExtraction"

        let modalWithError importError =
            TemplateImportModal.TemplateImportModal(
                isOpen = true,
                importType = TableJoinOptions.Headers,
                selectedTemplates = [| template |],
                existingTableNames = [||],
                setIsOpen = ignore,
                setImportType = ignore,
                submitImport = ignore,
                ?importError = importError
            )

        let rendered = RTL.render (modalWithError None)
        let error = "Import failed: table names must be unique"

        RTL.fireEvent.click (RTL.screen.getByLabelText "Create new table")
        RTL.fireEvent.click (importButton ())
        rendered.rerender (modalWithError (Some "table names must be unique"))
        RTL.screen.getByText error |> ignore

        setNewTableName "Sugar extraction day 2"
        Vitest.expect(RTL.screen.queryByText(error).IsNone).toBe true

        RTL.fireEvent.click (importButton ())
        RTL.screen.getByText error |> ignore
)

Vitest.test (
    "a typed name is dropped with its template, so selecting the template again suggests a free name",
    fun () ->
        let template = Template.init "SugarExtraction"
        let rendered = RTL.render (modal [| template |] [||] ignore)

        RTL.fireEvent.click (RTL.screen.getByLabelText "Create new table")
        setNewTableName "Day 2"
        rendered.rerender (modal [||] [| "Day 2" |] ignore)
        rendered.rerender (modal [| template |] [| "Day 2" |] ignore)

        RTL.fireEvent.click (RTL.screen.getByLabelText "Create new table")
        let nameInput = RTL.screen.getByLabelText "New table name" :?> HTMLInputElement
        Vitest.expect(nameInput.value).toBe "SugarExtraction"
)

let private withColumns (template: Template) =
    template.Table.AddColumn(CompositeHeader.Input IOType.Source)
    template.Table.AddColumn(CompositeHeader.Output IOType.Sample)
    template

/// jsdom fires "toggle" itself from a later timer, so the helper dispatches it inside act to render the grid before the next query.
let private openColumns (index: int) =
    let details =
        Browser.Dom.document.querySelectorAll("details").[index] :?> HTMLElement

    details.setAttribute ("open", "")
    RTL.act (fun () -> details.dispatchEvent (Browser.Event.Event.Create "toggle") |> ignore)

let private isImportDisabled () =
    (importButton () :?> HTMLButtonElement).disabled

Vitest.test (
    "unselecting all columns disables the import until a column is selected again",
    fun () ->
        let template = withColumns (Template.init "SugarExtraction")
        renderModal template [||] ignore

        RTL.fireEvent.click (RTL.screen.getByLabelText "Create new table")
        Vitest.expect(isImportDisabled ()).toBe false

        RTL.fireEvent.click (RTL.screen.getByText "Unselect all columns")
        Vitest.expect(isImportDisabled ()).toBe true
        RTL.screen.getByTestId "table-import-no-columns-0" |> ignore

        openColumns 0
        RTL.fireEvent.click (RTL.screen.getAllByRole("checkbox").[0])
        Vitest.expect(isImportDisabled ()).toBe false
        Vitest.expect(RTL.screen.queryByTestId("table-import-no-columns-0").IsNone).toBe true
)

Vitest.test (
    "a template that is not imported does not block the import when all its columns are unselected",
    fun () ->
        let first = withColumns (Template.init "First")
        let second = withColumns (Template.init "Second")
        RTL.render (modal [| first; second |] [||] ignore) |> ignore

        for radio in RTL.screen.getAllByLabelText "Create new table" do
            RTL.fireEvent.click radio

        RTL.fireEvent.click (RTL.screen.getAllByText("Unselect all columns").[0])
        Vitest.expect(isImportDisabled ()).toBe true

        RTL.fireEvent.click (RTL.screen.getAllByLabelText("Do not import").[0])
        Vitest.expect(isImportDisabled ()).toBe false
)

Vitest.test (
    "a deselected template row reaches the import payload",
    fun () ->
        let template = Template.init "SugarExtraction"

        template.Table.AddColumn(
            CompositeHeader.Input IOType.Source,
            ResizeArray [ for i in 1..3 -> CompositeCell.FreeText $"Sample {i}" ]
        )

        let mutable submitted: ImportModalConfirmPayload option = None

        RTL.render (
            TemplateImportModal.TemplateImportModal(
                isOpen = true,
                importType = TableJoinOptions.WithValues,
                selectedTemplates = [| template |],
                setIsOpen = ignore,
                setImportType = ignore,
                submitImport = (fun payload -> submitted <- Some payload)
            )
        )
        |> ignore

        RTL.fireEvent.click (RTL.screen.getByLabelText "Create new table")
        openColumns 0
        RTL.fireEvent.click (RTL.screen.getByTestId "table-import-row-0-1")
        RTL.fireEvent.click (importButton ())

        Vitest.expect(Set.toArray submitted.Value.DeselectedTemplateRows).toEqual [| template.Id, 1 |]
)

Vitest.test (
    "unselecting all rows disables the import until only headers are imported",
    fun () ->
        let template = Template.init "SugarExtraction"

        template.Table.AddColumn(
            CompositeHeader.Input IOType.Source,
            ResizeArray [ for i in 1..3 -> CompositeCell.FreeText $"Sample {i}" ]
        )

        let modalWith importType =
            TemplateImportModal.TemplateImportModal(
                isOpen = true,
                importType = importType,
                selectedTemplates = [| template |],
                setIsOpen = ignore,
                setImportType = ignore,
                submitImport = ignore
            )

        let rendered = RTL.render (modalWith TableJoinOptions.WithValues)
        RTL.fireEvent.click (RTL.screen.getByLabelText "Create new table")
        Vitest.expect(isImportDisabled ()).toBe false
        openColumns 0
        RTL.screen.getByTestId "table-import-row-0-0" |> ignore

        RTL.fireEvent.click (RTL.screen.getByText "Unselect all rows")
        Vitest.expect(isImportDisabled ()).toBe true

        rendered.rerender (modalWith TableJoinOptions.Headers)
        Vitest.expect(isImportDisabled ()).toBe false
        Vitest.expect(RTL.screen.queryByTestId("table-import-row-0-0").IsNone).toBe true
)
