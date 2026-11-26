namespace Pages

open Model
open Messages
open Feliz

module private AboutHelper =

    [<ReactComponent>]
    let IntroductionElement () =
        Html.div [

            prop.children [
                Html.p [
                    Html.b "ARCitISA"
                    Html.text
                        " is a workflow annotation tool for everyone. This tool provides an easy way to annotate experimental data in an excel application that every wet lab scientist is familiar with. If you are interested check out the full "
                    Html.a [
                        prop.href Arcitisa.Components.Shared.URLs.ARCITISA_WIKI
                        prop.target.blank
                        prop.text "documentation"
                    ]
                    Html.text " 📚."
                ]
            ]
        ]

    [<ReactComponent>]
    let MediaContainer (content: ReactElement, imageSrc: string, imageHref: string) =
        Html.div [
            prop.className "swt:hero"
            prop.children [
                Html.div [
                    prop.className "swt:hero-content swt:flex-col"
                    prop.children [
                        Html.div [ prop.className "swt:prose"; prop.children content ]
                        Html.div [
                            prop.className "not-prose"
                            prop.children [
                                Html.a [
                                    prop.href imageHref
                                    prop.className [ "swt:btn swt:btn-square swt:btn-primary swt:btn-lg" ]
                                    prop.children [
                                        Html.img [ prop.src imageSrc; prop.className "swt:size-8" ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    let GetInContactElements () =
        React.Fragment [
            MediaContainer(
                React.Fragment [
                    Html.strong "DataPLANT"
                    Html.br []
                    Html.p "ARCitISA is part of the DataPLANT organisation."
                    Html.p [
                        Html.text "Services and infrastructures to support "
                        Html.a [
                            prop.href "https://twitter.com/search?q=%23FAIRData&src=hashtag_click"
                            prop.target.blank
                            prop.text "#FAIRData"
                        ]
                        Html.text
                            " science and good data management practices within the plant basic research community. "
                        Html.a [
                            prop.href "https://twitter.com/search?q=%23NFDI&src=hashtag_click"
                            prop.target.blank
                            prop.text "#NFDI"
                        ]
                    ]

                    Html.p [
                        Html.text "Got a good idea or just want to get in touch? "
                        Html.a [
                            prop.href Arcitisa.Components.Shared.URLs.Helpdesk.Url
                            prop.target.blank
                            prop.text "Reach out to us!"
                        ]
                    ]
                ],
                "https://raw.githubusercontent.com/nfdi4plants/Branding/refs/heads/master/logos/DataPLANT/DataPLANT_logo_minimal_rounded_bg_black.svg",
                Arcitisa.Components.Shared.URLs.NfdiWebsite
            )

            MediaContainer(
                React.Fragment [
                    Html.strong "X - @nfdi4plants"
                    Html.br []
                    Html.span "Follow us on X for more up-to-date information about research data management! "
                    Html.a [
                        prop.href Arcitisa.Components.Shared.URLs.NFDITwitterUrl
                        prop.target.blank
                        prop.text "@nfdi4plants"
                    ]
                ],
                "/x-logo-black.png",
                Arcitisa.Components.Shared.URLs.NFDITwitterUrl
            )

            MediaContainer(
                React.Fragment [
                    Html.strong "GitHub"
                    Html.br []
                    Html.text "You can find the ARCitISA source code  "
                    Html.a [
                        prop.href Arcitisa.Components.Shared.URLs.ArcitisaRepo
                        prop.target.blank
                        prop.text "here"
                    ]
                    Html.text
                        ". Our developers are always happy to get in contact with you! If you don't have a GitHub account but want to reach out or want to snitch on some nasty bugs 🐛 you can tell us "
                    Html.a [
                        prop.href Arcitisa.Components.Shared.URLs.Helpdesk.UrlArcitisaTopic
                        prop.target.blank
                        prop.text "here"
                    ]
                    Html.text "."
                ],
                "/github-mark.png",
                Arcitisa.Components.Shared.URLs.ArcitisaRepo
            )
        ]

    [<ReactComponent>]
    let APIDocsElement () =
        Html.div [
            prop.className "swt:prose-sm swt:md:prose swt:lg:prose-lg"
            prop.children [
                Html.h2 "API Documentation"
                Html.div [
                    prop.className "swt:text-base-content/70"
                    prop.text "ARCitISA offers some APIs to retrieve ontology information."
                ]
                Html.ul [
                    prop.children [
                        Html.li [
                            Html.a [
                                prop.href Arcitisa.Components.Shared.URLs.Docs.IOntologyAPIv3
                                prop.target.blank
                                prop.text "IOntologyAPIv3"
                            ]
                            Html.span
                                " - API for searching and retrieving ontology terms and trees in ARCitISA. This is the API used by ARCitISA itself and thus the most up-to-date one. If you want to retrieve ontology information for your own applications, this is the API you should use."
                        ]
                    ]
                ]
            ]
        ]

type About =
    static member Main() =
        Html.div [
            prop.className "swt:prose-sm swt:md:prose swt:lg:prose-lg swt:divide-y-2 swt:gap-y-2 swt:py-1 swt:lg:py-4"
            prop.children [
                Html.h1 "ARCitISA"
                AboutHelper.IntroductionElement()
                AboutHelper.GetInContactElements()
                AboutHelper.APIDocsElement()
            ]
        ]
