namespace Arcitisa.Components.Page.Landing

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open ARCtrl
open Browser.Dom

[<Erase; Mangle(false)>]
type Landing =

    [<ReactComponent(true)>]
    static member Wizard
        (
            draft: LandingDraft,
            setDraft: LandingDraft -> unit,
            uiState: LandingUiState,
            setUiState: LandingUiState -> unit,
            onSubmit: SubmitPayload -> unit,
            ?onImportPersons: unit -> JS.Promise<Person[]>
        ) =

        let setError (value: string option) =
            setUiState (State.setError value uiState)

        let continueToQuestions () =
            if Validation.isRequiredDataValid draft then
                setUiState (State.continueToQuestions uiState)
            else
                setError (Some "Title and Description are required.")

        let pickTarget target =
            setUiState (State.selectTarget target uiState)

        let submit () =
            match uiState.SelectedTarget with
            | Some target ->
                if Validation.isRequiredDataValid draft then
                    Conversion.toSubmitPayload draft target |> onSubmit
                else
                    setError (Some "Title and Description are required.")
            | None -> setError (Some "Select Study or Assay before creating.")

        Html.div [
            prop.className "swt:p-8 swt:flex swt:justify-center"
            prop.children [
                Html.div [
                    prop.className
                        "swt:w-full swt:max-w-3xl swt:rounded-box swt:border swt:border-base-300 swt:bg-base-200 swt:p-6 swt:space-y-4"
                    prop.children [
                        Html.h2 [
                            prop.className "swt:text-3xl swt:font-bold swt:text-primary"
                            prop.text "Experiment Metadata"
                        ]
                        Html.p [
                            prop.className "swt:opacity-70"
                            prop.text "Only Title and Description are required. All other fields are optional."
                        ]
                        SharedFields.Main(draft, setDraft, onImportPersons)
                        Actions.ContinueButton(continueToQuestions, uiState.Error)
                        if uiState.ShowQuestions then
                            Html.div [
                                prop.className
                                    "swt:mt-4 swt:rounded-box swt:border swt:border-base-300 swt:bg-base-100 swt:p-4 swt:space-y-3"
                                prop.children [
                                    Html.h3 [
                                        prop.className "swt:text-lg swt:font-semibold"
                                        prop.text "Choose What To Create"
                                    ]
                                    TargetSelector.Main(uiState.SelectedTarget, pickTarget, uiState.IsSubmitting)
                                    match uiState.SelectedTarget with
                                    | Some LandingTarget.Study -> StudySection.Main(draft, setDraft)
                                    | Some LandingTarget.Assay -> AssaySection.Main(draft, setDraft)
                                    | None -> Html.none
                                    Actions.SubmitButton(uiState.SelectedTarget, uiState.IsSubmitting, submit)
                                ]
                            ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member Entry() =
        let draft, setDraft = React.useState LandingDraft.init
        let uiState, setUiState = React.useState LandingUiState.init

        let onSubmit (payload: SubmitPayload) =
            console.log ("Landing submit payload", payload)

        Landing.Wizard(draft, setDraft, uiState, setUiState, onSubmit)
