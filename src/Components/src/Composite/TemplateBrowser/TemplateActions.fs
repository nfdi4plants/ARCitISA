module Swate.Components.Composite.Template.TemplateActions

open ARCtrl
open Swate.Components.Shared
open Swate.Components.Composite.TableImport.Types

/// Syncs the selected template IDs with the currently loaded templates, removing any IDs that no longer exist in the loaded templates.
let syncSelectedTemplateIds
    (loadedTemplates: Template[])
    (setSelectedTemplateIds: (Set<System.Guid> -> Set<System.Guid>) -> unit)
    =
    let templateIds =
        loadedTemplates |> Seq.map (fun template -> template.Id) |> Set.ofSeq

    setSelectedTemplateIds (fun selected -> selected |> Set.intersect templateIds)
    |> ignore

let toggleTemplateSelection (templateId: System.Guid) (selectedTemplateIds: Set<System.Guid>) =
    let isCurrentlySelected = selectedTemplateIds.Contains templateId

    if isCurrentlySelected then
        selectedTemplateIds.Remove templateId
    else
        selectedTemplateIds.Add templateId

/// The import sources of the selected templates. The index of a source is the position of its template.
let importSources (templates: Template[]) : ImportSource[] =
    templates
    |> Array.mapi (fun index template -> {
        Index = index
        Name = template.Name
        Table = template.Table
    })

/// The decisions the template import starts from. The caller sets ActiveTableIndex.
let defaultDecisions: TableImportDecisions = {
    ImportType = TableJoinOptions.Headers
    DefaultAction = TableImportAction.AppendToActiveTable
    Actions = Map.empty
    DeselectedColumns = Set.empty
    DeselectedRows = Set.empty
    EnteredNames = Map.empty
    ActiveTableIndex = None
}

/// Moves the choices of the templates that stay selected to their new positions and drops the others.
/// The import dialog stays mounted while the selection changes, so its decisions have to follow the templates.
let remapDecisions
    (previousIds: System.Guid[])
    (nextIds: System.Guid[])
    (decisions: TableImportDecisions)
    : TableImportDecisions =
    let newIndex (index: int) =
        if index < 0 || index >= previousIds.Length then
            None
        else
            nextIds |> Array.tryFindIndex ((=) previousIds.[index])

    let remapPair (index: int, value: int) =
        newIndex index |> Option.map (fun next -> next, value)

    let remapMap (map: Map<int, 'a>) =
        map
        |> Map.toSeq
        |> Seq.choose (fun (index, value) -> newIndex index |> Option.map (fun next -> next, value))
        |> Map.ofSeq

    {
        decisions with
            Actions = remapMap decisions.Actions
            DeselectedColumns = decisions.DeselectedColumns |> Seq.choose remapPair |> Set.ofSeq
            DeselectedRows = decisions.DeselectedRows |> Seq.choose remapPair |> Set.ofSeq
            EnteredNames = remapMap decisions.EnteredNames
    }

let importIntoCopy
    (arcFile: ArcFiles)
    (templates: Template[])
    (decisions: TableImportDecisions)
    : Result<ArcFiles, exn> =
    Swate.Components.Composite.TableImport.Helper.applyImport arcFile (importSources templates) decisions
    |> Result.map _.ArcFile
