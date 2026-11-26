namespace Arcitisa.Components.Composite.Notes.Editor

module State =

    let setError (error: string option) (state: NotesUiState) = { state with Error = error }

    let clearError (state: NotesUiState) = { state with Error = None }

    let startSubmitting (state: NotesUiState) = {
        state with
            IsSubmitting = true
            Error = None
    }

    let stopSubmitting (state: NotesUiState) = { state with IsSubmitting = false }
