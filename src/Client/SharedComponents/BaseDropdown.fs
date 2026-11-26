namespace Components

open Feliz
open Arcitisa.Components

type BaseDropdown =
    [<ReactComponent>]
    static member Main(isOpen, setIsOpen, toggle: ReactElement, children: ReactElement seq, ?style: Style) =
        let ref = React.useElementRef ()
        React.useListener.onClickAway (ref, fun _ -> setIsOpen false)

        Html.div [
            prop.ref ref
            prop.className [
                "swt:dropdown"
                if isOpen then
                    "swt:dropdown-open"
                if style.IsSome then
                    style.Value.StyleString
            ]
            prop.children [
                toggle
                if isOpen then
                    Html.ul [
                        prop.tabIndex 0
                        prop.className [
                            "swt:dropdown-content swt:min-w-48 swt:menu swt:bg-base-200 swt:rounded-box swt:z-[9999999999999] swt:p-2 swt:shadow-sm swt:!top-[110%]"
                            if style.IsSome then
                                style.Value.GetSubclassStyle "content"
                        ]
                        prop.children children
                    ]
            ]
        ]
