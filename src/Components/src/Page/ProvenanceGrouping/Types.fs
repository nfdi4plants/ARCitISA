module Arcitisa.Components.Page.ProvenanceGrouping.Types

open Fable.Core
open Arcitisa.Components.Shared.ProvenanceGrouping.Types
open Arcitisa.Components.Shared.ProvenanceGrouping.Grouping
open Arcitisa.Components.Shared.ProvenanceGrouping.Edit
open Arcitisa.Components.Shared.ProvenanceGrouping.Session
open Arcitisa.Components.Shared.ProvenanceGrouping.Fixtures

type ProvenanceEditorChange = {
    Session: ProvenanceSession
    /// The delta introduced by this one change - empty for navigation-only
    /// changes such as undo or a layer switch. Do not accumulate this across
    /// calls for writeback: `Session.PatchLog` is the authoritative, ordered
    /// log of every patch emitted so far, and it survives undo for free
    /// because undo restores a session snapshot.
    Patches: ProvenanceTablePatch list
}

type SideViewState = {
    GroupingAssignments: GroupingAssignment list
}

type ProvenanceDetail =
    | Group of side: ProvenanceSide * groupId: string
    | Connection of connectionId: string

type ValueAssignmentSource = {
    CopiedFrom: ProvenancePropertyValueId option
    Property: ProvenancePropertyKey
    Value: ProvenanceValue
    Unit: ProvenanceTerm option
}

type ValueAssignmentWarning = {
    Target: ProvenancePropertyTarget
    ExistingValueIds: ProvenancePropertyValueId list
    Header: ProvenancePropertyHeader
    Value: ProvenanceValue
    Unit: ProvenanceTerm option
}

type ValueAssignmentPlan =
    | AddCurrent of CreateLoadedPropertyValueCommand
    | ConfirmOverwrite of ValueAssignmentWarning

[<RequireQualifiedAccess>]
type ValueAssignmentError =
    | EmptyTarget
    | MixedPropertyValueCounts of ProvenancePropertyKey
    | MultiplePropertyValues of ProvenancePropertyKey * ProvenanceSetId list
    | UpstreamPropertyNotAssigned of ProvenancePropertyKey

type PropertyAssignmentBatch = {
    Adds: CreateLoadedPropertyValueCommand list
    Overwrites: ValueAssignmentWarning list
}

type PendingAssignmentBatch = {
    Batch: PropertyAssignmentBatch
    AffectedSideCount: int
    AffectedValueCount: int
    AffectedGroupCount: int
    AffectedEntityCount: int
}

type PanelRatios = { Left: int; Middle: int; Right: int }

[<RequireQualifiedAccess>]
type ConnectionHandleKind =
    | GroupCard
    | GroupMember
    | GroupMemberPropertyAnchor
    | PropertyHeader
    | PropertyValue
    /// Measurement-only anchor on the property-facing edge of a group card.
    /// Property and value connectors attach here; it is never draggable or droppable.
    | GroupPropertyAnchor

type ConnectionHandleRef = {
    Kind: ConnectionHandleKind
    Side: ProvenanceSide
    Id: string
    ParentGroupId: string option
}

type ConnectionPoint = { X: float; Y: float }

type LiveConnectionDrag = {
    Source: ConnectionHandleRef
    Start: ConnectionPoint
    Current: ConnectionPoint
}

type PendingMemberResolution = {
    LayerId: ProvenanceLayerId
    InputGroupId: string
    OutputGroupId: string
    InputMemberCount: int
    OutputMemberCount: int
}

type ProvenanceColor = string

type ProvenanceColorContextId = string

type VisiblePropertyColorContext = {
    Id: ProvenanceColorContextId
    DefaultSourceId: ProvenanceSourceId
}

type VisiblePropertyColorKey = {
    ContextId: ProvenanceColorContextId
    Property: ProvenancePropertyKey
}

type PropertyColorSettings = {
    ManualPropertyColors: Map<VisiblePropertyColorKey, ProvenanceColor>
    SourceColors: Map<ProvenanceSourceId, ProvenanceColor>
    SourceColorSetOrder: Map<ProvenanceSourceId, int>
    NextSourceColorSetOrder: int
}

[<RequireQualifiedAccess>]
type PropertySort =
    | ValueCountDesc
    | NameAsc
    | ConnectionCountDesc

[<RequireQualifiedAccess>]
type GroupSort =
    | NameAsc
    | MemberCountDesc
    | ConnectionCountDesc

[<RequireQualifiedAccess>]
type PropertyValueCountFilter =
    | Any
    | Singleton
    | Multiple
    | CoverageGap

[<RequireQualifiedAccess>]
type PropertyOriginFilter =
    | AnyOrigin
    | CurrentOnly
    | AnyUpstream
    | Source of ProvenanceSourceId

type FilterState = {
    SearchText: string
    PropertySort: PropertySort
    GroupSort: GroupSort
    ValueCountFilter: PropertyValueCountFilter
    OriginFilter: PropertyOriginFilter
}

type PropertyStats = {
    Property: ProvenancePropertyKey
    DistinctValueCount: int
    SetsWithValueCount: int
    TotalSetCount: int
}

type PropertyCountBadge =
    | Hide
    | DistinctValues of int
    | Coverage of setsWithValueCount: int * totalSetCount: int

type UiState = {
    SideStates: Map<ProvenanceLayerSideId, SideViewState>
    PropertyRailPlacements: Map<ProvenanceLayerId * GroupingKey, ProvenanceSide>
    PropertyRailOrders: Map<ProvenanceLayerId * ProvenanceSide, ProvenancePropertyKey list>
    ExpandedProperties: Set<ProvenanceLayerId * ProvenanceSide * GroupingKey>
    PaletteValues: Map<ProvenanceLayerId * ProvenanceSide, ProvenancePropertyValue list>
    PendingAssignmentBatch: PendingAssignmentBatch option
    PanelRatios: Map<ProvenanceLayerId, PanelRatios>
    PendingMemberResolution: PendingMemberResolution option
    SelectedInputs: Set<ProvenanceLayerId * string>
    SelectedOutputs: Set<ProvenanceLayerId * string>
    /// Explicitly expanded group cards. Manual toggling keeps at most one entry;
    /// manual connection resolution expands the two pending cards together.
    ExpandedGroups: Set<ProvenanceSide * string>
    Detail: ProvenanceDetail option
    Error: string option
    Hint: string option
    PropertyColors: PropertyColorSettings
    Filters: FilterState
}

/// Builds demo sessions used by ProvenanceGrouping stories and browser tests.
module StoryFixtures =

    let createSampleSession () = sampleSession ()
    let createInputOnlySession () = inputOnlyModel () |> Session.init
    let createOutputOnlySession () = outputOnlyModel () |> Session.init

    let createDisconnectedPropertySession () =
        disconnectedPropertyModel () |> Session.init

    let createSwitchablePropertySession () =
        switchablePropertyModel () |> Session.init

    let createTypedSampleSession () = typedSampleModel () |> Session.init
    let createDataOutputOnlySession () = dataOutputOnlyModel () |> Session.init

    let createRetaggedTypedSampleSession () =
        let model = typedSampleModel ()
        let propertyValue = model.PropertyValues.["pv-output-a-instrument"]

        let retagged = {
            Name = "mass spectrometer"
            TermSource = Some "MS"
            TermAccession = Some "MS:1000031"
        }

        {
            model with
                PropertyValues =
                    model.PropertyValues
                    |> Map.add propertyValue.Id {
                        propertyValue with
                            Value = ProvenanceValue.Term retagged
                    }
        }
        |> Session.init

/// Converts emitted table patches into compact strings for Storybook assertions.
module PatchPreview =

    let private valueKind value =
        match value with
        | ProvenanceValue.Text _ -> "Text"
        | ProvenanceValue.Integer _ -> "Integer"
        | ProvenanceValue.Float _ -> "Float"
        | ProvenanceValue.Term _ -> "Term"

    let private unitName (unit': ProvenanceTerm option) =
        unit' |> Option.map (fun term -> term.Name) |> Option.defaultValue "none"

    let private valueMetadata value =
        match value with
        | ProvenanceValue.Term term ->
            let source = term.TermSource |> Option.defaultValue "none"
            let accession = term.TermAccession |> Option.defaultValue "none"
            $":{source}:{accession}"
        | _ -> ""

    let patchDetails patches =
        patches
        |> List.map (
            function
            | ProvenanceTablePatch.UpdatePropertyValue(_, _, _, value, unit') ->
                $"UpdatePropertyValue:{valueKind value}:{unitName unit'}{valueMetadata value}"
            | ProvenanceTablePatch.AddLoadedSet(_, _, header, _) ->
                $"AddLoadedSet:{header.Kind.Id}:{ProvenanceKind.displayName header.Kind}"
            | ProvenanceTablePatch.AddLoadedPropertyValue(_, _, _, value, unit') ->
                $"AddLoadedPropertyValue:{valueKind value}:{unitName unit'}{valueMetadata value}"
            | ProvenanceTablePatch.AddLoadedConnection _ -> "AddLoadedConnection"
            | ProvenanceTablePatch.RemoveLoadedConnection _ -> "RemoveLoadedConnection"
        )
        |> ResizeArray

[<Mangle(false)>]
module Exports =
    let createSampleSession () = StoryFixtures.createSampleSession ()
    let createInputOnlySession () = StoryFixtures.createInputOnlySession ()

    let createOutputOnlySession () =
        StoryFixtures.createOutputOnlySession ()

    let createDisconnectedPropertySession () =
        StoryFixtures.createDisconnectedPropertySession ()

    let createSwitchablePropertySession () =
        StoryFixtures.createSwitchablePropertySession ()

    let createTypedSampleSession () =
        StoryFixtures.createTypedSampleSession ()

    let createDataOutputOnlySession () =
        StoryFixtures.createDataOutputOnlySession ()

    let createRetaggedTypedSampleSession () =
        StoryFixtures.createRetaggedTypedSampleSession ()

    let patchDetails patches = PatchPreview.patchDetails patches

    /// The authoritative writeback log for a session - use this instead of
    /// accumulating per-change `Patches` deltas host-side, so undo retracts
    /// already-emitted patches instead of leaving them stranded.
    let patchLog (session: ProvenanceSession) =
        PatchPreview.patchDetails session.PatchLog
