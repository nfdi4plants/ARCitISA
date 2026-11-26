namespace MainComponents

open Feliz
open Browser.Types
open LocalStorage.Widgets
open Arcitisa
open Arcitisa.Components
open ARCtrl
open BuildingBlock.SearchComponent
open Arcitisa.Components.Shared
open Arcitisa.Components.Primitive.Buttons
open Arcitisa.Components.Composite.Widgets.Types
open Arcitisa.Components.Composite.Widgets.DataAnnotator
open Arcitisa.Components.Composite.Widgets.DataAnnotator.Types
open Arcitisa.Components.Composite.Widgets.DataAnnotator.Helper
open Arcitisa.Components.Page.ArcFileEditor.Helper

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
open Protocol
open Model
open Messages

module MoveEventListener =

    open Fable.Core.JsInterop

    let ensurePositionInsideWindow (element: IRefValue<HTMLElement option>) (position: Rect) =
        let maxX = Browser.Dom.window.innerWidth - element.current.Value.offsetWidth
        let tempX = position.X
        let newX = System.Math.Min(System.Math.Max(tempX, 0), int maxX)
        let maxY = Browser.Dom.window.innerHeight - element.current.Value.offsetHeight
        let tempY = position.Y
        let newY = System.Math.Min(System.Math.Max(tempY, 0), int maxY)
        { X = newX; Y = newY }

    let calculatePosition (element: IRefValue<HTMLElement option>) (startPosition: Rect) =
        fun (e: Event) ->
            let e: MouseEvent = !!e
            let tempX = int e.clientX - startPosition.X
            let tempY = int e.clientY - startPosition.Y
            let tempPosition = { X = tempX; Y = tempY }
            ensurePositionInsideWindow element tempPosition

    let onmousemove (element: IRefValue<HTMLElement option>) (startPosition: Rect) setPosition =
        fun (e: Event) ->
            let nextPosition = calculatePosition element startPosition e
            setPosition (Some nextPosition)

    let onmouseup (prefix, element: IRefValue<HTMLElement option>) onmousemove =
        Browser.Dom.document.removeEventListener ("mousemove", onmousemove)

        if element.current.IsSome then
            let rect = element.current.Value.getBoundingClientRect ()
            let position = { X = int rect.left; Y = int rect.top }
            Position.write (prefix, position)

module ResizeEventListener =

    open Fable.Core.JsInterop

    let adaptElement (innerWidth: int) (innerHeight: int) (size: Rect) (position: Rect) setWidth setPosition =
        let combinedWidth = size.X + position.X
        let combinedHeight = size.Y + position.Y

        if innerWidth <= size.X then
            (Some { X = innerWidth; Y = size.Y }) |> setWidth

        let newXPosition =
            if innerWidth <= combinedWidth then
                System.Math.Max(0, innerWidth - size.X)
            else
                position.X

        let newYPosition =
            if innerHeight <= combinedHeight then
                System.Math.Max(0, innerHeight - size.Y)
            else
                position.Y

        setPosition (Some { X = newXPosition; Y = newYPosition })


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

    let windowSizeChange setInnerWidth setInnerHeight =
        React.useEffect (fun () ->
            let onResize _ =
                setInnerWidth Browser.Dom.window.innerWidth
                setInnerHeight Browser.Dom.window.innerHeight

            Browser.Dom.window.addEventListener ("resize", onResize)
            // Cleanup function to remove event listener when the component unmounts
            FsReact.createDisposable (fun () -> Browser.Dom.window.removeEventListener ("resize", onResize))
        )

[<RequireQualifiedAccess>]
type Widget =
    | _BuildingBlock
    | _Template
    | _FilePicker
    | _DataAnnotator

    [<ReactComponent>]
    static member Base(content: ReactElement, prefix: string, rmv: MouseEvent -> unit, key: string) =
        let position, setPosition =
            React.useState (fun _ -> Rect.initPositionFromPrefix prefix)

        let size, setSize = React.useState (fun _ -> Rect.initSizeFromPrefix prefix)

        let innerWidth, setInnerWidth =
            React.useState (fun _ -> Browser.Dom.window.innerWidth)

        let innerHeight, setInnerHeight =
            React.useState (fun _ -> Browser.Dom.window.innerHeight)

        let element = React.useElementRef ()

        ResizeEventListener.windowSizeChange setInnerWidth setInnerHeight

        let debouncedAdaptElement =
            React.useDebouncedCallback (
                fun () ->
                    match position, size with
                    | Some position, Some size ->
                        ResizeEventListener.adaptElement
                            (int innerWidth)
                            (int innerHeight)
                            size
                            position
                            setSize
                            setPosition
                    | _, _ -> ()
                , 100
            )

        React.useEffectOnce (fun _ -> debouncedAdaptElement ())

        React.useEffect (
            (fun () ->
                //Adapt position when the size of the element is changed so that it is visible
                debouncedAdaptElement ()
            //React shall only be used, when the size of the element is changed
            ),
            [| box innerWidth; box innerHeight |]
        )

        React.useLayoutEffectOnce (fun _ ->
            position
            |> Option.iter (fun position ->
                MoveEventListener.ensurePositionInsideWindow element position
                |> Some
                |> setPosition
            )
        ) // Reposition widget inside window

        let resizeElement (content: ReactElement) =
            Html.div [
                prop.ref element
                prop.onMouseDown (fun e -> // resize
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
                    style.zIndex 40
                    style.cursor.eastWestResize //style.cursor.northWestSouthEastResize;
                    style.display.flex
                    style.position.fixedRelativeToWindow
                    style.minWidth.minContent
                    if size.IsSome then
                        style.width size.Value.X
                    else
                        style.maxWidth 600
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
            prop.onMouseDown (fun e -> e.stopPropagation ())
            prop.className "swt:cursor-default swt:flex swt:flex-col swt:grow swt:max-h-[60%] swt:overflow-visible"
            prop.children [
                Html.div [
                    prop.onMouseDown (fun e -> // move
                        e.preventDefault ()
                        e.stopPropagation ()
                        let x = e.clientX - element.current.Value.offsetLeft
                        let y = e.clientY - element.current.Value.offsetTop
                        let startPosition = { X = int x; Y = int y }
                        let onmousemove = MoveEventListener.onmousemove element startPosition setPosition
                        let onmouseup = fun _ -> MoveEventListener.onmouseup (prefix, element) onmousemove
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

    static member BuildingBlock(model, dispatch, rmv: MouseEvent -> unit) =
        let content = SearchComponent.Main(model, dispatch)
        let prefix = WidgetLiterals.BuildingBlock
        Widget.Base(content, prefix, rmv, prefix)


    [<ReactComponent>]
    static member Templates(model: Model, dispatch, rmv: MouseEvent -> unit) =

        let content =
            Html.div [
                prop.className "swt:flex swt:flex-col swt:gap-2 swt:overflow-y-hidden"
                prop.children [ Protocol.Templates.TemplateSelect(model, dispatch) ]
            ]

        let prefix = WidgetLiterals.Templates
        Widget.Base(content, prefix, rmv, prefix)

    static member FilePicker(model, dispatch, rmv) =
        let content =
            Pages.FilePicker.Main(model, dispatch, "swt:@container/filePickerWidget swt:min-w-32")

        let prefix = WidgetLiterals.FilePicker
        Widget.Base(content, prefix, rmv, prefix)

    [<ReactComponent>]
    static member DataAnnotator(model: Model, dispatch, rmv) =

        let content = Pages.DataAnnotator.Main(model, dispatch)

        let prefix = WidgetLiterals.DataAnnotator
        Widget.Base(content, prefix, rmv, prefix)
