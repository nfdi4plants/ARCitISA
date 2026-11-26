namespace Arcitisa.Components.Page.Landing

open Fable.Core
open Feliz
open ARCtrl
open Arcitisa.Components.Page.Metadata
open Arcitisa.Components.Page.Metadata.FormComponents
open Arcitisa.Components.Page.Metadata.FormComponents.PersonsInput

[<RequireQualifiedAccess>]
module SharedFields =

    [<ReactComponent>]
    let BoxedHelperField (title: string) (content: ReactElement) =
        Html.fieldSet [
            prop.className "swt:fieldset"
            prop.children [
                Html.legend [ prop.className "swt:fieldset-legend"; prop.text title ]
                Html.div [
                    prop.className "swt:rounded-box swt:border swt:border-base-300 swt:bg-base-100 swt:p-3"
                    prop.children [ content ]
                ]
            ]
        ]

    [<ReactComponent>]
    let Main
        (draft: LandingDraft, setDraft: LandingDraft -> unit, onImportPersons: (unit -> JS.Promise<Person[]>) option)
        =
        React.Fragment [
            TextInput.TextInput(
                draft.Identifier,
                (fun value -> setDraft { draft with Identifier = value }),
                label = "Identifier (Optional)",
                placeholder = "Auto-generated if empty"
            )
            TextInput.TextInput(
                draft.Title,
                (fun value -> setDraft { draft with Title = value }),
                label = "Title (Required)",
                placeholder = "Experiment title"
            )
            TextInput.TextInput(
                draft.Description,
                (fun value -> setDraft { draft with Description = value }),
                label = "Description (Required)",
                isArea = true,
                placeholder = "Experiment description"
            )
            BoxedHelperField
                "Involved People"
                (PersonsInput.PersonsInput(
                    draft.InvolvedPeople,
                    (fun persons -> setDraft { draft with InvolvedPeople = persons }),
                    ?onImportPersons = onImportPersons
                ))
            BoxedHelperField
                "Comments"
                (CommentsInput.CommentsInput(
                    draft.Comments,
                    (fun comments -> setDraft { draft with Comments = comments })
                ))
            TextInput.TextInput(
                draft.MainText,
                (fun value -> setDraft { draft with MainText = value }),
                label = "Main Text",
                isArea = true,
                placeholder = "Will be saved as protocol markdown"
            )
            Html.fieldSet [
                prop.className "swt:fieldset"
                prop.children [
                    Html.legend [ prop.className "swt:fieldset-legend"; prop.text "Files" ]
                    Html.div [
                        prop.className
                            "swt:border swt:border-dashed swt:border-base-content/30 swt:rounded-box swt:p-4 swt:bg-base-100"
                        prop.children [
                            Html.p [
                                prop.className "swt:opacity-70"
                                prop.text "File picker is temporarily unavailable here."
                            ]
                        ]
                    ]
                ]
            ]
        ]
