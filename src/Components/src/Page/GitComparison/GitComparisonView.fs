namespace Arcitisa.Components.Page

open Feliz

module internal GitComparisonView =

    let private PanelShellClassName =
        "swt:rounded-box swt:border swt:border-base-300 swt:bg-base-100 swt:overflow-hidden swt:shadow-sm"

    [<ReactComponent>]
    let TitleStack (title: ReactElement) (description: ReactElement option) (className: string option) =
        Html.div [
            prop.className [
                "swt:flex swt:flex-col swt:gap-1"
                if className.IsSome then
                    className.Value
            ]
            prop.children [
                title
                if description.IsSome then
                    description.Value
            ]
        ]

    [<ReactComponent>]
    let HeaderRow (leading: ReactElement) (trailing: ReactElement) (className: string option) =
        Html.div [
            prop.className [
                "swt:flex swt:flex-wrap swt:items-center swt:justify-between swt:gap-3 swt:px-4 swt:py-3"
                if className.IsSome then
                    className.Value
            ]
            prop.children [ leading; trailing ]
        ]

    [<ReactComponent>]
    let PanelShell
        (children: ReactElement)
        (testId: string option)
        (className: string option)
        (styleAttributes: IStyleAttribute list option)
        =
        Html.div [
            if testId.IsSome then
                prop.testId testId.Value
            prop.className [
                PanelShellClassName
                if className.IsSome then
                    className.Value
            ]
            if styleAttributes.IsSome then
                prop.style styleAttributes.Value
            prop.children children
        ]

    [<ReactComponent>]
    let SectionCard (header: ReactElement) (body: ReactElement) (key: string option) (className: string option) =
        Html.section [
            if key.IsSome then
                prop.key key.Value
            prop.className [
                "swt:rounded-box swt:border swt:border-base-300 swt:bg-base-100"
                if className.IsSome then
                    className.Value
            ]
            prop.children [ header; body ]
        ]
