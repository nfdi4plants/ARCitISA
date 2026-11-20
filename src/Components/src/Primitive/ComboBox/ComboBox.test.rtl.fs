module internal Swate.Components.Primitive.ComboBox.ComboBoxTests

open Feliz
open Vitest

Vitest.afterEach (fun () -> RTL.cleanup ())

/// Renders a combobox with an open list next to an unrelated input and counts onBlur calls.
let private renderOpen () =
    let blurs = ResizeArray<int>()

    RTL.render (
        React.Fragment [
            ComboBox.ComboBox<string>(
                inputValue = "",
                onInputChange = ignore,
                items = [| "Apple"; "Avocado" |],
                filterFn = (fun _ -> true),
                itemToString = id,
                onBlur = (fun _ -> blurs.Add 1)
            )
            Html.input [ prop.testId "outside" ]
        ]
    )
    |> ignore

    let combobox = RTL.screen.getByRole "combobox"
    RTL.fireEvent.change (combobox, {| target = {| value = "a" |} |})
    Vitest.expect(RTL.screen.queryByRole("listbox").IsSome).toBe true
    combobox, blurs

Vitest.test (
    "focus moving to an element outside the open list closes it and reports the blur",
    fun () ->
        let combobox, blurs = renderOpen ()

        RTL.fireEvent.custom (
            "focusOut",
            combobox,
            {|
                relatedTarget = RTL.screen.getByTestId "outside"
            |}
        )

        Vitest.expect(RTL.screen.queryByRole("listbox").IsSome).toBe false
        Vitest.expect(blurs.Count).toBe 1
)

Vitest.test (
    "a blur without a related element keeps the open list and does not report the blur",
    fun () ->
        let combobox, blurs = renderOpen ()

        RTL.fireEvent.custom ("focusOut", combobox)

        Vitest.expect(RTL.screen.queryByRole("listbox").IsSome).toBe true
        Vitest.expect(blurs.Count).toBe 0
)
