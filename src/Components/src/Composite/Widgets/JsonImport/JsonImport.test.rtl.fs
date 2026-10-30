module internal Swate.Components.Composite.Widgets.JsonImport.JsonImportTests

open ARCtrl
open Browser.Dom
open Browser.Types
open Fable.Core
open Vitest
open Swate.Components.Shared
open Swate.Components.Composite.Widgets.JsonImport.Types

Vitest.afterEach (fun () -> RTL.cleanup ())

let private measurement (name: string) =
    let table = ArcTable.init name
    table.AddColumn(CompositeHeader.Input IOType.Source)
    table.AddColumn(CompositeHeader.Output IOType.Sample)
    table.AddRowsEmpty 1
    table

let private assayWith (tableNames: string list) =
    let assay = ArcAssay.init "FixtureAssay"

    for name in tableNames do
        assay.AddTable(measurement name)

    ArcFiles.Assay assay

let private jsonOf (file: ArcFiles) (format: JsonExportFormat) =
    snd (Json.Export.parseToJsonString (file, format))

let private currentAssay () =
    let assay = ArcAssay.init "CurrentAssay"
    assay.AddTable(measurement "Existing")
    ArcFiles.Assay assay

type private Harness = {
    SetArcFileCalls: ResizeArray<ArcFiles>
    Requests: ResizeArray<JsonImportRequest>
    Errors: ResizeArray<JsonImportError>
    mutable Completed: int
}

let private newHarness () = {
    SetArcFileCalls = ResizeArray()
    Requests = ResizeArray()
    Errors = ResizeArray()
    Completed = 0
}

let private pickerOf (content: string) =
    fun () -> promise {
        return
            Ok(
                Some {
                    FileName = Some "fixture.json"
                    Content = content
                }
            )
    }

/// Renders the widget. Without onImportJson the widget applies the import to setArcFile itself.
let private renderWidget
    (harness: Harness)
    (current: ArcFiles)
    (activeTableIndex: int option)
    (pickJsonFile: unit -> JS.Promise<Result<JsonImportFile option, exn>>)
    (onImportJson: (JsonImportRequest -> JS.Promise<Result<unit, exn>>) option)
    =
    RTL.render (
        JsonImport.JsonImport(
            current,
            harness.SetArcFileCalls.Add,
            ?activeTableIndex = activeTableIndex,
            pickJsonFile = pickJsonFile,
            ?onImportJson = onImportJson,
            onError = harness.Errors.Add,
            onImportCompleted = (fun () -> harness.Completed <- harness.Completed + 1)
        )
    )
    |> ignore

/// An onImportJson that records the request and answers with the given result.
let private recording (harness: Harness) (result: Result<unit, exn>) =
    Some(fun (request: JsonImportRequest) ->
        harness.Requests.Add request
        promise { return result }
    )

let private choosePicker () =
    RTL.fireEvent.click (RTL.screen.getByTestId "json-import-picker-button")

let private openReview () = promise {
    choosePicker ()
    let! _ = RTL.screen.findByTestId "modal_json-import"
    return ()
}

let private exists (testId: string) =
    (RTL.screen.queryByTestId testId).IsSome

let private modalIsOpen () = exists "modal_json-import"

let private button (testId: string) =
    RTL.screen.getByTestId testId :?> HTMLButtonElement

let private radio (index: int) (action: string) =
    RTL.screen.getByTestId $"table-import-action-{index}-{action}" :?> HTMLInputElement

/// The column checkboxes. The row checkboxes are looked up by their own test ids.
let private checkboxes () =
    RTL.screen.getAllByRole "checkbox"
    |> Seq.map (fun el -> el :?> HTMLInputElement)
    |> Seq.filter (fun el -> el.getAttribute("data-testid").StartsWith "table-import-column-")
    |> Array.ofSeq

/// jsdom fires "toggle" itself from a later timer, so the helper dispatches it inside act to render the grid before the next query.
let private openColumns (index: int) =
    let details =
        Browser.Dom.document.querySelectorAll("details").[index] :?> HTMLElement

    details.setAttribute ("open", "")
    RTL.act (fun () -> details.dispatchEvent (Browser.Event.Event.Create "toggle") |> ignore)

let private eventually (check: unit -> unit) : JS.Promise<unit> =
    RTL.waitFor ((fun () -> check ()), RTLWaitForOptions(timeout = 3000))

let private pending () =
    let mutable resolve: Result<unit, exn> -> unit = ignore
    let p = Promise.create (fun res _ -> resolve <- res)
    p, (fun result -> resolve result)

Vitest.test (
    "the formats of an Assay are listed with the description of the selected one",
    fun () ->
        let harness = newHarness ()
        renderWidget harness (currentAssay ()) None (pickerOf "{}") None

        let select = RTL.screen.getByTestId "json-import-format-select"

        let optionTexts =
            [
                for i in 0 .. select.querySelectorAll("option").length - 1 -> select.querySelectorAll("option").[i]
            ]
            |> Seq.map (fun node -> node.textContent)
            |> Array.ofSeq

        Vitest.expect(optionTexts.Length).toBe 4

        let description () =
            (RTL.screen.getByTestId "json-import-format-description").textContent

        Vitest.expect(description ()).toBe "RO-Crate JSON-LD metadata, as found in ARC RO-Crate exports."
        RTL.fireEvent.change (select, {| target = {| value = "ISA" |} |})
        Vitest.expect(description ()).toBe "ISA-JSON, the exchange format of the ISA tools."
)

Vitest.test (
    "choosing a file does not change the ARC",
    fun () -> promise {
        let harness = newHarness ()

        renderWidget
            harness
            (currentAssay ())
            None
            (pickerOf (jsonOf (assayWith [ "A" ]) JsonExportFormat.ROCrate))
            (recording harness (Ok()))

        do! openReview ()

        Vitest.expect(harness.SetArcFileCalls.Count).toBe 0
        Vitest.expect(harness.Requests.Count).toBe 0
    }
)

Vitest.test (
    "invalid JSON reaches onError and opens no modal",
    fun () -> promise {
        let harness = newHarness ()
        renderWidget harness (currentAssay ()) None (pickerOf "not json") None
        choosePicker ()
        do! eventually (fun () -> Vitest.expect(harness.Errors.Count).toBe 1)

        let error = harness.Errors.[0]
        Vitest.expect(error.Title).toBe "Could not import JSON"
        Vitest.expect(error.Message).toBe "Could not read \"fixture.json\"."
        Vitest.expect(error.Details.IsSome).toBe true
        Vitest.expect(modalIsOpen ()).toBe false
    }
)

Vitest.test (
    "a failing picker reaches onError and opens no modal",
    fun () -> promise {
        let harness = newHarness ()
        let failing () = promise { return Error(exn "disk gone") }
        renderWidget harness (currentAssay ()) None failing None
        choosePicker ()
        do! eventually (fun () -> Vitest.expect(harness.Errors.Count).toBe 1)

        let error = harness.Errors.[0]
        Vitest.expect(error.Title).toBe "Could not import JSON"
        Vitest.expect(error.Message).toBe "Could not open the selected file."
        Vitest.expect(error.Details).toEqual (Some "disk gone")
        Vitest.expect(modalIsOpen ()).toBe false
    }
)

Vitest.test (
    "an error while the modal is open is shown inline and not passed to onError",
    fun () -> promise {
        let harness = newHarness ()

        renderWidget
            harness
            (currentAssay ())
            None
            (pickerOf (jsonOf (assayWith [ "A" ]) JsonExportFormat.ROCrate))
            (recording harness (Error(exn "boom")))

        do! openReview ()
        RTL.fireEvent.click (button "json-import-confirm")
        let! error = RTL.screen.findByTestId "json-import-error"

        Vitest.expect(error.textContent).toBe "Import failed: boom"
        Vitest.expect(harness.Errors.Count).toBe 0
    }
)

Vitest.test (
    "parsed tables start as new tables with all columns and the mode with values",
    fun () -> promise {
        let harness = newHarness ()

        renderWidget
            harness
            (currentAssay ())
            None
            (pickerOf (jsonOf (assayWith [ "A"; "B" ]) JsonExportFormat.ROCrate))
            None

        do! openReview ()

        Vitest.expect((radio 0 "ImportAsNewTable").``checked``).toBe true
        Vitest.expect((radio 1 "ImportAsNewTable").``checked``).toBe true
        openColumns 0
        openColumns 1
        Vitest.expect(checkboxes () |> Array.forall (fun box -> box.``checked``)).toBe true
        Vitest.expect(checkboxes().Length).toBe 4

        let mode =
            RTL.screen.getByTestId "table-import-mode-WithValues" :?> HTMLInputElement

        Vitest.expect(mode.``checked``).toBe true
    }
)

Vitest.test (
    "a table can be skipped and columns toggled, and tables decide independently",
    fun () -> promise {
        let harness = newHarness ()

        renderWidget
            harness
            (currentAssay ())
            None
            (pickerOf (jsonOf (assayWith [ "A"; "B" ]) JsonExportFormat.ROCrate))
            None

        do! openReview ()

        RTL.fireEvent.click (radio 0 "NoImport")
        Vitest.expect((radio 0 "NoImport").``checked``).toBe true
        Vitest.expect((radio 1 "ImportAsNewTable").``checked``).toBe true
        openColumns 0
        openColumns 1
        Vitest.expect((checkboxes ()).[0].disabled).toBe true
        Vitest.expect((checkboxes ()).[2].disabled).toBe false

        RTL.fireEvent.click ((checkboxes ()).[2])
        Vitest.expect((checkboxes ()).[2].``checked``).toBe false
        Vitest.expect((checkboxes ()).[3].``checked``).toBe true
        RTL.fireEvent.click ((checkboxes ()).[2])
        Vitest.expect((checkboxes ()).[2].``checked``).toBe true
    }
)

Vitest.test (
    "an invalid new table name disables Import",
    fun () -> promise {
        let harness = newHarness ()

        renderWidget
            harness
            (currentAssay ())
            None
            (pickerOf (jsonOf (assayWith [ "A" ]) JsonExportFormat.ROCrate))
            None

        do! openReview ()
        Vitest.expect((button "json-import-confirm").disabled).toBe false

        RTL.fireEvent.change (RTL.screen.getByLabelText "New table name", {| target = {| value = "a/b" |} |})
        Vitest.expect((button "json-import-confirm").disabled).toBe true

        RTL.fireEvent.change (RTL.screen.getByLabelText "New table name", {| target = {| value = "ab" |} |})
        Vitest.expect((button "json-import-confirm").disabled).toBe false
    }
)

Vitest.test (
    "append is disabled without an active table and enabled with one",
    fun () -> promise {
        let content = jsonOf (assayWith [ "A" ]) JsonExportFormat.ROCrate
        let harness = newHarness ()
        renderWidget harness (currentAssay ()) None (pickerOf content) None
        do! openReview ()

        Vitest.expect((radio 0 "AppendToActiveTable").disabled).toBe true
        RTL.cleanup ()

        renderWidget harness (currentAssay ()) (Some 0) (pickerOf content) None
        do! openReview ()

        Vitest.expect((radio 0 "AppendToActiveTable").disabled).toBe false
    }
)

Vitest.test (
    "Back and Cancel close the modal and leave the ARC unchanged",
    fun () -> promise {
        let harness = newHarness ()

        renderWidget
            harness
            (currentAssay ())
            None
            (pickerOf (jsonOf (assayWith [ "A" ]) JsonExportFormat.ROCrate))
            (recording harness (Ok()))

        do! openReview ()
        RTL.fireEvent.click (button "json-import-back")
        Vitest.expect(modalIsOpen ()).toBe false

        do! openReview ()
        RTL.fireEvent.click (button "json-import-cancel")
        Vitest.expect(modalIsOpen ()).toBe false

        Vitest.expect(harness.SetArcFileCalls.Count).toBe 0
        Vitest.expect(harness.Requests.Count).toBe 0
        Vitest.expect(harness.Completed).toBe 0
    }
)

Vitest.test (
    "double clicking Import calls onImportJson once",
    fun () -> promise {
        let harness = newHarness ()
        let promise', _ = pending ()

        let onImportJson (request: JsonImportRequest) =
            harness.Requests.Add request
            promise'

        renderWidget
            harness
            (currentAssay ())
            None
            (pickerOf (jsonOf (assayWith [ "A" ]) JsonExportFormat.ROCrate))
            (Some onImportJson)

        do! openReview ()
        let confirm = button "json-import-confirm"
        RTL.fireEvent.click confirm
        RTL.fireEvent.click confirm

        do! eventually (fun () -> Vitest.expect(harness.Requests.Count).toBe 1)
        do! Promise.sleep 20
        Vitest.expect(harness.Requests.Count).toBe 1
    }
)

Vitest.test (
    "while importing the modal cannot be dismissed",
    fun () -> promise {
        let harness = newHarness ()
        let promise', _ = pending ()

        renderWidget
            harness
            (currentAssay ())
            None
            (pickerOf (jsonOf (assayWith [ "A" ]) JsonExportFormat.ROCrate))
            (Some(fun _ -> promise'))

        do! openReview ()
        RTL.fireEvent.click (button "json-import-confirm")

        Vitest.expect(exists "json-import-progress").toBe true
        Vitest.expect(exists "modal-close").toBe false

        RTL.fireEvent.custom ("keyDown", RTL.screen.getByTestId "modal_json-import", {| key = "Escape" |})
        RTL.fireEvent.custom ("mouseDown", document.body)

        Vitest.expect(modalIsOpen ()).toBe true
        Vitest.expect(exists "json-import-progress").toBe true
    }
)

Vitest.test (
    "success appears after the callback resolves and Done closes the modal",
    fun () -> promise {
        let harness = newHarness ()
        let promise', resolve = pending ()

        renderWidget
            harness
            (currentAssay ())
            None
            (pickerOf (jsonOf (assayWith [ "A" ]) JsonExportFormat.ROCrate))
            (Some(fun _ -> promise'))

        do! openReview ()
        RTL.fireEvent.click (button "json-import-confirm")
        Vitest.expect(exists "json-import-success").toBe false

        resolve (Ok())
        let! success = RTL.screen.findByTestId "json-import-success"

        Vitest.expect(success.textContent).toContain "The changes are in the open ARC. Save the ARC to keep them."
        Vitest.expect(harness.Completed).toBe 0

        RTL.fireEvent.click (button "json-import-done")
        Vitest.expect(modalIsOpen ()).toBe false
        Vitest.expect(harness.Completed).toBe 1
    }
)

Vitest.test (
    "a callback error shows the error, no success, and only Close",
    fun () -> promise {
        let harness = newHarness ()

        renderWidget
            harness
            (currentAssay ())
            None
            (pickerOf (jsonOf (assayWith [ "A" ]) JsonExportFormat.ROCrate))
            (recording harness (Error(exn "boom")))

        do! openReview ()
        RTL.fireEvent.click (button "json-import-confirm")
        let! _ = RTL.screen.findByTestId "json-import-error"

        Vitest.expect(exists "json-import-success").toBe false
        Vitest.expect(exists "json-import-close").toBe true
        Vitest.expect(exists "json-import-confirm").toBe false
        Vitest.expect(harness.Completed).toBe 0
    }
)

let private replacementCases: (string * ArcFiles * JsonExportFormat)[] = [|
    "Investigation", ArcFiles.Investigation(ArcInvestigation.init "FixtureInvestigation"), JsonExportFormat.ROCrate
    "Workflow", ArcFiles.Workflow(ArcWorkflow.init "FixtureWorkflow"), JsonExportFormat.ARCtrl
    "Template", ArcFiles.Template(Template.init "FixtureTemplate"), JsonExportFormat.ARCtrl
    "DataMap", ArcFiles.DataMap(None, DataMap.init ()), JsonExportFormat.ARCtrl
|]

for label, file, format in replacementCases do
    Vitest.test (
        $"a {label} file shows the replacement confirmation instead of the table review",
        fun () -> promise {
            let harness = newHarness ()
            renderWidget harness file None (pickerOf (jsonOf file format)) None
            do! openReview ()

            Vitest.expect(exists "json-import-replace-warning").toBe true
            Vitest.expect((button "json-import-confirm").textContent).toBe "Replace"
            Vitest.expect(exists "json-import-summary").toBe false
        }
    )

for activeTableIndex in [ None; Some 0 ] do
    Vitest.test (
        $"the default import produces the tables of Json.Import.applyToCurrentArcFile (active table {activeTableIndex})",
        fun () -> promise {
            let harness = newHarness ()
            let current = currentAssay ()
            let content = jsonOf (assayWith [ "A"; "B" ]) JsonExportFormat.ROCrate

            let expected =
                match
                    Json.Import.tryParseFromJsonString (
                        content,
                        Some JsonExportFormat.ROCrate,
                        Some ArcFilesDiscriminate.Assay,
                        None
                    )
                with
                | Error exn -> failwith exn.Message
                | Ok imported ->
                    match Json.Import.applyToCurrentArcFile (current, imported) with
                    | Ok next -> next
                    | Error exn -> failwith exn.Message

            renderWidget harness current activeTableIndex (pickerOf content) None
            do! openReview ()
            RTL.fireEvent.click (button "json-import-confirm")
            let! _ = RTL.screen.findByTestId "json-import-success"
            Vitest.expect(exists "json-import-created-tables").toBe true
            Vitest.expect(exists "json-import-modified-tables").toBe false

            Vitest.expect(harness.SetArcFileCalls.Count).toBe 1

            let tableShape (file: ArcFiles) = [|
                for table in file.Tables() -> table.Name, [| for header in table.Headers -> string header |]
            |]

            Vitest.expect(tableShape harness.SetArcFileCalls.[0]).toEqual (tableShape expected)
        }
    )

let private notesCollisionAssayJson () =
    let template = ArcTable.init "Comments"

    for name in [ "A"; "B"; "C" ] do
        template.AddColumn(
            CompositeHeader.Comment name,
            ResizeArray [
                CompositeCell.FreeText $"{name}0"
                CompositeCell.FreeText $"{name}1"
            ]
        )

    // AddColumn refuses a second Comment "Notes", but a table decoded from JSON can contain one.
    let table =
        ArcTable.fromArcTableValues (
            "Comments",
            ResizeArray [
                CompositeHeader.Comment "Notes"
                CompositeHeader.Comment "Notes"
                CompositeHeader.Comment "Other"
            ],
            template.Copy().Values
        )

    let assay = ArcAssay.init "Source"
    assay.AddTable table
    jsonOf (ArcFiles.Assay assay) JsonExportFormat.ARCtrl

Vitest.test (
    "a failing apply step shows the error, keeps the configuration and allows a retry",
    fun () -> promise {
        let harness = newHarness ()
        let onImportJsonCalls = ref 0

        renderWidget
            harness
            (currentAssay ())
            None
            (pickerOf (notesCollisionAssayJson ()))
            (Some(fun _ ->
                onImportJsonCalls.Value <- onImportJsonCalls.Value + 1
                promise { return Ok() }
            ))

        RTL.fireEvent.change (RTL.screen.getByTestId "json-import-format-select", {| target = {| value = "ARCtrl" |} |})

        do! openReview ()
        openColumns 0
        // Deselecting "Other" leaves the plain copy path, so the duplicate header is rejected.
        RTL.fireEvent.click ((checkboxes ()).[2])
        RTL.fireEvent.click (button "json-import-confirm")
        let! _ = RTL.screen.findByTestId "json-import-error"

        Vitest.expect(exists "table-import-action-0-ImportAsNewTable").toBe true
        Vitest.expect((button "json-import-confirm").disabled).toBe false
        Vitest.expect(harness.Errors.Count).toBe 0
        Vitest.expect(onImportJsonCalls.Value).toBe 0
    }
)

let private fileOf (name: string) (content: string) : File =
    JsInterop.emitJsExpr (content, name) "new File([$0], $1)"

let private renderWithFileInput (harness: Harness) =
    RTL.render (
        JsonImport.JsonImport(
            currentAssay (),
            harness.SetArcFileCalls.Add,
            onError = harness.Errors.Add,
            onImportCompleted = (fun () -> harness.Completed <- harness.Completed + 1)
        )
    )
    |> ignore

let private chooseFile (file: File) =
    // The handler reads files.item 0, so a plain array is not enough.
    let files: obj =
        JsInterop.emitJsExpr file "Object.assign([$0], { item: i => [$0][i] })"

    RTL.fireEvent.change (RTL.screen.getByTestId "json-import-file-input", {| target = {| files = files |} |})

Vitest.test (
    "a file chosen with the file input opens the review",
    fun () -> promise {
        let harness = newHarness ()
        renderWithFileInput harness

        let format =
            Json.Generic.tryGetDefaultImportFormat ArcFilesDiscriminate.Assay
            |> Option.defaultValue JsonExportFormat.ARCtrl

        chooseFile (fileOf "x.json" (jsonOf (assayWith [ "A" ]) format))
        let! _ = RTL.screen.findByTestId "modal_json-import"

        Vitest.expect(harness.Errors.Count).toBe 0
    }
)

Vitest.test (
    "a file with invalid JSON from the file input reaches onError and opens no modal",
    fun () -> promise {
        let harness = newHarness ()
        renderWithFileInput harness
        chooseFile (fileOf "x.json" "not json")
        do! eventually (fun () -> Vitest.expect(harness.Errors.Count).toBe 1)

        let error = harness.Errors.[0]
        Vitest.expect(error.Title).toBe "Could not import JSON"
        Vitest.expect(error.Message).toBe "Could not read \"x.json\"."
        Vitest.expect(error.Details.IsSome).toBe true
        Vitest.expect(modalIsOpen ()).toBe false
    }
)

let private assayWithRows (tableName: string) (rowCount: int) =
    let table = ArcTable.init tableName

    let cells prefix =
        ResizeArray [
            for i in 1..rowCount -> CompositeCell.FreeText $"{prefix} {i}"
        ]

    table.AddColumn(CompositeHeader.Input IOType.Source, cells "Source")
    table.AddColumn(CompositeHeader.Output IOType.Sample, cells "Sample")
    let assay = ArcAssay.init "FixtureAssay"
    assay.AddTable table
    ArcFiles.Assay assay

Vitest.test (
    "a deselected row is not imported",
    fun () -> promise {
        let harness = newHarness ()

        renderWidget
            harness
            (currentAssay ())
            None
            (pickerOf (jsonOf (assayWithRows "A" 3) JsonExportFormat.ROCrate))
            None

        do! openReview ()
        openColumns 0
        RTL.fireEvent.click (RTL.screen.getByTestId "table-import-row-0-2")
        RTL.fireEvent.click (button "json-import-confirm")
        let! _ = RTL.screen.findByTestId "json-import-success"

        let created =
            harness.SetArcFileCalls.[0].Tables() |> Seq.find (fun table -> table.Name = "A")

        Vitest.expect(created.RowCount).toBe 2
    }
)

Vitest.test (
    "importing headers only hides the row checkboxes",
    fun () -> promise {
        let harness = newHarness ()

        renderWidget
            harness
            (currentAssay ())
            None
            (pickerOf (jsonOf (assayWithRows "A" 3) JsonExportFormat.ROCrate))
            None

        do! openReview ()
        openColumns 0
        Vitest.expect(exists "table-import-row-0-0").toBe true
        RTL.fireEvent.click (RTL.screen.getByTestId "table-import-mode-Headers")
        Vitest.expect(exists "table-import-row-0-0").toBe false
    }
)

Vitest.test (
    "unselecting all rows disables Import and shows the hint",
    fun () -> promise {
        let harness = newHarness ()

        renderWidget
            harness
            (currentAssay ())
            None
            (pickerOf (jsonOf (assayWithRows "A" 3) JsonExportFormat.ROCrate))
            None

        do! openReview ()
        RTL.fireEvent.click (RTL.screen.getByTestId "table-import-unselect-all-rows-0")

        Vitest.expect((button "json-import-confirm").disabled).toBe true
        Vitest.expect(exists "table-import-no-rows-0").toBe true
    }
)

Vitest.test (
    "the summary names the kept rows of a source with deselected rows, and only when rows are imported",
    fun () -> promise {
        let harness = newHarness ()

        renderWidget
            harness
            (currentAssay ())
            None
            (pickerOf (jsonOf (assayWithRows "A" 3) JsonExportFormat.ROCrate))
            None

        do! openReview ()

        let summary () =
            (RTL.screen.getByTestId "json-import-summary").textContent

        Vitest.expect(summary().Contains "rows)").toBe false

        openColumns 0
        RTL.fireEvent.click (RTL.screen.getByTestId "table-import-row-0-1")
        Vitest.expect(summary().Contains "(2 of 3 rows)").toBe true

        RTL.fireEvent.click (RTL.screen.getByTestId "table-import-mode-Headers")
        Vitest.expect(summary().Contains "rows)").toBe false
    }
)
