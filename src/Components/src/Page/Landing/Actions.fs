namespace Arcitisa.Components.Page.Landing

open Feliz

[<RequireQualifiedAccess>]
module Actions =

    [<ReactComponent>]
    let ContinueButton (onContinue: unit -> unit, error: string option) =
        Html.div [
            prop.className "swt:flex swt:items-center swt:gap-3"
            prop.children [
                Html.button [
                    prop.className "swt:btn swt:btn-primary"
                    prop.onClick (fun _ -> onContinue ())
                    prop.text "Continue"
                ]
                match error with
                | Some message -> Html.span [ prop.className "swt:text-error"; prop.text message ]
                | None -> Html.none
            ]
        ]

    [<ReactComponent>]
    let SubmitButton (target: LandingTarget option, isSubmitting: bool, onSubmit: unit -> unit) =
        Html.button [
            prop.className [
                "swt:btn swt:btn-secondary"
                if isSubmitting then
                    "swt:btn-disabled"
            ]
            prop.disabled isSubmitting
            prop.onClick (fun _ -> onSubmit ())
            prop.text (
                match target with
                | Some LandingTarget.Study -> "Create Study"
                | Some LandingTarget.Assay -> "Create Assay"
                | None -> "Create"
            )
        ]
