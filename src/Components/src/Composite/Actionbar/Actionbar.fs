namespace Swate.Components.Composite.Actionbar

open Feliz
open Fable.Core

open Swate.Components
open Swate.Components.Composite.Actionbar.Types
open Swate.Components.Primitive
open Swate.Components.Primitive.ContextMenu
open Swate.Components.Primitive.ContextMenu.Types

[<Erase; Mangle(false)>]
type Actionbar =

    [<ReactComponent>]
    static member private Button
        (
            buttonInfo: ButtonInfo,
            buttonSize: DaisyuiSize,
            tooltipPosition: DaisyuiTooltipPosition,
            ?buttonClassName: string,
            ?buttonTestId: string,
            ?debug: bool
        ) =

        let debug = defaultArg debug false

        let toolTipPosition =
            match tooltipPosition with
            | DaisyuiTooltipPosition.Top -> "swt:tooltip-top"
            | DaisyuiTooltipPosition.Right -> "swt:tooltip-right"
            | DaisyuiTooltipPosition.Bottom -> "swt:tooltip-bottom"
            | DaisyuiTooltipPosition.Left -> "swt:tooltip-left"

        let btnSize =
            match buttonSize with
            | DaisyuiSize.XS -> "swt:btn-xs"
            | DaisyuiSize.SM -> "swt:btn-sm"
            | DaisyuiSize.MD -> "swt:btn-md"
            | DaisyuiSize.LG -> "swt:btn-lg"
            | DaisyuiSize.XL -> "swt:btn-xl"

        let Button =
            Html.button [
                if debug then
                    prop.testId "button-test"
                prop.className [
                    match buttonClassName with
                    | Some customClass -> customClass
                    | None -> "swt:btn swt:btn-ghost swt:btn-square"
                    btnSize
                ]
                if buttonTestId.IsSome then
                    prop.testId buttonTestId.Value
                prop.children [
                    Html.i [ prop.className [ "swt:iconify " + buttonInfo.icon ] ]
                ]
                prop.onClick buttonInfo.onClick
            ]

        match buttonInfo.toolTip with
        | None -> Button
        | Some tooltip ->
            Html.div [
                prop.className [ "swt:tooltip"; toolTipPosition ]
                prop.ariaLabel tooltip
                prop.children [
                    Html.div [ prop.className "swt:tooltip-content"; prop.text tooltip ]
                    Button
                ]
            ]

    [<ReactComponent>]
    static member MaterialIcon(icon: string, ?styling: bool) =

        let styling = defaultArg styling false

        Html.i [
            prop.className [ "swt:iconify " + icon ]
            if styling then
                prop.style [ style.transform [ transform.rotate 90 ] ]
        ]

    [<ReactComponent>]
    static member private ContextMenu(containerRef, buttons: ButtonInfo[], ?portalRootRef, ?renderTrigger, ?debug) =

        let buttonElements =
            buttons
            |> Array.map (fun (button: ButtonInfo) ->
                ContextMenuItem(
                    Html.li [ prop.text (Option.defaultValue "" button.toolTip) ],
                    Actionbar.MaterialIcon(button.icon),
                    ?label = button.toolTip,
                    onClick = (fun event -> button.onClick event.buttonEvent)
                )
            )
            |> List.ofArray

        ContextMenu.ContextMenu(
            (fun _ -> buttonElements),
            ref = containerRef,
            onSpawn =
                (fun e ->
                    let target = e.target :?> Browser.Types.HTMLElement
                    Some target
                ),
            ?portalRootRef = portalRootRef,
            ?renderTrigger = renderTrigger,
            ?debug = debug
        )

    [<ReactComponent>]
    static member private RestElement
        (
            buttons: ButtonInfo[],
            buttonSize,
            tooltipPosition,
            ?buttonClassName,
            ?keepContextMenuPortalLocal: bool,
            ?debug: bool
        ) =

        let debug = defaultArg debug false

        let containerRef = React.useElementRef ()

        if Array.isEmpty buttons then
            Html.none
        else
            let renderTrigger openMenu =
                let buttonInfo =
                    ButtonInfo.create (
                        "swt:fluent--line-horizontal-1-dot-20-regular swt:size-5",
                        "Show more options",
                        (fun _ -> openMenu ())
                    )

                Actionbar.Button(
                    buttonInfo,
                    buttonSize,
                    tooltipPosition,
                    ?buttonClassName = buttonClassName,
                    debug = debug,
                    buttonTestId = "actionbar-rest-button"
                )

            Html.div [
                prop.ref containerRef
                if debug then
                    prop.testId "actionbar-test"
                prop.children [
                    Actionbar.ContextMenu(
                        containerRef,
                        buttons,
                        renderTrigger = renderTrigger,
                        ?portalRootRef =
                            (if defaultArg keepContextMenuPortalLocal false then
                                 Some containerRef
                             else
                                 None),
                        debug = debug
                    )
                ]
            ]

    [<ReactComponent(true)>]
    static member Main
        (
            buttons: ButtonInfo[],
            maxNumber: int,
            ?debug: bool,
            ?barClassName: string,
            ?buttonSize: DaisyuiSize,
            ?tooltipPosition: DaisyuiTooltipPosition,
            ?buttonClassName: string,
            ?keepContextMenuPortalLocal: bool
        ) =

        let debug = defaultArg debug false
        let buttonSize = defaultArg buttonSize DaisyuiSize.MD
        let tooltipPosition = defaultArg tooltipPosition DaisyuiTooltipPosition.Bottom

        let visibleCount = max 0 (min maxNumber buttons.Length)
        let visibleButtons, overflowButtons = Array.splitAt visibleCount buttons

        let selectedElements =
            visibleButtons
            |> Array.map (fun button ->
                Actionbar.Button(
                    button,
                    debug = debug,
                    buttonSize = buttonSize,
                    tooltipPosition = tooltipPosition,
                    ?buttonClassName = buttonClassName
                )
            )

        let restElements =
            Actionbar.RestElement(
                overflowButtons,
                buttonSize,
                tooltipPosition,
                ?buttonClassName = buttonClassName,
                ?keepContextMenuPortalLocal = keepContextMenuPortalLocal,
                debug = debug
            )

        let selectedElement = React.Fragment selectedElements

        Html.div [
            prop.className [
                barClassName
                |> Option.defaultValue "swt:flex swt:items-center swt:w-max swt:p-1"
            ]
            prop.children [ selectedElement; restElements ]
        ]

    [<ReactComponent>]
    static member Entry(maxNumber, ?debug: bool) =

        let newARCButton =
            ButtonInfo.create ("swt:fluent--document-add-24-regular swt:size-5", "Create a new ARC", fun _ -> ())

        let openARCButton =
            ButtonInfo.create ("swt:fluent--folder-arrow-up-24-regular swt:size-5", "Open an existing ARC", fun _ -> ())

        let downLoadARCButton =
            ButtonInfo.create (
                "swt:fluent--cloud-arrow-down-24-regular swt:size-5",
                "Download an existing ARC",
                fun _ -> ()
            )

        let standardButtons = [|
            newARCButton
            openARCButton
            downLoadARCButton
            newARCButton
            openARCButton
        |]

        Actionbar.Main(standardButtons, maxNumber, ?debug = debug)
