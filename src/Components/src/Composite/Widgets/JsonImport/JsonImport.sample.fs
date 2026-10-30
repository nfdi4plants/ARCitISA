module internal Swate.Components.Composite.Widgets.JsonImport.JsonImportSample

open ARCtrl
open Fable.Core
open Feliz
open Swate.Components.Shared
open Swate.Components.Composite.TableImport.TableImportPreviewSample
open Swate.Components.Composite.Widgets.JsonImport.Types

let private currentAssay =
    let assay = ArcAssay.init "CurrentAssay"
    assay.AddTable(buildTable "Existing" 3)
    ArcFiles.Assay assay

let private parsedAssay: ParsedJsonFile =
    let assay = ArcAssay.init "ImportedAssay"
    assay.AddTable(buildTable "Measurement" 20)
    assay.AddTable(buildTable "Calibration" 20)
    assay.AddTable(buildTable "Growth" 20)

    {
        Id = 1
        File = ArcFiles.Assay assay
        FileName = Some "assay.json"
        Format = JsonExportFormat.ROCrate
    }

let private currentInvestigation =
    ArcFiles.Investigation(ArcInvestigation.init "CurrentInvestigation")

let private parsedInvestigation: ParsedJsonFile = {
    Id = 2
    File = ArcFiles.Investigation(ArcInvestigation.init "ImportedInvestigation")
    FileName = Some "investigation.json"
    Format = JsonExportFormat.ROCrate
}

let private modal
    (parsed: ParsedJsonFile)
    (current: ArcFiles)
    (importJson: JsonImportRequest -> JS.Promise<Result<unit, exn>>)
    =
    JsonImportModal.JsonImportModal(
        parsed,
        current,
        Some 0,
        importJson,
        onBack = ignore,
        onCancel = ignore,
        onDone = ignore
    )

[<Erase; Mangle(false)>]
type JsonImportSample =

    [<ReactComponent>]
    static member ReviewSample() =
        modal parsedAssay currentAssay (fun _ -> promise { return Ok() })

    [<ReactComponent>]
    static member ReplacementSample() =
        modal parsedInvestigation currentInvestigation (fun _ -> promise { return Ok() })

    [<ReactComponent>]
    static member ImportingSample() =
        modal parsedAssay currentAssay (fun _ -> Promise.create (fun _ _ -> ()))

    [<ReactComponent>]
    static member SuccessSample() =
        modal parsedAssay currentAssay (fun _ -> promise { return Ok() })

    [<ReactComponent>]
    static member ErrorSample() =
        modal parsedAssay currentAssay (fun _ -> promise { return Error(exn "The ARC could not be updated.") })
