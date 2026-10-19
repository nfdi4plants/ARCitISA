namespace Swate.Components.Page.Metadata.FormComponents

open System
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open Feliz

open Swate.Components
open Swate.Components.Primitive
open Swate.Components.Primitive.BaseModal
open Swate.Components.Primitive.LayoutComponents
open Swate.Components.JsBindings

[<Erase; Mangle(false)>]
type InputSequence =

    [<ReactComponent>]
    static member private InputSequenceElement(key: string, id: string, listComponent: ReactElement) =
        let sortable = JsBindings.DndKit.useSortable ({| id = id |})

        let style = {|
            transform = DndKit.CSS.Transform.toString sortable.transform
            transition = sortable.transition
        |}

        Html.div [
            prop.ref sortable.setNodeRef
            prop.id id
            for attribute in Object.keys sortable.attributes do
                prop.custom (attribute, sortable.attributes.get attribute)
            prop.className "swt:flex swt:flex-row"
            prop.custom ("style", style)
            prop.children [
                Html.span [
                    for listener in Object.keys sortable.listeners do
                        prop.custom (listener, sortable.listeners.get listener)
                    prop.className "swt:cursor-grab swt:flex swt:items-center"
                    prop.children [ Icons.ArrowUpDown() ]
                ]
                Html.div [ prop.className "swt:grow"; prop.children listComponent ]
            ]
        ]

    [<ReactComponent>]
    static member InputSequence<'T>
        (
            inputs: ResizeArray<'T>,
            constructor: unit -> 'T,
            setter: ResizeArray<'T> -> unit,
            inputComponent: 'T * ('T -> unit) * (MouseEvent -> unit) -> ReactElement,
            ?validator: ResizeArray<'T> -> Result<unit, string>,
            ?label: string,
            ?extendedElements: ReactElement
        ) =
        let sensors = DndKit.useSensors [| DndKit.useSensor DndKit.PointerSensor |]
        let error, setError = React.useState (None: string option)

        let guids =
            React.useMemo (
                (fun () ->
                    ResizeArray [
                        for _ in inputs do
                            Guid.NewGuid()
                    ]
                ),
                [| box inputs.Count |]
            )

        let mkId index = guids.[index].ToString()

        let getIndexFromId (id: string) =
            guids.FindIndex(fun guid -> guid = Guid id)

        let previousValidInputs = React.useRef inputs

        let validateSetter next =
            match validator with
            | Some validate ->
                match validate next with
                | Ok() ->
                    previousValidInputs.current <- next
                    setter next
                | Error message ->
                    setter previousValidInputs.current
                    setError (Some $"Validation Error: {message}")
            | None ->
                previousValidInputs.current <- next
                setter next

        let handleDragEnd (event: DndKit.IDndKitEvent) =
            let active = event.active
            let over = event.over

            if isNull over |> not && active.id <> over.id then
                let oldIndex = getIndexFromId (string active.id)
                let newIndex = getIndexFromId (string over.id)

                if oldIndex >= 0 && newIndex >= 0 then
                    DndKit.arrayMove (inputs, oldIndex, newIndex) |> validateSetter

        Html.div [
            prop.className "swt:space-y-2"
            prop.children [
                BaseModal.ErrorModalObsolete(error.IsSome, (fun _ -> setError None), error |> Option.defaultValue "")
                if label.IsSome then
                    LayoutComponents.FieldTitle label.Value
                if extendedElements.IsSome then
                    extendedElements.Value
                DndKit.DndContext(
                    sensors = sensors,
                    onDragEnd = handleDragEnd,
                    collisionDetection = DndKit.closestCenter,
                    children =
                        DndKit.SortableContext(
                            items = guids,
                            strategy = DndKit.verticalListSortingStrategy,
                            children =
                                Html.div [
                                    prop.className "swt:space-y-2"
                                    prop.children [
                                        for index in 0 .. (inputs.Count - 1) do
                                            let item = inputs.[index]
                                            let id = mkId index

                                            InputSequence.InputSequenceElement(
                                                id,
                                                id,
                                                inputComponent (
                                                    item,
                                                    (fun updated ->
                                                        inputs.[index] <- updated
                                                        validateSetter inputs
                                                    ),
                                                    (fun _ ->
                                                        inputs.RemoveAt index
                                                        validateSetter inputs
                                                    )
                                                )
                                            )
                                    ]
                                ]
                        )
                )
                Html.div [
                    prop.className "swt:flex swt:justify-center swt:w-full swt:mt-2"
                    prop.children [
                        Helpers.addButton (fun _ ->
                            inputs.Add(constructor ())
                            validateSetter inputs
                        )
                    ]
                ]
            ]
        ]
