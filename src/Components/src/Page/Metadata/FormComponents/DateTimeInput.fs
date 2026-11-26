namespace Arcitisa.Components.Page.Metadata.FormComponents

open Fable.Core
open System
open Arcitisa.Components
open Feliz
open Arcitisa.Components.Primitive.LayoutComponents

[<Erase; Mangle(false)>]
type DateTimeInput =

    [<ReactComponent>]
    static member DateTimeInput(inputValue: string, setter: string -> unit, ?label: string) =
        let inputRef = React.useInputRef ()
        let debouncedSetter = React.useDebouncedCallback setter

        React.useEffect (
            (fun () ->
                if inputRef.current.IsSome then
                    inputRef.current.Value.value <- inputValue
            ),
            [| box inputValue |]
        )

        Html.div [
            prop.className "swt:grow"
            prop.children [
                if label.IsSome then
                    LayoutComponents.FieldTitle label.Value
                Html.input [
                    prop.className "swt:input"
                    prop.type'.dateTimeLocal
                    prop.ref inputRef
                    prop.onChange (fun (dateValue: DateTime) ->
                        dateValue.ToString("yyyy-MM-ddTHH:mm") |> debouncedSetter
                    )
                ]
            ]
        ]
