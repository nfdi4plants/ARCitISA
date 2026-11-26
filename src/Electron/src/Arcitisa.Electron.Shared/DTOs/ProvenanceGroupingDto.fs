module Arcitisa.Electron.Shared.DTOs.ProvenanceGroupingDto

open Arcitisa.Components.Shared.ProvenanceGrouping.Types
open Arcitisa.Components.Shared.ProvenanceGrouping.ARCtrlConverter

[<RequireQualifiedAccess>]
type ProvenanceTableScopeDto =
    | Study
    | Assay
    | Run

type ProvenanceTableSelectionDto = {
    Scope: ProvenanceTableScopeDto
    ParentIdentifier: string
    TableName: string
    DisplayLabel: string
}

type ProvenanceMapEntryDto<'T> = { Key: string; Value: 'T }

type ProvenanceTermDto = {
    Name: string
    TermSource: string option
    TermAccession: string option
}

type ProvenanceKindDto = { Id: string; Label: string }

type ProvenanceIOHeaderDto = {
    Kind: ProvenanceKindDto
    Text: string
}

type ProvenancePropertyHeaderDto = {
    Kind: ProvenanceKindDto
    Category: ProvenanceTermDto
}

type ProvenanceValueDto = {
    Kind: string
    Text: string option
    Integer: int option
    Float: float option
    Term: ProvenanceTermDto option
}

type ProvenanceSourceRefDto = {
    Id: ProvenanceSourceId
    Name: ProvenanceSourceName
}

type ProvenanceWritebackAnchorDto = {
    Source: ProvenanceSourceRefDto
    ProcessId: ProvenanceProcessId option
    ProcessName: ProvenanceProcessName option
    Header: ProvenancePropertyHeaderDto
    InputNames: string[]
    OutputNames: string[]
}

type ProvenancePropertyOriginDto = {
    IsReal: bool
    Anchor: ProvenanceWritebackAnchorDto
}

type ProvenancePropertyValueDto = {
    Id: ProvenancePropertyValueId
    Header: ProvenancePropertyHeaderDto
    Value: ProvenanceValueDto
    Unit: ProvenanceTermDto option
    Origin: ProvenancePropertyOriginDto
}

type ProvenanceSetDto = {
    Id: ProvenanceSetId
    Source: ProvenanceSourceRefDto
    Header: ProvenanceIOHeaderDto
    Name: string
    PropertyValueIds: ProvenancePropertyValueId[]
    InheritedPropertyValueIds: ProvenanceMapEntryDto<ProvenancePropertyValueId[]>[]
}

type ProvenanceConnectionDto = {
    Id: ProvenanceConnectionId
    Source: ProvenanceSourceRefDto
    ProcessId: ProvenanceProcessId option
    ProcessName: ProvenanceProcessName option
    InputSetId: ProvenanceSetId
    OutputSetId: ProvenanceSetId
}

type ProvenanceModelDto = {
    Source: ProvenanceSourceRefDto
    PropertyValues: ProvenanceMapEntryDto<ProvenancePropertyValueDto>[]
    InputSets: ProvenanceMapEntryDto<ProvenanceSetDto>[]
    OutputSets: ProvenanceMapEntryDto<ProvenanceSetDto>[]
    Connections: ProvenanceMapEntryDto<ProvenanceConnectionDto>[]
}

type ProvenanceLoadResultDto = {
    Selection: ProvenanceTableSelectionDto
    Model: ProvenanceModelDto
    Warnings: string[]
}

module private MapDto =

    let ofMap convert (map: Map<string, 'T>) =
        map
        |> Map.toArray
        |> Array.map (fun (key, value) -> { Key = key; Value = convert value })

    let toMap convert entries =
        entries
        |> Array.map (fun entry -> entry.Key, convert entry.Value)
        |> Map.ofArray

module ProvenanceTermDto =

    let ofModel (term: ProvenanceTerm) = {
        Name = term.Name
        TermSource = term.TermSource
        TermAccession = term.TermAccession
    }

    let toModel (term: ProvenanceTermDto) : ProvenanceTerm = {
        Name = term.Name
        TermSource = term.TermSource
        TermAccession = term.TermAccession
    }

module ProvenanceKindDto =

    let ofModel (kind: ProvenanceKind) = { Id = kind.Id; Label = kind.Label }

    let toModel (kind: ProvenanceKindDto) : ProvenanceKind = { Id = kind.Id; Label = kind.Label }

module ProvenanceIOHeaderDto =

    let ofModel (header: ProvenanceIOHeader) = {
        Kind = ProvenanceKindDto.ofModel header.Kind
        Text = header.Text
    }

    let toModel (header: ProvenanceIOHeaderDto) : ProvenanceIOHeader = {
        Kind = ProvenanceKindDto.toModel header.Kind
        Text = header.Text
    }

module ProvenancePropertyHeaderDto =

    let ofModel (header: ProvenancePropertyHeader) = {
        Kind = ProvenanceKindDto.ofModel header.Kind
        Category = ProvenanceTermDto.ofModel header.Category
    }

    let toModel (header: ProvenancePropertyHeaderDto) : ProvenancePropertyHeader = {
        Kind = ProvenanceKindDto.toModel header.Kind
        Category = ProvenanceTermDto.toModel header.Category
    }

module ProvenanceValueDto =

    let private empty kind = {
        Kind = kind
        Text = None
        Integer = None
        Float = None
        Term = None
    }

    let ofModel value =
        match value with
        | ProvenanceValue.Text text -> { empty "Text" with Text = Some text }
        | ProvenanceValue.Integer integer -> {
            empty "Integer" with
                Integer = Some integer
          }
        | ProvenanceValue.Float float -> {
            empty "Float" with
                Float = Some float
          }
        | ProvenanceValue.Term term -> {
            empty "Term" with
                Term = Some(ProvenanceTermDto.ofModel term)
          }

    let toModel value =
        match value.Kind with
        | "Integer" ->
            value.Integer
            |> Option.map ProvenanceValue.Integer
            |> Option.defaultValue (ProvenanceValue.Text "")
        | "Float" ->
            value.Float
            |> Option.map ProvenanceValue.Float
            |> Option.defaultValue (ProvenanceValue.Text "")
        | "Term" ->
            value.Term
            |> Option.map (ProvenanceTermDto.toModel >> ProvenanceValue.Term)
            |> Option.defaultValue (ProvenanceValue.Text "")
        | "Text"
        | _ -> value.Text |> Option.defaultValue "" |> ProvenanceValue.Text

module ProvenanceSourceRefDto =

    let ofModel (source: ProvenanceSourceRef) = { Id = source.Id; Name = source.Name }

    let toModel (source: ProvenanceSourceRefDto) : ProvenanceSourceRef = { Id = source.Id; Name = source.Name }

module ProvenanceWritebackAnchorDto =

    let ofModel (source: ProvenanceWritebackAnchor) = {
        Source = ProvenanceSourceRefDto.ofModel source.Source
        ProcessId = source.ProcessId
        ProcessName = source.ProcessName
        Header = ProvenancePropertyHeaderDto.ofModel source.Header
        InputNames = source.InputNames |> List.toArray
        OutputNames = source.OutputNames |> List.toArray
    }

    let toModel (source: ProvenanceWritebackAnchorDto) : ProvenanceWritebackAnchor = {
        Source = ProvenanceSourceRefDto.toModel source.Source
        ProcessId = source.ProcessId
        ProcessName = source.ProcessName
        Header = ProvenancePropertyHeaderDto.toModel source.Header
        InputNames = source.InputNames |> Array.toList
        OutputNames = source.OutputNames |> Array.toList
    }

module ProvenancePropertyOriginDto =

    let ofModel =
        function
        | ProvenancePropertyOrigin.Real anchor -> {
            IsReal = true
            Anchor = ProvenanceWritebackAnchorDto.ofModel anchor
          }
        | ProvenancePropertyOrigin.Virtual anchor -> {
            IsReal = false
            Anchor = ProvenanceWritebackAnchorDto.ofModel anchor
          }

    let toModel origin =
        let anchor = ProvenanceWritebackAnchorDto.toModel origin.Anchor

        if origin.IsReal then
            ProvenancePropertyOrigin.Real anchor
        else
            ProvenancePropertyOrigin.Virtual anchor

module ProvenancePropertyValueDto =

    let ofModel (propertyValue: ProvenancePropertyValue) = {
        Id = propertyValue.Id
        Header = ProvenancePropertyHeaderDto.ofModel propertyValue.Header
        Value = ProvenanceValueDto.ofModel propertyValue.Value
        Unit = propertyValue.Unit |> Option.map ProvenanceTermDto.ofModel
        Origin = ProvenancePropertyOriginDto.ofModel propertyValue.Origin
    }

    let toModel (propertyValue: ProvenancePropertyValueDto) : ProvenancePropertyValue = {
        Id = propertyValue.Id
        Header = ProvenancePropertyHeaderDto.toModel propertyValue.Header
        Value = ProvenanceValueDto.toModel propertyValue.Value
        Unit = propertyValue.Unit |> Option.map ProvenanceTermDto.toModel
        Origin = ProvenancePropertyOriginDto.toModel propertyValue.Origin
    }

module ProvenanceSetDto =

    let ofModel (set: ProvenanceSet) = {
        Id = set.Id
        Source = ProvenanceSourceRefDto.ofModel set.Source
        Header = ProvenanceIOHeaderDto.ofModel set.Header
        Name = set.Name
        PropertyValueIds = set.PropertyValueIds |> List.toArray
        InheritedPropertyValueIds = set.InheritedPropertyValueIds |> MapDto.ofMap List.toArray
    }

    let toModel (set: ProvenanceSetDto) : ProvenanceSet = {
        Id = set.Id
        Source = ProvenanceSourceRefDto.toModel set.Source
        Header = ProvenanceIOHeaderDto.toModel set.Header
        Name = set.Name
        PropertyValueIds = set.PropertyValueIds |> Array.toList
        InheritedPropertyValueIds = set.InheritedPropertyValueIds |> MapDto.toMap Array.toList
    }

module ProvenanceConnectionDto =

    let ofModel (connection: ProvenanceConnection) = {
        Id = connection.Id
        Source = ProvenanceSourceRefDto.ofModel connection.Source
        ProcessId = connection.ProcessId
        ProcessName = connection.ProcessName
        InputSetId = connection.InputSetId
        OutputSetId = connection.OutputSetId
    }

    let toModel (connection: ProvenanceConnectionDto) : ProvenanceConnection = {
        Id = connection.Id
        Source = ProvenanceSourceRefDto.toModel connection.Source
        ProcessId = connection.ProcessId
        ProcessName = connection.ProcessName
        InputSetId = connection.InputSetId
        OutputSetId = connection.OutputSetId
    }

module ProvenanceModelDto =

    let ofModel (model: ProvenanceModel) = {
        Source = ProvenanceSourceRefDto.ofModel model.Source
        PropertyValues = model.PropertyValues |> MapDto.ofMap ProvenancePropertyValueDto.ofModel
        InputSets = model.InputSets |> MapDto.ofMap ProvenanceSetDto.ofModel
        OutputSets = model.OutputSets |> MapDto.ofMap ProvenanceSetDto.ofModel
        Connections = model.Connections |> MapDto.ofMap ProvenanceConnectionDto.ofModel
    }

    let toModel (model: ProvenanceModelDto) : ProvenanceModel = {
        Source = ProvenanceSourceRefDto.toModel model.Source
        PropertyValues = model.PropertyValues |> MapDto.toMap ProvenancePropertyValueDto.toModel
        InputSets = model.InputSets |> MapDto.toMap ProvenanceSetDto.toModel
        OutputSets = model.OutputSets |> MapDto.toMap ProvenanceSetDto.toModel
        Connections = model.Connections |> MapDto.toMap ProvenanceConnectionDto.toModel
    }

module ProvenanceTableSelectionDto =

    let private scopeText scope =
        match scope with
        | ProvenanceTableScopeDto.Study -> "Study"
        | ProvenanceTableScopeDto.Assay -> "Assay"
        | ProvenanceTableScopeDto.Run -> "Run"

    let private toArcScope scope =
        match scope with
        | ProvenanceTableScopeDto.Study -> ArcTableScope.Study
        | ProvenanceTableScopeDto.Assay -> ArcTableScope.Assay
        | ProvenanceTableScopeDto.Run -> ArcTableScope.Run

    let private ofArcScope scope =
        match scope with
        | ArcTableScope.Study -> ProvenanceTableScopeDto.Study
        | ArcTableScope.Assay -> ProvenanceTableScopeDto.Assay
        | ArcTableScope.Run -> ProvenanceTableScopeDto.Run

    let create scope parentIdentifier tableName = {
        Scope = scope
        ParentIdentifier = parentIdentifier
        TableName = tableName
        DisplayLabel = $"{scopeText scope} {parentIdentifier} / {tableName}"
    }

    let toArcLocation selection : ArcTableLocation = {
        Scope = toArcScope selection.Scope
        ParentIdentifier = selection.ParentIdentifier
        TableName = selection.TableName
    }

    let fromArcLocation (location: ArcTableLocation) =
        create (ofArcScope location.Scope) location.ParentIdentifier location.TableName
