namespace Arcitisa.Components.Page.ProvenanceGrouping

[<Fable.Core.Mangle(false)>]
module Exports =

    open Arcitisa.Components.Shared.ProvenanceGrouping.Types
    open Arcitisa.Components.Shared.ProvenanceGrouping.Session
    open Arcitisa.Components.Page.ProvenanceGrouping.Types

    let private sideFromName =
        function
        | "Input" -> ProvenanceSide.Input
        | "Output" -> ProvenanceSide.Output
        | side -> failwithf "Unknown provenance side '%s'." side

    let private propertyByName propertyName (model: ProvenanceModel) =
        model.PropertyValues
        |> Map.toList
        |> List.map (snd >> ProvenancePropertyValue.propertyKey)
        |> List.distinct
        |> List.find (fun property -> property.Header.Category.Name = propertyName)

    let sampleDroppedPropertyRailColor sideName propertyName layerColor =
        let session = StoryFixtures.createSampleSession ()
        let layer = Session.activeLayer session
        let side = sideFromName sideName
        let property = propertyByName propertyName layer.Model

        let uiState =
            State.init session
            |> State.Sides.ensure session
            |> State.PropertyPlacement.place layer.Id side property
            |> State.PropertyColors.setSourceColor layer.Model.Source.Id layerColor

        let projection =
            PropertyProjection.railProjectionWithFilters session layer.Id side layer.Model uiState

        projection.ColorByHeader
        |> Map.tryFind property
        |> Option.bind id
        |> Option.defaultValue ""
