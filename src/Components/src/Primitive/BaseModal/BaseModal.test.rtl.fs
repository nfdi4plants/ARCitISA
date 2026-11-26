module internal Arcitisa.Components.Primitive.BaseModal.BaseModalTests

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
        RTL.fireEvent.custom ("keyDown", RTL.screen.getByTestId "modal_t", {| key = "Escape" |})

        Vitest.expect(calls.ToArray()).toEqual [| false |]
)

Vitest.test (
    "a non-dismissible modal has no close button and ignores Escape and outside presses",
    fun () ->
        let calls = renderModal (Some false)

        Vitest.expect(RTL.screen.queryByTestId("modal-close").IsNone).toBe true

        RTL.fireEvent.custom ("keyDown", RTL.screen.getByTestId "modal_t", {| key = "Escape" |})
        RTL.fireEvent.custom ("mouseDown", document.body)

        Vitest.expect(calls.Count).toBe 0
)

Vitest.test (
    "an outside mousedown closes a dismissible modal",
    fun () ->
        let calls = renderModal None
        RTL.fireEvent.custom ("mouseDown", document.body)

        Vitest.expect(calls.ToArray()).toEqual [| false |]
)

/// Waits past the animation frame in which the modal moves the focus.
let private afterFocusMove () =
    Fable.Core.JS.Constructors.Promise.Create(fun resolve _ ->
        Fable.Core.JS.setTimeout (fun () -> resolve ()) 50 |> ignore
    )

let private renderWithControls (children: ReactElement) =
    RTL.render (
        BaseModal.Modal(
            isOpen = true,
            setIsOpen = ignore,
            header = Html.text "Header",
            children = children,
            footer =
                React.Fragment [
                    Html.button [ prop.key "cancel"; prop.text "Cancel" ]
                    Html.button [ prop.key "ok"; prop.text "OK" ]
                ]
        )
    )
    |> ignore

let private button (name: string) =
    RTL.screen.getByRole ("button", ByRoleOptions(name = Text name))

Vitest.test (
    "a modal without controls in its content focuses the first footer button, not the close button",
    fun () -> promise {
        renderWithControls (Html.text "Delete this?")
        do! afterFocusMove ()

        Vitest.expect(document.activeElement).toBe (button "Cancel")
    }
)

Vitest.test (
    "a modal focuses the first control of its content",
    fun () -> promise {
        renderWithControls (Html.input [ prop.testId "first-input" ])
        do! afterFocusMove ()

        Vitest.expect(document.activeElement).toBe (RTL.screen.getByTestId "first-input")
    }
)

Vitest.test (
    "a control marked data-autofocus takes the focus before earlier controls",
    fun () -> promise {
        renderWithControls (
            React.Fragment [
                Html.input [ prop.key "a"; prop.testId "first-input" ]
                Html.input [
                    prop.key "b"
                    prop.testId "marked-input"
                    prop.custom ("data-autofocus", "")
                ]
            ]
        )

        do! afterFocusMove ()

        Vitest.expect(document.activeElement).toBe (RTL.screen.getByTestId "marked-input")
    }
)

Vitest.test (
    "a modal skips controls that are not shown, such as a hidden file input",
    fun () -> promise {
        renderWithControls (
            React.Fragment [
                Html.input [
                    prop.key "file"
                    prop.type'.file
                    prop.style [ style.display.none ]
                ]
                Html.div [
                    prop.key "hidden-parent"
                    prop.style [ style.display.none ]
                    prop.children [ Html.input [] ]
                ]
                Html.input [
                    prop.key "invisible"
                    prop.style [ style.visibility.hidden ]
                ]
                Html.button [ prop.key "pick"; prop.text "Choose files" ]
            ]
        )

        do! afterFocusMove ()

        Vitest.expect(document.activeElement).toBe (button "Choose files")
    }
)

Vitest.test (
    "a modal skips controls outside the tab order and inside an inert subtree",
    fun () -> promise {
        renderWithControls (
            React.Fragment [
                Html.button [
                    prop.key "untabbable"
                    prop.tabIndex -1
                    prop.text "Not in the tab order"
                ]
                Html.div [
                    prop.key "inert"
                    prop.custom ("inert", true)
                    prop.children [ Html.input [] ]
                ]
                Html.input [ prop.key "reachable"; prop.testId "reachable-input" ]
            ]
        )

        do! afterFocusMove ()

        Vitest.expect(document.activeElement).toBe (RTL.screen.getByTestId "reachable-input")
    }
)
