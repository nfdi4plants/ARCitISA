namespace Arcitisa.Components.Page.Landing

module State =

    let setError (error: string option) (state: LandingUiState) = { state with Error = error }

    let continueToQuestions (state: LandingUiState) = {
        state with
            ShowQuestions = true
            Error = None
    }

    let selectTarget (target: LandingTarget) (state: LandingUiState) = {
        state with
            SelectedTarget = Some target
            Error = None
    }

    let startSubmitting (state: LandingUiState) = {
        state with
            IsSubmitting = true
            Error = None
    }

    let stopSubmitting (state: LandingUiState) = { state with IsSubmitting = false }
