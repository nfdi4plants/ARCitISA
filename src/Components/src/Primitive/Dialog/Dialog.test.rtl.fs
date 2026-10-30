module internal Swate.Components.Primitive.Dialog.DialogTests

open Vitest

Vitest.afterEach (fun () -> RTL.cleanup ())

let private renderDialog (validate: string -> Result<string, string>) =
    RTL.render (
        Dialog.StringSubmissionDialog(
            isOpen = true,
            title = "Rename",
            description = "Rename the item.",
            fieldLabel = "Name",
            initialValue = "",
            close = ignore,
            submit = ignore,
            validate = validate,
            submitLabel = "Rename",
            validationMessage = "fallback"
        )
    )
    |> ignore

let private typeName (name: string) =
    RTL.fireEvent.change (RTL.screen.getByRole "textbox", {| target = {| value = name |} |})

Vitest.test (
    "shows the validator's error text",
    fun () ->
        renderDialog (fun name -> if name = "a:b" then Error "custom reason" else Ok name)
        typeName "a:b"

        Vitest.expect(RTL.screen.getByText("custom reason").hidden).toBe false
        Vitest.expect(RTL.screen.queryByText("fallback").IsNone).toBe true
)

Vitest.test (
    "falls back to the validation message when the error text is empty",
    fun () ->
        renderDialog (fun name -> if name = "a:b" then Error "" else Ok name)
        typeName "a:b"

        Vitest.expect(RTL.screen.getByText("fallback").hidden).toBe false
)

Vitest.test (
    "shows no message for a valid name",
    fun () ->
        renderDialog Ok
        typeName "notes.md"

        Vitest.expect(RTL.screen.getByText("fallback").hidden).toBe true
)
