namespace Arcitisa.Components.Page.Landing

open Feliz
open Arcitisa.Components.Page.Metadata
open Arcitisa.Components.Page.Metadata.FormComponents
open Arcitisa.Components.Page.Metadata.FormComponents.PublicationsInput

[<RequireQualifiedAccess>]
module StudySection =

    [<ReactComponent>]
    let private BoxedHelperContent (content: ReactElement) =
        Html.div [
            prop.className "swt:rounded-box swt:border swt:border-base-300 swt:bg-base-100 swt:p-3"
            prop.children [ content ]
        ]

    [<ReactComponent>]
    let Main (draft: LandingDraft, setDraft: LandingDraft -> unit) =
        Html.div [
            prop.className "swt:space-y-3"
            prop.children [
                BoxedHelperContent(
                    PublicationsInput.PublicationsInput(
                        draft.Publications,
                        (fun pubs -> setDraft { draft with Publications = pubs }),
                        label = "Publications"
                    )
                )
                DateTimeInput.DateTimeInput(
                    draft.SubmissionDate,
                    (fun dateText -> setDraft { draft with SubmissionDate = dateText }),
                    label = "Submission Date"
                )
                DateTimeInput.DateTimeInput(
                    draft.PublicReleaseDate,
                    (fun dateText ->
                        setDraft {
                            draft with
                                PublicReleaseDate = dateText
                        }
                    ),
                    label = "Public Release Date"
                )
                BoxedHelperContent(
                    OntologyAnnotationInput.OntologyAnnotationsInput(
                        draft.StudyDesignDescriptors,
                        (fun descriptors ->
                            setDraft {
                                draft with
                                    StudyDesignDescriptors = descriptors
                            }
                        ),
                        label = "Study Design Descriptors"
                    )
                )
            ]
        ]
