namespace Arcitisa.Components.Primitive.Dropdown

open Fable.Core
open Feliz
open Arcitisa.Components

[<Erase; Mangle(false)>]
type Dropdown =

    [<ReactComponent(true)>]
    static member Main
        (
            isOpen,
            setIsOpen,
            toggle: ReactElement,
            children: ReactElement,
            ?dropdownClassName: string,
            ?contentClassName: string,
            ?closeOnClick: bool
        ) =

        let closeOnClick = defaultArg closeOnClick true

        let classNameDropdownContent =
            defaultArg
                contentClassName
                "swt:w-max swt:max-w-none swt:menu swt:bg-base-200 swt:rounded-box swt:z-99 swt:p-2 swt:shadow-sm swt:top-110%"

        let ref = React.useElementRef ()

        React.useListener.onClickAway (ref, fun _ -> setIsOpen false)

        Html.div [
            prop.ref ref
            prop.className [
                "swt:dropdown swt:inline-block"
                if isOpen then
                    "swt:dropdown-open"
                defaultArg dropdownClassName ""
            ]
            prop.children [
                toggle
                if isOpen then
                    Html.ul [
                        prop.tabIndex 0
                        prop.className [ "swt:dropdown-content"; classNameDropdownContent ]

                        //Close dropdown on click
                        if closeOnClick then
                            prop.onClick (fun _ -> setIsOpen false)
                        prop.children children
                    ]
            ]
        ]
