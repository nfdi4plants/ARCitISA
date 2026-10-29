module internal Swate.Components.Primitive.BaseModal.BaseModalTests

open Browser.Dom
open Feliz
open Vitest

Vitest.afterEach (fun () -> RTL.cleanup ())

/// Renders an open modal and returns the values passed to setIsOpen.
let private renderModal (dismissible: bool option) =
    let calls = ResizeArray<bool>()

    RTL.render (
        BaseModal.Modal(
            isOpen = true,
            setIsOpen = (fun value -> calls.Add value),
            header = Html.text "Header",
            children = Html.text "Content",
            debug = "t",
            ?dismissible = dismissible
        )
    )
    |> ignore

    calls

let private pressEscape () =
    RTL.fireEvent.custom ("keyDown", RTL.screen.getByTestId "modal_t", {| key = "Escape" |})

Vitest.test (
    "the header close button calls setIsOpen false",
    fun () ->
        let calls = renderModal None
        RTL.fireEvent.click (RTL.screen.getByTestId "modal-close")

        Vitest.expect(calls.ToArray()).toEqual [| false |]
)

Vitest.test (
    "Escape closes a dismissible modal",
    fun () ->
        let calls = renderModal None
        pressEscape ()

        Vitest.expect(calls.ToArray()).toEqual [| false |]
)

Vitest.test (
    "a non-dismissible modal has no close button and ignores Escape and outside presses",
    fun () ->
        let calls = renderModal (Some false)

        Vitest.expect(RTL.screen.queryByTestId("modal-close").IsNone).toBe true

        pressEscape ()
        RTL.fireEvent.custom ("mouseDown", document.body)

        Vitest.expect(calls.Count).toBe 0
)
