module Swate.Components.Composite.Template.TemplateActions

open ARCtrl
open Swate.Components.Shared
open Swate.Components.Composite.Template.Types
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

let getTemplateImportAction (templateId: System.Guid) (templateImportDecisions: Map<System.Guid, TableImportAction>) =
    templateImportDecisions
    |> Map.tryFind templateId
    |> Option.defaultValue TableImportAction.AppendToActiveTable

let setTemplateImportAction
    (templateId: System.Guid)
    (importAction: TableImportAction)
    (setTemplateImportDecisions: (Map<System.Guid, TableImportAction> -> Map<System.Guid, TableImportAction>) -> unit)
    =
    setTemplateImportDecisions (fun decisions -> decisions.Add(templateId, importAction))
    |> ignore

let isTemplateColumnSelected
    (templateId: System.Guid)
    (columnIndex: int)
    (deselectedTemplateColumns: Set<System.Guid * int>)
    =
    deselectedTemplateColumns.Contains(templateId, columnIndex) |> not

let toggleTemplateColumnSelection
    (templateId: System.Guid)
    (columnIndex: int)
    (setDeselectedTemplateColumns: (Set<System.Guid * int> -> Set<System.Guid * int>) -> unit)
    =
    setDeselectedTemplateColumns (fun deselected ->
        if deselected.Contains(templateId, columnIndex) then
            deselected.Remove(templateId, columnIndex)
        else
            deselected.Add(templateId, columnIndex)
    )
    |> ignore

let selectAllTemplateColumns
    (templateId: System.Guid)
    (setDeselectedTemplateColumns: (Set<System.Guid * int> -> Set<System.Guid * int>) -> unit)
    =
    setDeselectedTemplateColumns (fun deselected ->
        deselected
        |> Set.filter (fun (candidateTemplateId, _) -> candidateTemplateId <> templateId)
    )
    |> ignore

let unselectAllTemplateColumns
    (template: Template)
    (setDeselectedTemplateColumns: (Set<System.Guid * int> -> Set<System.Guid * int>) -> unit)
    =
    let allTemplateColumns =
        template.Table.Columns
        |> Seq.mapi (fun columnIndex _ -> template.Id, columnIndex)
        |> Set.ofSeq

    setDeselectedTemplateColumns (fun deselected -> Set.union deselected allTemplateColumns)
    |> ignore

let selectedTemplatesForImport
    (templates: Template[])
    (selectedTemplateIds: Set<System.Guid>)
    (templateImportDecisions: Map<System.Guid, TableImportAction>)
    =
    templates
    |> Array.filter (fun template -> selectedTemplateIds.Contains template.Id)
    |> Array.choose (fun template ->
        match getTemplateImportAction template.Id templateImportDecisions with
        | TableImportAction.NoImport -> None
        | nextAction -> Some(template, nextAction)
    )

let newTableNames
    (existingTableNames: string seq)
    (selectedTemplatesForImport: (Template * TableImportAction)[])
    (enteredNames: Map<System.Guid, string>)
    =
    let newTableTemplates =
        selectedTemplatesForImport
        |> Array.choose (fun (template, action) ->
            if action = TableImportAction.ImportAsNewTable then
                Some template
            else
                None
        )

    newTableTemplates
    |> Array.map (fun template -> template.Table.Name, enteredNames.TryFind template.Id)
    |> resolveNewTableNames existingTableNames
    |> Array.zip newTableTemplates
    |> Array.map (fun (template, newTableName) -> template.Id, newTableName)
    |> Map.ofArray

let importIntoCopy (arcFile: ArcFiles) (activeTableIndex: int option) (payload: ImportModalConfirmPayload) =
    let selected = payload.SelectedTemplatesForImport

    let sources =
        selected
        |> Array.mapi (fun index (template, _) -> {
            Index = index
            Name = template.Name
            Table = template.Table
        })

    let indexById =
        selected
        |> Array.mapi (fun index (template, _) -> template.Id, index)
        |> Map.ofArray

    let decisions: TableImportDecisions = {
        ImportType = payload.ImportType
        DefaultAction = TableImportAction.NoImport
        Actions = selected |> Array.mapi (fun index (_, action) -> index, action) |> Map.ofArray
        DeselectedColumns =
            payload.DeselectedTemplateColumns
            |> Seq.choose (fun (templateId, columnIndex) ->
                indexById.TryFind templateId |> Option.map (fun index -> index, columnIndex)
            )
            |> Set.ofSeq
        EnteredNames =
            selected
            |> Array.mapi (fun index (template, action) -> index, template, action)
            |> Array.choose (fun (index, template, action) ->
                if action = TableImportAction.ImportAsNewTable then
                    payload.NewTableNames.TryFind template.Id
                    |> Option.map (fun name -> index, name.Trim())
                else
                    None
            )
            |> Map.ofArray
        ActiveTableIndex = activeTableIndex
    }

    match Swate.Components.Composite.TableImport.Helper.applyImport arcFile sources decisions with
    | Ok result -> result.ArcFile
    | Error error -> raise error
