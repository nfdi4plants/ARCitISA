namespace Arcitisa.Components.Page.Landing

open Feliz
open Arcitisa.Components.Page.Metadata
open Arcitisa.Components.Page.Metadata.FormComponents

[<RequireQualifiedAccess>]
module AssaySection =

    [<ReactComponent>]
    let Main (draft: LandingDraft, setDraft: LandingDraft -> unit) =
        Html.div [
            prop.className "swt:space-y-3"
            prop.children [
                OntologyAnnotationInput.OntologyAnnotationInput(
                    draft.MeasurementType,
                    (fun value -> setDraft { draft with MeasurementType = value }),
                    label = "Measurement Type"
                )
                OntologyAnnotationInput.OntologyAnnotationInput(
                    draft.TechnologyType,
                    (fun value -> setDraft { draft with TechnologyType = value }),
                    label = "Technology Type"
                )
                OntologyAnnotationInput.OntologyAnnotationInput(
                    draft.TechnologyPlatform,
                    (fun value ->
                        setDraft {
                            draft with
                                TechnologyPlatform = value
                        }
                    ),
                    label = "Technology Platform"
                )
            ]
        ]
