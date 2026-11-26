namespace Arcitisa.Components.Page.Landing

open Feliz

[<RequireQualifiedAccess>]
module TargetSelector =

    [<ReactComponent>]
    let private TargetSelectButton
        (
            label: string,
            target: LandingTarget,
            selectedTarget: LandingTarget option,
            setSelectedTarget: LandingTarget -> unit,
            isSubmitting: bool
        ) =
        Html.button [
            prop.className [
                "swt:btn swt:flex-1"
                if selectedTarget = Some target then
                    "swt:btn-primary"
                else
                    "swt:btn-outline"
            ]
            prop.disabled isSubmitting
            prop.onClick (fun _ -> setSelectedTarget target)
            prop.text label
        ]

    [<ReactComponent>]
    let Main (selectedTarget: LandingTarget option, setSelectedTarget: LandingTarget -> unit, isSubmitting: bool) =
        Html.div [
            prop.className "swt:flex swt:gap-3"
            prop.children [
                TargetSelectButton("Study", LandingTarget.Study, selectedTarget, setSelectedTarget, isSubmitting)
                TargetSelectButton("Assay", LandingTarget.Assay, selectedTarget, setSelectedTarget, isSubmitting)
            ]
        ]
