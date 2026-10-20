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

        RTL.fireEvent.click (RTL.screen.getByLabelText "Import (new table)")
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

        RTL.fireEvent.click (RTL.screen.getByLabelText "Import (new table)")
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

        RTL.fireEvent.click (RTL.screen.getByLabelText "Import (new table)")
        setNewTableName "Day 2"
        rendered.rerender (modal [||] [| "Day 2" |] ignore)
        rendered.rerender (modal [| template |] [| "Day 2" |] ignore)

        RTL.fireEvent.click (RTL.screen.getByLabelText "Import (new table)")
        let nameInput = RTL.screen.getByLabelText "New table name" :?> HTMLInputElement
        Vitest.expect(nameInput.value).toBe "SugarExtraction"
)
