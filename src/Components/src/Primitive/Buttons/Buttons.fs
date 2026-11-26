namespace Arcitisa.Components.Primitive.Buttons

open Feliz
open Fable.Core
open Arcitisa.Components
open Arcitisa.Components.Primitive

[<Erase; Mangle(false)>]
type Buttons =

    [<ReactComponent>]
    static member DeleteButton(?children, ?className: string, ?props: IReactProperty list) =
        Html.button [
            prop.className [
                "swt:btn swt:btn-square"
                if className.IsSome then
                    className.Value
            ]
            if props.IsSome then
                yield! props.Value
            prop.children [
                if children.IsSome then
                    yield! children.Value
                Svg.svg [
                    svg.xmlns "http://www.w3.org/2000/svg"
                    svg.className "swt:h-6 swt:w-6"
                    svg.fill "none"
                    svg.viewBox (0, 0, 24, 24)
                    svg.stroke "currentColor"
                    svg.children [
                        Svg.path [
                            svg.strokeLineCap "round"
                            svg.strokeLineJoin "round"
                            svg.strokeWidth 2
                            svg.d "M6 18L18 6M6 6l12 12"
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member CircularExitButton(?children, ?className: string, ?props: IReactProperty list) =
        Html.button [
            prop.className [
                "swt:btn swt:btn-outline swt:btn-circle"
                if className.IsSome then
                    className.Value
            ]
            if props.IsSome then
                yield! props.Value
            prop.children [
                if children.IsSome then
                    yield! children.Value
                Svg.svg [
                    svg.xmlns "http://www.w3.org/2000/svg"
                    svg.className "swt:h-6 swt:w-6"
                    svg.fill "none"
                    svg.viewBox (0, 0, 24, 24)
                    svg.stroke "currentColor"
                    svg.children [
                        Svg.path [
                            svg.strokeLineCap "round"
                            svg.strokeLineJoin "round"
                            svg.strokeWidth 2
                            svg.d "M6 18L18 6M6 6l12 12"
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member CollapseButton
        (isCollapsed, setIsCollapsed, ?collapsedIcon, ?collapseIcon, ?classes: string, ?classFn: bool -> string)
        =
        Html.label [
            prop.className [
                "swt:btn swt:btn-square swt:swap swt:swap-rotate swt:grow-0"
                if classes.IsSome then
                    classes.Value
                match classFn with
                | Some fn -> fn isCollapsed
                | None -> ()
            ]
            prop.onClick (fun e ->
                e.preventDefault ()
                e.stopPropagation ()
                not isCollapsed |> setIsCollapsed
            )
            prop.onMouseDown (fun e -> e.stopPropagation ())
            prop.children [
                Html.input [
                    prop.type'.checkbox
                    prop.isChecked isCollapsed
                    prop.onChange (fun (_: bool) -> ())
                ]
                Html.i [
                    prop.className "swt:swap-off"
                    prop.children [
                        if collapsedIcon.IsSome then
                            collapsedIcon.Value
                        else
                            Icons.ChevronDown()
                    ]
                ]
                Html.i [
                    prop.className "swt:swap-on"
                    prop.children [
                        if collapseIcon.IsSome then
                            collapseIcon.Value
                        else
                            Icons.Close()
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member QuickAccessButton
        (
            children: ReactElement,
            title: string,
            onclick: Browser.Types.MouseEvent -> unit,
            ?isDisabled: bool,
            ?props: IReactProperty seq,
            ?color: DaisyuiColors,
            ?classes: string,
            ?tooltipPosition: string
        ) =
        let isDisabled = defaultArg isDisabled false

        let tooltipPosition =
            defaultArg tooltipPosition "swt:tooltip-center swt:tooltip-bottom"

        Html.div [
            prop.className [ "swt:tooltip swt:z-99"; tooltipPosition ]
            prop.custom ("data-tip", title)
            prop.children [
                Html.button [
                    prop.className [
                        "swt:btn swt:btn-sm swt:btn-neutral swt:btn-square"

                        match color with
                        | Some DaisyuiColors.Primary -> "swt:hover:text-primary!"
                        | Some DaisyuiColors.Secondary -> "swt:hover:text-secondary!"
                        | Some DaisyuiColors.Accent -> "swt:hover:text-accent!"
                        | Some DaisyuiColors.Error -> "swt:hover:text-error!"
                        | Some DaisyuiColors.Info -> "swt:hover:text-info!"
                        | Some DaisyuiColors.Success -> "swt:hover:text-success!"
                        | Some DaisyuiColors.Warning -> "swt:hover:text-warning!"
                        | None -> "swt:hover:text-primary!"

                        if classes.IsSome then
                            classes.Value
                    ]
                    prop.tabIndex (if isDisabled then -1 else 0)
                    prop.disabled isDisabled
                    prop.onClick onclick
                    if props.IsSome then
                        yield! props.Value
                    prop.ariaLabel title
                    prop.children children
                ]
            ]
        ]
