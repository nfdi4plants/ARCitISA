namespace Modals

open Arcitisa.Components.Shared.Database
open Feliz
open ARCtrl
open Arcitisa.Components
open Arcitisa.Components.Primitive
open Arcitisa.Components.Primitive.BaseModal
open Arcitisa.Components.Shared


module private TermModalUtil =
    type State =
        | Loading
        | Found of Database.Term
        | NotFound

open TermModalUtil

type TermModal =

    [<ReactComponent>]
    static member Main(isOpen, setIsOpen, oa: OntologyAnnotation) =

        let state, setState = React.useState (State.Loading)

        React.useEffectOnce (fun _ ->
            async {
                let! term = Api.ontology.getTermById (oa.TermAccessionShort)

                match term with
                | Some t -> Found t |> setState
                | None -> NotFound |> setState
            }
            |> Async.StartImmediate
        )

        let mkInfoPart (txt: string) (desc: string) =
            Html.div [
                Html.h3 [
                    prop.className "swt:text-lg swt:font-semibold"
                    prop.text txt
                ]
                if desc <> "" then
                    Html.p [ prop.text desc ]
            ]

        let headerElement =
            Html.div [
                Html.span [ prop.className "swt:font-bold"; prop.text oa.NameText ]
                Html.div [
                    prop.className "swt:text-xs"
                    prop.text oa.TermAccessionShort
                ]
            ]

        let modalActivity =
            Html.div [
                prop.className "swt:space-y-4"
                prop.children [
                    match state with
                    | Loading -> Html.p "loading .."
                    | NotFound ->
                        Html.p [
                            prop.dangerouslySetInnerHTML
                                $"Unable to find term with id <b>{oa.TermAccessionShort}</b> in database."
                        ]
                    | Found term ->
                        mkInfoPart "Description" term.Description
                        mkInfoPart "Source Ontology" term.FK_Ontology

                        if term.IsObsolete then
                            Html.p [ prop.className "swt:text-error"; prop.text "Obsolete" ]

                        Html.a [
                            prop.className "swt:space-x-2 swt:float-right swt:link-primary"
                            prop.href (OntologyAnnotation.from term |> _.TermAccessionOntobeeUrl)
                            prop.target.blank
                            prop.children [ Html.span "Ref"; Icons.ExternalLinkAlt() ]
                        ]
                ]
            ]

        BaseModal.Modal(isOpen, setIsOpen, Html.p headerElement, Html.div [], modalActions = modalActivity)
