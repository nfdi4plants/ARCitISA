namespace Arcitisa.Components.Page.Metadata.FormComponents

open Browser.Types
open Fable.Core
open Feliz
open ARCtrl

open Arcitisa.Components
open Arcitisa.Components.Composite.TermSearch
open Arcitisa.Components.Composite.TermSearch.Types
open Arcitisa.Components.Primitive.LayoutComponents

[<Erase; Mangle(false)>]
type OntologyAnnotationInput =

    [<ReactComponent>]
    static member OntologyAnnotationInput
        (
            input: OntologyAnnotation option,
            setter: OntologyAnnotation option -> unit,
            ?label: string,
            ?parent: OntologyAnnotation,
            ?rmv: MouseEvent -> unit
        ) =
        let startedChange = React.useRef false
        let term, setTerm = React.useState (input |> Option.map _.ToTerm())

        let setTermWrapper =
            React.useCallback (fun (nextTerm: Term option) ->
                setTerm nextTerm
                startedChange.current <- true
            )

        let debouncedTerm = React.useDebounce (term, 300)

        React.useEffect (
            (fun () ->
                if startedChange.current then
                    setter (debouncedTerm |> Option.map OntologyAnnotation.from)

                startedChange.current <- false
            ),
            [| box debouncedTerm |]
        )

        React.useEffect (
            (fun () ->
                setTerm (input |> Option.map _.ToTerm())
                startedChange.current <- false
            ),
            [| box input |]
        )

        Html.div [
            prop.className "swt:space-y-2 swt:grow"
            prop.children [
                if label.IsSome then
                    LayoutComponents.FieldTitle label.Value
                Html.div [
                    prop.className "swt:w-full swt:flex swt:gap-2 swt:relative"
                    prop.children [
                        TermSearch.TermSearch(
                            term,
                            setTermWrapper,
                            ?parentId = (parent |> Option.map _.TermAccessionShort),
                            classNames = TermSearchStyle(Fable.Core.U2.Case1 "swt:w-full")
                        )
                        if rmv.IsSome then
                            Helpers.deleteButton rmv.Value
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member OntologyAnnotationsInput
        (
            input: ResizeArray<OntologyAnnotation>,
            setter: ResizeArray<OntologyAnnotation> -> unit,
            ?label: string,
            ?parent: OntologyAnnotation
        ) =
        InputSequence.InputSequence(
            input,
            OntologyAnnotation.empty,
            setter,
            (fun (value, setValue, remove) ->
                OntologyAnnotationInput.OntologyAnnotationInput(
                    Some value,
                    (fun next -> next |> Option.defaultValue (OntologyAnnotation.empty ()) |> setValue),
                    ?parent = parent,
                    rmv = remove
                )
            ),
            ?label = label
        )
