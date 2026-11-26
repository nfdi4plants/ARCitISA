namespace View

open Feliz
open Messages
open Arcitisa.Components
open Arcitisa.Components.Primitive

module private MainPageUtil =
    [<Literal>]
    let DrawerId = "MainPageDrawerId"


open MainPageUtil

type MainPageView =

    static member DrawerSideContentItem(model: Model.Model, route: Routing.MainPage, onclick) =
        let isActive = model.PageState.MainPage = route

        Html.li [
            prop.onClick onclick
            prop.children [
                Html.a [
                    prop.className [
                        if isActive then
                            "swt:bg-primary swt:text-primary-content"
                    ]
                    prop.text route.AsStringRdbl
                ]
            ]
        ]

    static member DrawerSideContent(model: Model.Model, dispatch) =
        Html.div [
            prop.className "swt:bg-base-200 swt:text-base-content swt:min-h-full swt:w-80 swt:p-4"
            prop.children [
                Html.button [
                    prop.className "swt:btn swt:btn-link swt:btn-sm"
                    prop.role "navigation"
                    prop.ariaLabel "Back to spreadsheet view"
                    prop.onClick (fun _ ->
                        PageState.UpdateMainPage Routing.MainPage.Default |> PageStateMsg |> dispatch
                    )
                    prop.children [ Icons.Back(); Html.span "Back" ]
                ]
                Html.ul [
                    prop.className "swt:menu swt:gap-y-1"
                    prop.children [
                        MainPageView.DrawerSideContentItem(
                            model,
                            Routing.MainPage.Settings,
                            fun _ -> PageState.UpdateMainPage Routing.MainPage.Settings |> PageStateMsg |> dispatch
                        )
                        MainPageView.DrawerSideContentItem(
                            model,
                            Routing.MainPage.About,
                            fun _ -> PageState.UpdateMainPage Routing.MainPage.About |> PageStateMsg |> dispatch
                        )
                        MainPageView.DrawerSideContentItem(
                            model,
                            Routing.MainPage.PrivacyPolicy,
                            fun _ ->
                                PageState.UpdateMainPage Routing.MainPage.PrivacyPolicy
                                |> PageStateMsg
                                |> dispatch
                        )
                    ]
                ]
            ]
        ]

    static member Navbar(model: Model.Model, dispatch) =
        Components.BaseNavbar.Glow [
            Html.label [
                prop.className "swt:btn swt:btn-square swt:btn-ghost swt:md:hidden"
                prop.htmlFor DrawerId
                prop.children [
                    Svg.svg [
                        svg.xmlns "http://www.w3.org/2000/svg"
                        svg.className "swt:size-5"
                        svg.fill "none"
                        svg.viewBox (0, 0, 24, 24)
                        svg.stroke "currentColor"
                        svg.children [
                            Svg.path [
                                svg.strokeLineCap "round"
                                svg.strokeLineJoin "round"
                                svg.strokeWidth 2
                                svg.d "M4 6h16M4 12h16M4 18h7"
                            ]
                        ]
                    ]
                ]
            ]

            Components.Logo.Main(
                onClick = (fun _ -> PageState.UpdateMainPage Routing.MainPage.Default |> PageStateMsg |> dispatch)
            )
        ]

    static member MainContent(model: Model.Model, dispatch) =
        match model.PageState.MainPage with
        | Routing.MainPage.Settings -> Pages.Settings.Main(model, dispatch)
        | Routing.MainPage.About -> Pages.About.Main()
        | Routing.MainPage.PrivacyPolicy -> Pages.PrivacyPolicy.Main()
        | _ ->
            Html.div [
                prop.className "swt:flex swt:flex-col swt:items-center"
                prop.children [
                    Html.h1 [ prop.text "404"; prop.className "swt:text-4xl" ]
                ]
            ]

    static member Main(model: Model.Model, dispatch) =
        Html.div [
            prop.className "swt:drawer swt:md:drawer-open"
            prop.children [
                Html.input [
                    prop.id DrawerId
                    prop.type'.checkbox
                    prop.className "swt:drawer-toggle"
                ]
                Html.div [
                    prop.className "swt:drawer-content swt:flex swt:flex-col swt:items-center swt:overflow-y-auto"
                    prop.children [
                        MainPageView.Navbar(model, dispatch)
                        MainPageView.MainContent(model, dispatch)
                    ]
                ]
                Html.div [
                    prop.className "swt:drawer-side swt:z-10"
                    prop.children [
                        Html.label [
                            prop.className "swt:drawer-overlay"
                            prop.htmlFor DrawerId
                            prop.ariaLabel "Close sidebar"
                        ]
                        MainPageView.DrawerSideContent(model, dispatch)
                    ]
                ]
            ]
        ]
