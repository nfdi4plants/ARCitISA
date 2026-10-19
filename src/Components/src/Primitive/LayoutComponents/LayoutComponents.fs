namespace Swate.Components.Primitive.LayoutComponents

open Feliz
open Swate.Components
open Swate.Components.Primitive

[<RequireQualifiedAccess>]
type LayoutComponents =

    [<ReactComponent>]
    static member FieldTitle(title: string) =
        Html.h5 [
            prop.className "swt:text-primary swt:font-semibold"
            prop.text title
        ]

    [<ReactComponent>]
    static member BoxedField(?title: string, ?description: string, ?content: ReactElement list) =
        Html.div [
            prop.className
                "swt:card swt:card-sm swt:space-y-6 swt:border-2 swt:border-base-300 swt:shadow-xl swt:bg-base swt:prose swt:prose-headings:text-primary swt:max-w-full swt:lg:max-w-200 swt:w-full"
            prop.children [
                Html.div [
                    prop.className "swt:card-body"
                    prop.children [
                        Html.div [
                            prop.children [
                                if title.IsSome then
                                    Html.h1 [ prop.className "swt:mt-0"; prop.text title.Value ]
                                if description.IsSome then
                                    Html.p [
                                        prop.className "swt:text-sm swt:text-base-content/80"
                                        prop.text description.Value
                                    ]
                            ]
                        ]
                        if content.IsSome then
                            Html.div [
                                prop.className "swt:divide-y swt:divide-base-content"
                                prop.children (
                                    content.Value
                                    |> List.map (fun element ->
                                        Html.div [ prop.className "swt:py-2"; prop.children [ element ] ]
                                    )
                                )
                            ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member Section(children: ReactElement seq) =
        Html.section [
            prop.className "swt:overflow-auto swt:py-4"
            prop.children [
                Html.div [
                    prop.className "swt:flex swt:flex-col swt:gap-4 swt:w-full swt:items-center"
                    prop.children children
                ]
            ]
        ]

    [<ReactComponent>]
    static member Collapse (title: ReactElement seq) (content: ReactElement seq) =
        Html.div [
            prop.className
                "swt:collapse swt:collapse-plus swt:grow swt:border swt:has-checked:border-transparent swt:has-checked:bg-base-200"
            prop.children [
                Html.input [ prop.type'.checkbox; prop.className "peer" ]
                Html.div [
                    prop.className
                        "swt:collapse-title swt:after:text-primary swt:@md/main:after:size-4! swt:@md/main:after:text-xl swt:flex swt:gap-4"
                    prop.children title
                ]
                Html.div [
                    prop.className "swt:collapse-content swt:space-y-4 swt:cursor-default"
                    prop.children content
                ]
            ]
        ]

    [<ReactComponent>]
    static member CollapseTitle(title: string, subtitle: string, ?count: string) =
        React.Fragment [
            Html.div [
                Html.h5 [
                    prop.className "swt:text-md swt:font-semibold"
                    prop.text title
                ]
                Html.div [
                    prop.className "not-prose swt:text-xs swt:text-base-content/70"
                    prop.children [ Html.span [ prop.text subtitle ] ]
                ]
            ]
            if count.IsSome then
                Html.div [
                    prop.className "not-prose swt:flex swt:flex-col swt:ml-auto swt:items-center swt:justify-center"
                    prop.children [
                        Icons.Edit()
                        Html.div [ prop.className "swt:text-sm"; prop.text count.Value ]
                    ]
                ]
        ]
