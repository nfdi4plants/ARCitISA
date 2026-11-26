namespace Arcitisa.Components.Page.ProvenanceGrouping

open System
open System.Globalization
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Arcitisa.Components.Composite.FolderedDraggableList
open Arcitisa.Components.Composite.FolderedDraggableList.Types
open Arcitisa.Components.JsBindings
open Arcitisa.Components.Shared.ProvenanceGrouping.Types
open Arcitisa.Components.Shared.ProvenanceGrouping.Grouping
open Arcitisa.Components.Shared.ProvenanceGrouping.Edit
open Arcitisa.Components.Shared.ProvenanceGrouping.Session
open Arcitisa.Components.Page.ProvenanceGrouping.Types

module PropertyShelf =

    type private FolderKey =
        | SourceFolder of ProvenanceSourceRef
        | UnknownFolder

    let private slug (value: string) =
        let text = if isNull value then "" else value.Trim()

        let chars =
            text
            |> Seq.map (fun character ->
                if Char.IsLetterOrDigit character || character = '-' || character = '_' then
                    Char.ToLowerInvariant character
                else
                    '-'
            )
            |> Seq.toArray

        let slug = String(chars).Trim('-')

        if String.IsNullOrWhiteSpace slug then "item" else slug

    let private badgeText (badge: PropertyCountBadge option) : string option =
        match badge with
        | Some PropertyCountBadge.Hide
        | None -> None
        | Some(PropertyCountBadge.DistinctValues count) -> Some(string count)
        | Some(PropertyCountBadge.Coverage(withValue, total)) -> Some($"{withValue}/{total}")

    let private headerIdentity (property: ProvenancePropertyKey) = DragDrop.propertyKeyIdentity property

    let private headerId (property: ProvenancePropertyKey) = headerIdentity property |> slug

    let private sourceSideForHeader
        (layerId: ProvenanceLayerId)
        (inputProjection: PropertyRails.RailProjection)
        (outputProjection: PropertyRails.RailProjection)
        (uiState: UiState)
        (property: ProvenancePropertyKey)
        =
        match uiState.PropertyRailPlacements |> Map.tryFind (layerId, property) with
        | Some side -> side
        | None when outputProjection.Headers |> List.contains property -> ProvenanceSide.Output
        | _ -> ProvenanceSide.Input

    let private originsForHeader
        (_layerId: ProvenanceLayerId)
        (_sourceSide: ProvenanceSide)
        (inputProjection: PropertyRails.RailProjection)
        (outputProjection: PropertyRails.RailProjection)
        (property: ProvenancePropertyKey)
        =
        [
            inputProjection.OriginByHeader |> Map.tryFind property
            outputProjection.OriginByHeader |> Map.tryFind property
        ]
        |> List.choose id
        |> List.fold Set.union Set.empty

    let private sourceOfOrigin =
        function
        | ProvenancePropertyOrigin.Real anchor
        | ProvenancePropertyOrigin.Virtual anchor -> anchor.Source

    let private folderKeyForOrigin origin = SourceFolder(sourceOfOrigin origin)

    let private folderKeyId =
        function
        | SourceFolder source -> PropertyFolders.sourceFolderId source
        | UnknownFolder -> PropertyFolders.unknownFolderId

    let private folderName =
        function
        | SourceFolder source -> source.Name
        | UnknownFolder -> "Unknown origin"

    let private folderColor (uiState: UiState) =
        function
        | SourceFolder source -> uiState.PropertyColors.SourceColors |> Map.tryFind source.Id
        | UnknownFolder -> None

    let setFolderColor (session: ProvenanceSession) folderId color state =
        let sourceRefs =
            [
                for layer in session.Layers do
                    yield layer.Model.Source

                    yield!
                        layer.Model.PropertyValues
                        |> Map.toList
                        |> List.map (snd >> fun propertyValue -> sourceOfOrigin propertyValue.Origin)

                yield!
                    state.PaletteValues
                    |> Map.toList
                    |> List.collect snd
                    |> List.map (fun propertyValue -> sourceOfOrigin propertyValue.Origin)
            ]
            |> List.distinctBy (fun source -> source.Id)

        let source =
            sourceRefs
            |> List.tryFind (fun source -> folderKeyId (SourceFolder source) = folderId)

        match source, color with
        | Some source, Some selectedColor -> State.PropertyColors.setSourceColor source.Id selectedColor state
        | Some source, None -> State.PropertyColors.clearSourceColor source.Id state
        | None, _ -> state

    let private folderSort (session: ProvenanceSession) activeLayerId key =
        let activeLayer = Session.layerById activeLayerId session

        match key with
        | SourceFolder source when source.Id = activeLayer.Model.Source.Id -> 0, 0, folderName key
        | SourceFolder source ->
            let layerIndex =
                session.LayerOrder
                |> List.tryFindIndex (fun layerId -> (Session.layerById layerId session).Model.Source.Id = source.Id)
                |> Option.defaultValue Int32.MaxValue

            1, layerIndex, folderName key
        | UnknownFolder -> 2, Int32.MaxValue, folderName key

    let private manualColor (session: ProvenanceSession) (uiState: UiState) (property: ProvenancePropertyKey) =
        let layer = Session.activeLayer session
        let colorContext = State.PropertyColors.visibleColorContextForLayer session layer

        uiState.PropertyColors.ManualPropertyColors
        |> Map.tryFind {
            ContextId = colorContext.Id
            Property = property
        }

    let private isPlacedInCurrentLayer (layer: ProvenanceLayer) (uiState: UiState) property =
        let key = State.Keys.groupingKey property

        let placedInRail = uiState.PropertyRailPlacements |> Map.containsKey (layer.Id, key)

        let groupedOnSide sideId =
            (State.Sides.get sideId uiState).GroupingAssignments
            |> List.exists (fun assignment -> assignment.Key = key)

        placedInRail
        || groupedOnSide layer.InputSideId
        || groupedOnSide layer.OutputSideId

    let private itemForHeader
        (session: ProvenanceSession)
        (sourceSide: ProvenanceSide)
        (folderKey: FolderKey)
        (inputProjection: PropertyRails.RailProjection)
        (outputProjection: PropertyRails.RailProjection)
        (uiState: UiState)
        (property: ProvenancePropertyKey)
        : FolderedDraggableItem<PropertyShelfItemPayload> =
        let header = property.Header

        let badge =
            outputProjection.BadgeByHeader
            |> Map.tryFind property
            |> Option.orElseWith (fun () -> inputProjection.BadgeByHeader |> Map.tryFind property)
            |> badgeText

        let tooltip =
            [
                ProvenanceKind.displayName header.Kind
                folderName folderKey
            ]
            |> List.filter (String.IsNullOrWhiteSpace >> not)
            |> String.concat " · "

        {
            Id = $"{folderKeyId folderKey}-{headerId property}"
            Label = header.Category.Name
            Payload = {
                Property = property
                SourceSide = sourceSide
            }
            Color = manualColor session uiState property
            Badge = badge
            Tooltip =
                if String.IsNullOrWhiteSpace tooltip then
                    None
                else
                    Some tooltip
            Disabled = false
        }

    let private dedupeItems (items: FolderedDraggableItem<PropertyShelfItemPayload> list) =
        items
        |> List.groupBy (fun item -> item.Id)
        |> List.map (fun (_, matching) -> matching.Head)

    let folders
        session
        (layer: ProvenanceLayer)
        uiState
        (inputProjection: PropertyRails.RailProjection)
        (outputProjection: PropertyRails.RailProjection)
        : FolderedDraggableFolder<PropertyShelfItemPayload> list =
        let headers =
            [
                yield! inputProjection.Headers
                yield! outputProjection.Headers
            ]
            |> List.distinct
            |> List.filter (isPlacedInCurrentLayer layer uiState >> not)

        let itemEntries =
            headers
            |> List.collect (fun property ->
                let sourceSide =
                    sourceSideForHeader layer.Id inputProjection outputProjection uiState property

                let folderKey = SourceFolder property.OriginSource

                [
                    folderKey,
                    itemForHeader session sourceSide folderKey inputProjection outputProjection uiState property
                ]
            )

        let folderKeys =
            [
                yield SourceFolder layer.Model.Source
                yield! itemEntries |> List.map fst
            ]
            |> List.distinct
            |> List.sortBy (folderSort session layer.Id)

        folderKeys
        |> List.map (fun key ->
            let items =
                itemEntries
                |> List.choose (fun (itemFolderKey, item) -> if itemFolderKey = key then Some item else None)
                |> dedupeItems

            {
                Id = folderKeyId key
                Name = folderName key
                Color = folderColor uiState key
                Items = items
            }
        )
