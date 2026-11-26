namespace Renderer.Components.Helper

open Fable.Core
open Feliz
open Arcitisa.Components.Primitive.BaseModal

[<Erase; Mangle(false)>]
type FileTargetConflictModal =

    [<ReactComponent>]
    static member Main
        (isOpen: bool, targetPath: string option, close: unit -> unit, overwrite: unit -> unit, ?isBusy: bool)
        =

        let isBusy = defaultArg isBusy false
        let displayPath = targetPath |> Option.defaultValue "the selected target"

        let setIsOpen isOpen =
            if not isOpen && not isBusy then
                close ()

        let footer =
            Html.div [
                prop.className "swt:flex swt:gap-2 swt:justify-end swt:w-full"
                prop.children [
                    Html.button [
                        prop.className "swt:btn swt:btn-ghost"
                        prop.disabled isBusy
                        prop.onClick (fun _ -> close ())
                        prop.text "Rename"
                    ]
                    Html.button [
                        prop.className "swt:btn swt:btn-warning"
                        prop.disabled isBusy
                        prop.onClick (fun _ -> overwrite ())
                        prop.text (if isBusy then "Overwriting..." else "Overwrite")
                    ]
                ]
            ]

        BaseModal.Modal(
            isOpen = isOpen,
            setIsOpen = setIsOpen,
            header = Html.text "Target Already Exists",
            description =
                Html.text $"A file already exists at '{displayPath}'. Rename the note or overwrite the existing file.",
            children = Html.none,
            footer = footer,
            debug = "file-target-conflict"
        )
