module ElectronRenderer.JsonImportWidgetTests

open Browser.Dom
open Browser.Types
open Fable.Core
open Feliz
open Vitest
open ARCtrl
open Swate.Components.Composite.Widgets.JsonImport.Types
open Swate.Components.Shared

let rec private waitUntil (predicate: unit -> bool, attempts: int) = promise {
    if predicate () then
        return ()
    elif attempts <= 0 then
        failwith "Timed out waiting for JSON import widget."
    else
        do! Promise.sleep 1
        return! waitUntil (predicate, attempts - 1)
}

let private renderToBody (element: ReactElement) = promise {
    let container = document.createElement ("div") :?> HTMLDivElement
    document.body.appendChild container |> ignore
    let root = ReactDOM.createRoot container
    root.render element
    do! Promise.sleep 0

    return
        container,
        (fun () ->
            root.unmount ()
            container.remove ()
        )
}

let private createAssayJson () =
    let assay = ArcAssay.init "WidgetImportAssay"
    let table = ArcTable.init "measurement"
    table.AddColumn(CompositeHeader.Input IOType.Source)
    table.AddColumn(CompositeHeader.Output IOType.Sample)
    table.AddRowsEmpty 1
    assay.AddTable table

    match Json.Export.tryParseToJsonString (ArcFiles.Assay assay, JsonExportFormat.ROCrate) with
    | Ok(_, content) -> content
    | Error exn -> failwith $"Expected test assay JSON export to succeed: {exn.Message}"

let private jsonImportWidget
    arcFile
    setArcFile
    (pickJsonFile: (unit -> JS.Promise<Result<JsonImportFile option, exn>>) option)
    (onImportJson: (JsonImportRequest -> JS.Promise<Result<unit, exn>>) option)
    (onError: JsonImportError -> unit)
    =
    Swate.Components.Composite.Widgets.JsonImport.JsonImport.JsonImport(
        arcFile,
        setArcFile,
        ?pickJsonFile = pickJsonFile,
        ?onImportJson = onImportJson,
        onError = onError
    )

Vitest.afterEach (fun () -> document.body.innerHTML <- "")

Vitest.describe (
    "JsonImport widget",
    fun () ->
        Vitest.test (
            "shows only JSON formats supported by the active file type",
            fun () -> promise {
                let! container, cleanup =
                    jsonImportWidget (ArcFiles.DataMap(None, DataMap.init ())) ignore None None ignore
                    |> renderToBody

                try
                    let optionNodes =
                        container.querySelectorAll ("[data-testid='json-import-format-select'] option")

                    Vitest.expect(optionNodes.length).toBe (1)
                    Vitest.expect(optionNodes.[0].textContent).toBe ("ARCtrl")
                finally
                    cleanup ()
            }
        )

        Vitest.test (
            "enables the file input in browser fallback mode",
            fun () -> promise {
                let assay = ArcAssay.init "FallbackAssay"

                let! container, cleanup =
                    jsonImportWidget (ArcFiles.Assay assay) ignore None None ignore |> renderToBody

                try
                    let input =
                        container.querySelector ("[data-testid='json-import-file-input']") :?> HTMLInputElement

                    Vitest.expect(input.disabled).toBe (false)
                finally
                    cleanup ()
            }
        )

        Vitest.test (
            "passes parsed ARC files from an injected picker to the import callback",
            fun () -> promise {
                let assay = ArcAssay.init "CurrentAssay"
                let assayJson = createAssayJson ()
                let mutable receivedRequest: JsonImportRequest option = None

                let pickJsonFile () = promise {
                    return
                        Ok(
                            Some {
                                FileName = Some "assay.json"
                                Content = assayJson
                            }
                        )
                }

                let onImportJson request = promise {
                    receivedRequest <- Some request
                    return Ok()
                }

                let! container, cleanup =
                    jsonImportWidget (ArcFiles.Assay assay) ignore (Some pickJsonFile) (Some onImportJson) ignore
                    |> renderToBody

                try
                    let button =
                        container.querySelector ("[data-testid='json-import-picker-button']") :?> HTMLButtonElement

                    button.click ()

                    do!
                        waitUntil (
                            (fun () -> not (isNull (document.querySelector "[data-testid='json-import-confirm']"))),
                            100
                        )

                    let confirm =
                        document.querySelector "[data-testid='json-import-confirm']" :?> HTMLButtonElement

                    confirm.click ()
                    do! waitUntil ((fun () -> receivedRequest.IsSome), 100)

                    match receivedRequest with
                    | Some request ->
                        Vitest.expect(request.SourceFileName).toEqual (Some "assay.json")
                        Vitest.expect(request.JsonFormat).toEqual (JsonExportFormat.ROCrate)

                        match request.ImportedFile with
                        | ArcFiles.Assay importedAssay ->
                            Vitest.expect(importedAssay.Identifier).toBe ("WidgetImportAssay")
                        | importedFile ->
                            failwith
                                $"Expected imported file to be an Assay, got {importedFile.RelatedArcFilesDiscriminate}."
                    | None -> failwith "Expected import callback to receive a parsed request."
                finally
                    cleanup ()
            }
        )

        Vitest.test (
            "treats picker cancel as a no-op",
            fun () -> promise {
                let assay = ArcAssay.init "CurrentAssay"
                let mutable importCalled = false
                let mutable errorCalled = false

                let pickJsonFile () = promise { return Ok None }

                let onImportJson _ = promise {
                    importCalled <- true
                    return Ok()
                }

                let! container, cleanup =
                    jsonImportWidget
                        (ArcFiles.Assay assay)
                        ignore
                        (Some pickJsonFile)
                        (Some onImportJson)
                        (fun _ -> errorCalled <- true)
                    |> renderToBody

                try
                    let button =
                        container.querySelector ("[data-testid='json-import-picker-button']") :?> HTMLButtonElement

                    button.click ()
                    do! Promise.sleep 10

                    Vitest.expect(importCalled).toBe (false)
                    Vitest.expect(errorCalled).toBe (false)
                finally
                    cleanup ()
            }
        )

        Vitest.test (
            "calls onError for parse failures from an injected picker",
            fun () -> promise {
                let assay = ArcAssay.init "CurrentAssay"
                let mutable observedError: JsonImportError option = None

                let pickJsonFile () = promise {
                    return
                        Ok(
                            Some {
                                FileName = Some "broken.json"
                                Content = "not json"
                            }
                        )
                }

                let! container, cleanup =
                    jsonImportWidget
                        (ArcFiles.Assay assay)
                        ignore
                        (Some pickJsonFile)
                        None
                        (fun error -> observedError <- Some error)
                    |> renderToBody

                try
                    let button =
                        container.querySelector ("[data-testid='json-import-picker-button']") :?> HTMLButtonElement

                    button.click ()
                    do! waitUntil ((fun () -> observedError.IsSome), 100)

                    Vitest.expect(observedError.IsSome).toBe (true)
                finally
                    cleanup ()
            }
        )

        Vitest.test (
            "picking a file opens the review modal without calling the import callback",
            fun () -> promise {
                let mutable importCalled = false

                let pickJsonFile () = promise {
                    return
                        Ok(
                            Some {
                                FileName = Some "assay.json"
                                Content = createAssayJson ()
                            }
                        )
                }

                let onImportJson _ = promise {
                    importCalled <- true
                    return Ok()
                }

                let! container, cleanup =
                    jsonImportWidget
                        (ArcFiles.Assay(ArcAssay.init "CurrentAssay"))
                        ignore
                        (Some pickJsonFile)
                        (Some onImportJson)
                        ignore
                    |> renderToBody

                try
                    (container.querySelector ("[data-testid='json-import-picker-button']") :?> HTMLButtonElement)
                        .click ()

                    do!
                        waitUntil (
                            (fun () -> not (isNull (document.querySelector "[data-testid='modal_json-import']"))),
                            100
                        )

                    Vitest.expect(importCalled).toBe (false)
                finally
                    cleanup ()
            }
        )

        Vitest.test (
            "shows the success view only after the import callback resolves",
            fun () -> promise {
                let mutable receivedRequest: JsonImportRequest option = None
                let mutable resolve: Result<unit, exn> -> unit = ignore

                let pickJsonFile () = promise {
                    return
                        Ok(
                            Some {
                                FileName = Some "assay.json"
                                Content = createAssayJson ()
                            }
                        )
                }

                let onImportJson request =
                    receivedRequest <- Some request
                    Promise.create (fun res _ -> resolve <- res)

                let! container, cleanup =
                    jsonImportWidget
                        (ArcFiles.Assay(ArcAssay.init "CurrentAssay"))
                        ignore
                        (Some pickJsonFile)
                        (Some onImportJson)
                        ignore
                    |> renderToBody

                let exists testId =
                    not (isNull (document.querySelector $"[data-testid='{testId}']"))

                try
                    (container.querySelector ("[data-testid='json-import-picker-button']") :?> HTMLButtonElement)
                        .click ()

                    do! waitUntil ((fun () -> exists "json-import-confirm"), 100)
                    (document.querySelector "[data-testid='json-import-confirm']" :?> HTMLButtonElement).click ()
                    do! waitUntil ((fun () -> receivedRequest.IsSome), 100)
                    do! Promise.sleep 10

                    Vitest.expect(exists "json-import-success").toBe (false)
                    Vitest.expect(exists "json-import-progress").toBe (true)

                    match receivedRequest.Value.NextArcFile with
                    | ArcFiles.Assay next ->
                        Vitest.expect(next.Tables |> Seq.map _.Name |> Seq.toArray).toEqual ([| "measurement" |])
                    | _ -> failwith "Expected NextArcFile to be an Assay."

                    resolve (Ok())
                    do! waitUntil ((fun () -> exists "json-import-success"), 100)
                finally
                    cleanup ()
            }
        )

        Vitest.test (
            "a failing import callback shows the error and no success view",
            fun () -> promise {
                let pickJsonFile () = promise {
                    return
                        Ok(
                            Some {
                                FileName = Some "assay.json"
                                Content = createAssayJson ()
                            }
                        )
                }

                let onImportJson _ = promise { return Error(exn "Callback failed") }

                let! container, cleanup =
                    jsonImportWidget
                        (ArcFiles.Assay(ArcAssay.init "CurrentAssay"))
                        ignore
                        (Some pickJsonFile)
                        (Some onImportJson)
                        ignore
                    |> renderToBody

                let exists testId =
                    not (isNull (document.querySelector $"[data-testid='{testId}']"))

                try
                    (container.querySelector ("[data-testid='json-import-picker-button']") :?> HTMLButtonElement)
                        .click ()

                    do! waitUntil ((fun () -> exists "json-import-confirm"), 100)
                    (document.querySelector "[data-testid='json-import-confirm']" :?> HTMLButtonElement).click ()
                    do! waitUntil ((fun () -> exists "json-import-error"), 100)

                    Vitest.expect(exists "json-import-success").toBe (false)
                    Vitest.expect(exists "json-import-close").toBe (true)
                    Vitest.expect(exists "json-import-confirm").toBe (false)
                finally
                    cleanup ()
            }
        )

        Vitest.test (
            "a parse error reaches onError with the parser message and opens no modal",
            fun () -> promise {
                let content = "not json"
                let mutable observedError: JsonImportError option = None

                let pickJsonFile () = promise {
                    return
                        Ok(
                            Some {
                                FileName = Some "broken.json"
                                Content = content
                            }
                        )
                }

                let expectedDetails =
                    match
                        Json.Import.tryParseFromJsonString (
                            content,
                            Some JsonExportFormat.ROCrate,
                            Some ArcFilesDiscriminate.Assay,
                            Some "broken.json"
                        )
                    with
                    | Error exn -> exn.Message
                    | Ok _ -> failwith "Expected the test input to fail parsing."

                let! container, cleanup =
                    jsonImportWidget
                        (ArcFiles.Assay(ArcAssay.init "CurrentAssay"))
                        ignore
                        (Some pickJsonFile)
                        None
                        (fun error -> observedError <- Some error)
                    |> renderToBody

                try
                    (container.querySelector ("[data-testid='json-import-picker-button']") :?> HTMLButtonElement)
                        .click ()

                    do! waitUntil ((fun () -> observedError.IsSome), 100)

                    Vitest.expect(observedError.Value.Title).toBe ("Could not import JSON")
                    Vitest.expect(observedError.Value.Details).toEqual (Some expectedDetails)
                    Vitest.expect(isNull (document.querySelector "[data-testid='modal_json-import']")).toBe (true)
                finally
                    cleanup ()
            }
        )

        Vitest.test (
            "a file without tables reaches onError and opens no modal",
            fun () -> promise {
                let emptyAssayJson =
                    match
                        Json.Export.tryParseToJsonString (
                            ArcFiles.Assay(ArcAssay.init "EmptyAssay"),
                            JsonExportFormat.ROCrate
                        )
                    with
                    | Ok(_, content) -> content
                    | Error exn -> failwith exn.Message

                let mutable observedError: JsonImportError option = None

                let pickJsonFile () = promise {
                    return
                        Ok(
                            Some {
                                FileName = Some "empty.json"
                                Content = emptyAssayJson
                            }
                        )
                }

                let! container, cleanup =
                    jsonImportWidget
                        (ArcFiles.Assay(ArcAssay.init "CurrentAssay"))
                        ignore
                        (Some pickJsonFile)
                        None
                        (fun error -> observedError <- Some error)
                    |> renderToBody

                try
                    (container.querySelector ("[data-testid='json-import-picker-button']") :?> HTMLButtonElement)
                        .click ()

                    do! waitUntil ((fun () -> observedError.IsSome), 100)

                    Vitest.expect(observedError.Value.Message).toBe ("\"empty.json\" contains no tables to import.")
                    Vitest.expect(observedError.Value.Details).toEqual (None)
                    Vitest.expect(isNull (document.querySelector "[data-testid='modal_json-import']")).toBe (true)
                finally
                    cleanup ()
            }
        )
)
