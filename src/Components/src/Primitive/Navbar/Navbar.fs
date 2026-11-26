namespace Arcitisa.Components.Primitive.Navbar

open Feliz
open Fable.Core

[<Erase; Mangle(false)>]
type Navbar =

    [<ReactComponent>]
    static member Main
        (?left: ReactElement, ?middle: ReactElement, ?right: ReactElement, ?debug: bool, ?className: string)
        =
        let debug = defaultArg debug false
        let left = defaultArg left (Html.div [])
        let middle = defaultArg middle (Html.div [])
        let right = defaultArg right (Html.div [])

        Html.div [
            prop.className [
                "swt:text-base-content swt:gap-2 swt:flex swt:items-center swt:w-full swt:p-2 swt:shadow-xl swt:bg-base-200 swt:h-min"
                if className.IsSome then
                    className.Value
            ]
            prop.role "navigation"
            prop.ariaLabel "arc navigation"
            if debug then
                prop.testId "navbar-test"
            prop.children [
                Html.div [
                    prop.className "swt:grow-0 swt:flex swt:flex-row swt:gap-2"
                    prop.children left
                ]
                Html.div [
                    prop.className "swt:grow swt:flex swt:flex-row swt:text-center swt:gap-2"
                    prop.children middle
                ]
                Html.div [
                    prop.className "swt:grow-0 swt:flex swt:flex-row swt:gap-2"
                    prop.children right
                ]
            ]
        ]

    [<ReactComponent(true)>]
    static member Entry(?debug: bool) =

        Navbar.Main(Html.div [], ?debug = debug)
