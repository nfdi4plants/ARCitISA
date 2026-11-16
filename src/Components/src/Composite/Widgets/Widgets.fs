namespace Swate.Components.Composite.Widgets

open Feliz
open Browser.Types
open Swate
open Swate.Components
open Swate.Components.JsBindings
open Swate.Components.Composite.Widgets.LocalStorage
open Swate.Components.Composite.Widgets.Context
open Swate.Components.Composite.TermSearch
open Swate.Components.Primitive.Buttons
open Swate.Components.Composite.Widgets.Types

module InitExtensions =

    type Rect with

        static member initSizeFromPrefix(prefix: string) =
            match Size.load prefix with
            | Some p -> Some p
            | None -> None

        static member initPositionFromPrefix(prefix: string) =
            match Position.load prefix with
            | Some p -> Some p
            | None -> None

open InitExtensions

open Fable.Core
open Fable.Core.JsInterop

module WidgetPosition =

    /// Moves a widget into its host. Positions are relative to the top left corner of the host.
    /// A widget that is larger than the host sits at the top left corner.
    let clampIntoHost (host: Rect) (element: Rect) (position: Rect) : Rect = {
        X = min (max position.X 0) (max 0 (host.X - element.X))
        Y = min (max position.Y 0) (max 0 (host.Y - element.Y))
    }

module private MoveEventListener =

    open Fable.Core.JsInterop

    let private measure (element: IRefValue<HTMLElement option>) = {
        X = int element.current.Value.offsetWidth
        Y = int element.current.Value.offsetHeight
    }

    let ensurePositionInsideHost
        (host: IRefValue<HTMLElement option>)
        (element: IRefValue<HTMLElement option>)
        (position: Rect)
        =
        WidgetPosition.clampIntoHost (measure host) (measure element) position

    let pointerInHost (host: IRefValue<HTMLElement option>) (e: MouseEvent) =
        let hostRect = host.current.Value.getBoundingClientRect ()

        {
            X = int (e.clientX - hostRect.left)
            Y = int (e.clientY - hostRect.top)
        }

    let calculatePosition
        (host: IRefValue<HTMLElement option>)
        (element: IRefValue<HTMLElement option>)
        (startPosition: Rect)
        =
        fun (e: Event) ->
            let pointer = pointerInHost host !!e

            let tempPosition = {
                X = pointer.X - startPosition.X
                Y = pointer.Y - startPosition.Y
            }

            ensurePositionInsideHost host element tempPosition

    let onmousemove
        (host: IRefValue<HTMLElement option>)
        (element: IRefValue<HTMLElement option>)
        (startPosition: Rect)
        setPosition
        =
        fun (e: Event) ->
            let nextPosition = calculatePosition host element startPosition e
            setPosition (Some nextPosition)

    let onmouseup (prefix, host: IRefValue<HTMLElement option>, element: IRefValue<HTMLElement option>) onmousemove =
        Browser.Dom.document.removeEventListener ("mousemove", onmousemove)

        if element.current.IsSome then
            let rect = element.current.Value.getBoundingClientRect ()
            let hostRect = host.current.Value.getBoundingClientRect ()

            let position = {
                X = int (rect.left - hostRect.left)
                Y = int (rect.top - hostRect.top)
            }

            Position.write (prefix, position)

module ResizeEventListener =

    open Fable.Core.JsInterop

    let onmousemove (startPosition: Rect) (startSize: Rect) setSize =
        fun (e: Event) ->
            let e: MouseEvent = !!e
            let width = int e.clientX - startPosition.X + startSize.X
            // I did not enable this, as it creates issues with overlays such as the term search dropdown.
            // The widget card itself has overflow: visible, which makes a set height impossible,
            // but wihout the visible overflow term search results might require scrolling.
            // // let height = int e.clientY - startPosition.Y + startSize.Y
            setSize (Some { X = width; Y = startSize.Y })

    let onmouseup (prefix, element: IRefValue<HTMLElement option>) onmousemove =
        Browser.Dom.document.removeEventListener ("mousemove", onmousemove)

        if element.current.IsSome then
            Size.write (
                prefix,
                {
                    X = int element.current.Value.offsetWidth
                    Y = int element.current.Value.offsetHeight
                }
            )

[<RequireQualifiedAccess>]
[<Erase; Mangle(false)>]
type Widget =


    [<ReactComponent>]
    static member Base
        (
            content: ReactElement,
            host: IRefValue<HTMLElement option>,
            prefix: string,
            rmv: MouseEvent -> unit,
            key: string,
            ?zIndex: int,
            ?onFocus: unit -> unit
        ) =
        let zIndex = defaultArg zIndex 9999
        let onFocus = defaultArg onFocus (fun () -> ())

        let position, setPosition =
            React.useState (fun _ -> Rect.initPositionFromPrefix prefix)

        let size, setSize = React.useState (fun _ -> Rect.initSizeFromPrefix prefix)

        let element = React.useElementRef ()

        // Keeps the widget inside the host when the host changes size, for example when a sidebar opens.
        React.useLayoutEffectOnce (fun _ ->
            if Dom.ResizeObserver.isAvailable then
                let observer =
                    Dom.ResizeObserver(fun _ ->
                        let current = {
                            X = int element.current.Value.offsetLeft
                            Y = int element.current.Value.offsetTop
                        }

                        let clamped = MoveEventListener.ensurePositionInsideHost host element current

                        if clamped <> current then
                            setPosition (Some clamped)
                    )

                observer.observe host.current.Value
                FsReact.createDisposable (fun () -> observer.disconnect ())
            else
                FsReact.createDisposable ignore
        )

        React.useLayoutEffectOnce (fun _ ->
            position
            |> Option.iter (fun position ->
                MoveEventListener.ensurePositionInsideHost host element position
                |> Some
                |> setPosition
            )
        ) // Reposition widget inside host

        let resizeElement (content: ReactElement) =
            Html.div [
                prop.ref element
                prop.onMouseDown (fun e -> // resize
                    onFocus ()
                    e.preventDefault ()
                    e.stopPropagation ()
                    let startPosition = { X = int e.clientX; Y = int e.clientY }

                    let startSize = {
                        X = int element.current.Value.offsetWidth
                        Y = int element.current.Value.offsetHeight
                    }

                    let onmousemove = ResizeEventListener.onmousemove startPosition startSize setSize
                    let onmouseup = fun e -> ResizeEventListener.onmouseup (prefix, element) onmousemove
                    Browser.Dom.document.addEventListener ("mousemove", onmousemove)
                    let config = createEmpty<AddEventListenerOptions>
                    config.once <- true
                    Browser.Dom.document.addEventListener ("mouseup", onmouseup, config)
                )
                prop.className
                    "swt:shadow-md swt:border swt:border-base-300 swt:space-y-4 swt:rounded-lg swt:border-r-2 swt:bg-base-100"
                prop.style [
                    style.zIndex zIndex
                    style.cursor.eastWestResize //style.cursor.northWestSouthEastResize;
                    style.display.flex
                    style.position.absolute
                    style.minWidth.minContent
                    if size.IsSome then
                        style.width size.Value.X
                        style.maxWidth (length.percent 100)
                    else
                        style.custom ("maxWidth", "min(600px, 100%)")
                    //style.height size.Value.Y
                    if position.IsNone then
                        //style.transform.translate (length.perc -50,length.perc -50)
                        style.top (length.perc 20)
                        style.left (length.perc 20)
                    else
                        style.top position.Value.Y
                        style.left position.Value.X
                ]
                prop.children content
            ]

        resizeElement
        <| Html.div [
            prop.onMouseDown (fun e ->
                onFocus ()
                e.stopPropagation ()
            )
            prop.className "swt:cursor-default swt:flex swt:flex-col swt:grow swt:max-h-[60%] swt:overflow-visible"
            prop.children [
                Html.div [
                    prop.onMouseDown (fun e -> // move
                        onFocus ()
                        e.preventDefault ()
                        e.stopPropagation ()
                        let pointer = MoveEventListener.pointerInHost host e

                        let startPosition = {
                            X = pointer.X - int element.current.Value.offsetLeft
                            Y = pointer.Y - int element.current.Value.offsetTop
                        }

                        let onmousemove =
                            MoveEventListener.onmousemove host element startPosition setPosition

                        let onmouseup =
                            fun _ -> MoveEventListener.onmouseup (prefix, host, element) onmousemove

                        Browser.Dom.document.addEventListener ("mousemove", onmousemove)
                        let config = createEmpty<AddEventListenerOptions>
                        config.once <- true
                        Browser.Dom.document.addEventListener ("mouseup", onmouseup, config)
                    )
                    prop.className
                        "swt:cursor-move swt:flex swt:justify-end swt:bg-linear-to-br swt:from-primary swt:to-base-200 swt:rounded-lg"
                    prop.children [
                        Buttons.DeleteButton(
                            className = "swt:btn-ghost swt:bg-primary/30",
                            props = [
                                prop.onMouseDown (fun e -> e.stopPropagation ())
                                prop.onClick (fun e ->
                                    e.stopPropagation ()
                                    rmv e
                                )
                            ]
                        )
                    ]
                ]
                Html.div [
                    prop.className "swt:p-2 swt:max-h-[80vh] swt:overflow-visible swt:flex swt:flex-col"
                    prop.children [ content ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member WidgetController
        (widgets: Map<WidgetType, WidgetDefinition>, children: ReactElement, ?closeAllWhen: bool)
        =

        let hostRef = React.useElementRef ()

        let activeWidgets, setActiveWidgets =
            React.useStateWithUpdater<WidgetType list> ([])

        let closeAllWhen = defaultArg closeAllWhen false

        React.useEffect (
            (fun () ->
                if closeAllWhen then
                    setActiveWidgets (fun _ -> [])
            ),
            [| box closeAllWhen |]
        )

        let closeWidget =
            React.useCallback (
                (fun (widgetType: WidgetType) ->
                    setActiveWidgets (fun widgets -> widgets |> List.filter (fun widget -> widget <> widgetType))
                ),
                [| box setActiveWidgets |]
            )

        let focusWidget =
            React.useCallback (
                (fun (widgetType: WidgetType) ->
                    setActiveWidgets (fun widgets ->
                        if widgets |> List.contains widgetType then
                            widgets
                            |> List.filter (fun widget -> widget <> widgetType)
                            |> fun nextWidgets -> nextWidgets @ [ widgetType ]
                        else
                            widgets
                    )
                ),
                [| box setActiveWidgets |]
            )

        let openWidget =
            React.useCallback (
                (fun (widgetType: WidgetType) ->
                    setActiveWidgets (fun widgets ->
                        if widgets |> List.contains widgetType then
                            widgets
                            |> List.filter (fun widget -> widget <> widgetType)
                            |> fun nextWidgets -> nextWidgets @ [ widgetType ]
                        else
                            widgets @ [ widgetType ]
                    )
                ),
                [| box setActiveWidgets |]
            )

        let toggleWidget =
            React.useCallback (
                (fun (widgetType: WidgetType) ->
                    setActiveWidgets (fun widgets ->
                        if widgets |> List.contains widgetType then
                            widgets |> List.filter (fun widget -> widget <> widgetType)
                        else
                            widgets @ [ widgetType ]
                    )
                ),
                [| box setActiveWidgets |]
            )

        let isActive =
            React.useCallback (
                (fun (widgetType: WidgetType) -> activeWidgets |> List.contains widgetType),
                [| box activeWidgets |]
            )

        let widgetContext: Context.WidgetControllerContext =
            React.useMemo (
                (fun () -> {
                    activeWidgets = activeWidgets
                    isActive = isActive
                    openWidget = openWidget
                    closeWidget = closeWidget
                    toggleWidget = toggleWidget
                    focusWidget = focusWidget
                }),
                [|
                    box activeWidgets
                    box isActive
                    box openWidget
                    box closeWidget
                    box toggleWidget
                    box focusWidget
                |]
            )

        let widgetElements = [
            for index, widgetType in activeWidgets |> List.indexed do
                match widgets.TryFind widgetType with
                | Some widget ->
                    Widget.Base(
                        content = widget.content,
                        host = hostRef,
                        prefix = widget.prefix,
                        rmv = (fun _ -> closeWidget widgetType),
                        key = widgetType.ToString(),
                        zIndex = 99 + index,
                        onFocus = (fun () -> focusWidget widgetType)
                    )
                | None -> Html.none
        ]

        // Widgets stay inside this host, so they cannot cover the app chrome around the content.
        WidgetControllerCtx.Provider(
            widgetContext,
            Html.div [
                prop.ref hostRef
                prop.className "swt:relative swt:flex swt:flex-col swt:grow swt:min-h-0 swt:min-w-0"
                prop.children (children :: widgetElements)
            ]
        )

    /// This component is only used for testing and development via playground
    [<ReactComponent>]
    static member private EntryControls(widgetTypes: WidgetType list) =
        let context = useWidgetControllerCtx ()

        let controlButton (widgetType: WidgetType) =
            let isActive = context.isActive widgetType

            Html.button [
                prop.className [
                    "swt:btn swt:btn-sm"
                    if isActive then "swt:btn-primary" else "swt:btn-outline"
                ]
                prop.textf "%s %s" (if isActive then "Close" else "Open") (widgetType.ToString())
                prop.onClick (fun _ -> context.toggleWidget widgetType)
            ]

        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-3 swt:items-center swt:justify-center"
            prop.children [
                Html.div [
                    prop.className "swt:flex swt:flex-wrap swt:gap-2 swt:justify-center"
                    prop.children [
                        for widgetType in widgetTypes do
                            controlButton widgetType
                    ]
                ]
                Html.div [
                    prop.className "swt:flex swt:flex-wrap swt:gap-2 swt:justify-center"
                    prop.children [
                        Html.button [
                            prop.className "swt:btn swt:btn-sm swt:btn-secondary"
                            prop.text "Open All"
                            prop.onClick (fun _ -> widgetTypes |> List.iter context.openWidget)
                        ]
                        Html.button [
                            prop.className "swt:btn swt:btn-sm swt:btn-ghost"
                            prop.text "Close All"
                            prop.onClick (fun _ -> widgetTypes |> List.iter context.closeWidget)
                        ]
                    ]
                ]
                Html.div [
                    prop.className "swt:text-xs swt:opacity-70"
                    prop.textf
                        "Active order: %s"
                        (if context.activeWidgets.IsEmpty then
                             "none"
                         else
                             context.activeWidgets |> List.map _.ToString() |> String.concat " > ")
                ]
            ]
        ]

    /// This component is only used for testing and development via playground
    [<ReactComponent>]
    static member Entry() =
        let term, setTerm = React.useState None
        let clickCounter, setClickCounter = React.useState 0
        let fileName, setFileName = React.useState "example.tsv"
        let annotateEnabled, setAnnotateEnabled = React.useState false
        let note, setNote = React.useState "Playground note"

        let widgetTypes = [
            WidgetType.BuildingBlock
            WidgetType.Template
            WidgetType.FilePicker
            WidgetType.DataAnnotator
            WidgetType.Playground
        ]

        let widgets: Map<WidgetType, WidgetDefinition> =
            [
                WidgetType.BuildingBlock,
                {|
                    prefix = "TEST_ENTRY_BUILDINGBLOCK"
                    content =
                        Html.div [
                            prop.className "swt:flex swt:flex-col swt:gap-2 swt:min-w-64"
                            prop.children [
                                Html.h3 [
                                    prop.className "swt:font-bold"
                                    prop.text "BuildingBlock POC"
                                ]
                                Html.span [ prop.textf "Counter: %i" clickCounter ]
                                Html.button [
                                    prop.className "swt:btn swt:btn-sm swt:btn-primary swt:w-max"
                                    prop.text "Increment"
                                    prop.onClick (fun _ -> setClickCounter (clickCounter + 1))
                                ]
                            ]
                        ]
                |}

                WidgetType.Template,
                {|
                    prefix = "TEST_ENTRY_TEMPLATE"
                    content =
                        Html.div [
                            prop.className "swt:flex swt:flex-col swt:gap-2 swt:min-w-80"
                            prop.children [
                                Html.h3 [ prop.className "swt:font-bold"; prop.text "Template POC" ]
                                TermSearch.TermSearch(term, setTerm)
                                Html.span [
                                    prop.className "swt:text-xs swt:opacity-70"
                                    prop.textf
                                        "Selected term: %s"
                                        (term |> Option.map (fun t -> t.ToString()) |> Option.defaultValue "None")
                                ]
                            ]
                        ]
                |}

                WidgetType.FilePicker,
                {|
                    prefix = "TEST_ENTRY_FILEPICKER"
                    content =
                        Html.div [
                            prop.className "swt:flex swt:flex-col swt:gap-2 swt:min-w-72"
                            prop.children [
                                Html.h3 [
                                    prop.className "swt:font-bold"
                                    prop.text "FilePicker POC"
                                ]
                                Html.input [
                                    prop.className "swt:input swt:input-sm swt:input-bordered"
                                    prop.value fileName
                                    prop.onChange setFileName
                                ]
                                Html.span [
                                    prop.className "swt:text-xs swt:opacity-70"
                                    prop.textf "Current: %s" fileName
                                ]
                            ]
                        ]
                |}

                WidgetType.DataAnnotator,
                {|
                    prefix = "TEST_ENTRY_DATAANNOTATOR"
                    content =
                        Html.div [
                            prop.className "swt:flex swt:flex-col swt:gap-2 swt:min-w-64"
                            prop.children [
                                Html.h3 [
                                    prop.className "swt:font-bold"
                                    prop.text "DataAnnotator POC"
                                ]
                                Html.label [
                                    prop.className "swt:label swt:cursor-pointer swt:justify-start swt:gap-2"
                                    prop.children [
                                        Html.input [
                                            prop.type'.checkbox
                                            prop.className "swt:checkbox swt:checkbox-sm"
                                            prop.isChecked annotateEnabled
                                            prop.onChange (fun (isChecked: bool) -> setAnnotateEnabled isChecked)
                                        ]
                                        Html.span [ prop.text "Enable annotation" ]
                                    ]
                                ]
                                Html.span [
                                    prop.className "swt:text-xs swt:opacity-70"
                                    prop.text (
                                        if annotateEnabled then
                                            "Status: enabled"
                                        else
                                            "Status: disabled"
                                    )
                                ]
                            ]
                        ]
                |}

                WidgetType.Playground,
                {|
                    prefix = "TEST_ENTRY_PLAYGROUND"
                    content =
                        Html.div [
                            prop.className "swt:flex swt:flex-col swt:gap-2 swt:min-w-80"
                            prop.children [
                                Html.h3 [
                                    prop.className "swt:font-bold"
                                    prop.text "Playground POC"
                                ]
                                Html.textarea [
                                    prop.className "swt:textarea swt:textarea-bordered swt:textarea-sm"
                                    prop.rows 3
                                    prop.value note
                                    prop.onChange setNote
                                ]
                                Html.span [
                                    prop.className "swt:text-xs swt:opacity-70"
                                    prop.textf "Length: %i" note.Length
                                ]
                            ]
                        ]
                |}
            ]
            |> Map.ofList

        Widget.WidgetController(widgets, children = Widget.EntryControls(widgetTypes))
