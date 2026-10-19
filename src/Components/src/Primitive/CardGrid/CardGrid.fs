namespace Swate.Components.Primitive.CardGrid

open Fable.Core
open Feliz

[<Erase; Mangle(false)>]
type CardGrid =

    [<ReactComponent>]
    static member CardGridButton
        (icon: ReactElement, header: string, description: string, onclick, ?disabled: bool, ?testId: string)
        =
        let disabled = defaultArg disabled false

        Html.div [
            if testId.IsSome then
                prop.testId testId.Value
            // prop.className "swt:transform"
            prop.className [
                "swt:btn swt:text-left swt:transition-transform
                swt:w-full swt:min-h-[120px]
                swt:min-w-[120px] swt:max-w-[230px] swt:flex-col
                swt:gap-2 swt:border swt:border-base-content"

                if disabled then
                    "swt:btn-disabled swt:opacity-50"
                else
                    "swt:hover:scale-95"
            ]
            prop.role.button
            prop.tabIndex 0
            prop.onClick (fun _ ->
                if not disabled then
                    onclick ()
            )
            prop.children [
                Html.div [
                    prop.className "swt:flex swt:flex-row swt:gap-2 swt:font-bold swt:items-center swt:w-full"
                    prop.children [
                        Html.div [
                            prop.className
                                "swt:p-1 swt:border swt:border-primary swt:rounded swt:bg-primary/50 swt:text-primary-content/80 swt:h-fit swt:w-fit swt:flex swt:items-center"
                            prop.children [ icon ]
                        ]
                        Html.span header
                    ]
                ]
                Html.div [
                    prop.className "swt:text-xs swt:text-base-content/50 swt:text-left"
                    prop.text description
                ]
            ]
        ]

    [<ReactComponent(true)>]
    static member CardGrid
        (children: ReactElement, ?gridTitle: string, ?leadingElements: ReactElement, ?gridClassName: string)
        =
        Html.div [
            prop.className "swt:card swt:bg-base-300 swt:shadow-xl"
            prop.children [
                Html.div [
                    prop.className "swt:card-body swt:flex-1 swt:flex swt:flex-col swt:justify-center"
                    prop.children [
                        if gridTitle.IsSome then
                            Html.h3 [
                                prop.className "swt:flex swt:font-bold swt:text-xl swt:mb-6"
                                prop.text gridTitle.Value
                            ]
                        if leadingElements.IsSome then
                            leadingElements.Value
                        Html.div [
                            prop.className [
                                "swt:gap-6 swt:h-full swt:place-items-center"
                                gridClassName |> Option.defaultValue "swt:grid swt:grid-cols-2 swt:grid-rows-2"
                            ]
                            prop.children children
                        ]
                    ]
                ]
            ]
        ]
