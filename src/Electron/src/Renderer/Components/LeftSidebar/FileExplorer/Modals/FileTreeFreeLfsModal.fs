namespace Renderer.Components.LeftSidebar.FileExplorer.Modals

open Fable.Core
open Feliz
open Swate.Components.Primitive.BaseModal

[<Erase; Mangle(false)>]
type FileTreeFreeLfsModal =

    [<ReactComponent>]
    static member Main(isOpen: bool, itemName: string option, close: unit -> unit, submit: unit -> unit) =

        let setIsOpen isOpen =
            if not isOpen then
                close ()

        let displayName = itemName |> Option.defaultValue "this file"

        let footer =
            Html.div [
                prop.className "swt:flex swt:gap-2 swt:justify-end swt:w-full"
                prop.children [
                    Html.button [
                        prop.className "swt:btn swt:btn-ghost"
                        prop.onClick (fun _ -> close ())
                        prop.text "Cancel"
                    ]
                    Html.button [
                        prop.className "swt:btn swt:btn-warning"
                        prop.onClick (fun _ -> submit ())
                        prop.text "Free local copy"
                    ]
                ]
            ]

        BaseModal.Modal(
            isOpen = isOpen,
            setIsOpen = setIsOpen,
            header = Html.text "Free local copy",
            description =
                Html.text
                    $"Remove the local copy of '{displayName}'? The file stays in Git LFS and can be downloaded again.",
            children = Html.none,
            footer = footer,
            debug = "arc-free-lfs"
        )
