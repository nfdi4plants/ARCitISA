namespace Components

open Feliz
open Arcitisa.Components
open Arcitisa.Components.Primitive

open Browser.Dom

type NavbarBurger =

    static member private BurgerSwap(isOpen, setIsOpen) =
        Html.button [
            prop.children [
                Html.label [
                    prop.onClick (fun _ -> setIsOpen (not isOpen))
                    prop.className [
                        "swt:swap swt:swap-rotate swt:z-[10] swt:cursor-pointer"
                        if isOpen then
                            "swt:swap-active"
                    ]
                    prop.children [
                        // hamburger icon
                        Svg.svg [
                            svg.className "swt:swap-off swt:fill-current swt:size-6"
                            svg.xmlns "http://www.w3.org/2000/svg"
                            svg.viewBox (0, 0, 512, 512)
                            svg.children [
                                Svg.path [
                                    svg.d "M64,384H448V341.33H64Zm0-106.67H448V234.67H64ZM64,128v42.67H448V128Z"
                                ]
                            ]
                        ]

                        // close icon
                        Svg.svg [
                            svg.className "swt:swap-on swt:fill-current swt:size-6"
                            svg.xmlns "http://www.w3.org/2000/svg"
                            svg.viewBox (0, 0, 512, 512)
                            svg.children [
                                Svg.polygon [
                                    svg.points
                                        "400 145.49 366.51 112 256 222.51 145.49 112 112 145.49 222.51 256 112 366.51 145.49 400 256 289.49 366.51 400 400 366.51 289.49 256 400 145.49"
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]

    static member private DropdownItem(text: string, icon: ReactElement, props: IReactProperty) =
        Html.li [
            Html.a [
                props
                prop.className "swt:flex swt:flex-row swt:justify-between swt:items-center"
                prop.children [ Html.span [ prop.text text ]; icon ]
            ]
        ]


    [<ReactComponent>]
    static member Main(model, dispatch, ?host: Arcitisahost) =
        let isOpen, setIsOpen = React.useState (false)

        let navigateTo =
            fun (mainPage: Routing.MainPage) ->

                Messages.PageState.UpdateMainPage mainPage |> Messages.PageStateMsg |> dispatch

        let openUrlInBrowser url : IReactProperty =
            prop.onClick (fun ev -> window.``open`` (url, "_blank") |> ignore)

        Components.BaseDropdown.Main(
            isOpen,
            setIsOpen,
            NavbarBurger.BurgerSwap(isOpen, setIsOpen),
            [
                NavbarBurger.DropdownItem(
                    "Settings",
                    Icons.Cog("swt:size-4"),
                    prop.onClick (fun _ -> navigateTo Routing.MainPage.Settings)
                )
                NavbarBurger.DropdownItem(
                    "About",
                    Icons.About("swt:size-4"),
                    prop.onClick (fun _ -> navigateTo Routing.MainPage.About)
                )
                NavbarBurger.DropdownItem(
                    "Privacy Policy",
                    Icons.PrivacyPolicy("swt:size-4"),
                    prop.onClick (fun _ -> navigateTo Routing.MainPage.PrivacyPolicy)
                )
                NavbarBurger.DropdownItem(
                    "Docs",
                    Icons.Docs("swt:size-4"),
                    prop.href Arcitisa.Components.Shared.URLs.ARCITISA_WIKI
                )
                NavbarBurger.DropdownItem(
                    "Contact",
                    Icons.Contact("swt:size-4"),
                    prop.href Arcitisa.Components.Shared.URLs.CONTACT
                )
            ],
            style = Style.init "swt:dropdown-end"
        )
