module internal Swate.Components.Primitive.ComboBox.ComboBoxTests

open Feliz
open Vitest
open Swate.Components.Primitive.ComboBox.Types

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

Vitest.test (
    "onKeyDown gets every key and usedByList marks the Enter that picks an item and Escape",
    fun () ->
        let keys = ResizeArray<string * bool>()
        let picked = ResizeArray<string>()

        RTL.render (
            ComboBox.ComboBox<string>(
                inputValue = "",
                onInputChange = ignore,
                items = [| "Apple"; "Avocado" |],
                filterFn = (fun _ -> true),
                itemToString = id,
                onChange = (fun _ item -> picked.Add item),
                onKeyDown = (fun e -> keys.Add((e.key, ComboBoxKey.usedByList e)))
            )
        )
        |> ignore

        let combobox = RTL.screen.getByRole "combobox"

        let press (key: string) =
            RTL.fireEvent.custom ("keyDown", combobox, {| key = key; code = key |})

        RTL.fireEvent.change (combobox, {| target = {| value = "a" |} |})
        press "ArrowDown"
        press "Enter"
        // The list is closed now, so it leaves this Enter alone.
        press "Enter"
        press "Escape"

        Vitest.expect(picked.ToArray()).toEqual [| "Apple" |]

        // The arrow key belongs to the list navigation of Floating UI, so only the keys of the ComboBox are checked.
        Vitest.expect(keys.ToArray() |> Array.filter (fun (key, _) -> key <> "ArrowDown")).toEqual [|
            "Enter", true
            "Enter", false
            "Escape", true
        |]
)
