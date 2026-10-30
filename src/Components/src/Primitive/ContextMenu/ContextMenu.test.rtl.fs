module internal Swate.Components.Primitive.ContextMenu.ContextMenuTests

open Browser.Dom
open Fable.Core
open Feliz
open Vitest
open Swate.Components.Primitive.ContextMenu.Types

[<Erase; Mangle(false)>]
type private Harness =

    [<ReactComponent>]
    static member Menu(items: ContextMenuItem list ref) =
        let owner = React.useElementRef ()

        Html.div [
            prop.ref owner
            prop.testId "menu-owner"
            prop.children [
                ContextMenu.ContextMenu(
                    (fun _ -> items.Value),
                    owner,
                    renderTrigger =
                        (fun openMenu ->
                            Html.button [
                                prop.testId "menu-trigger"
                                prop.text "Open menu"
                                prop.onClick (fun _ -> openMenu ())
                            ]
                        )
                )
            ]
        ]

let private item (text: string) (label: string option) (selected: string ref) =
    ContextMenuItem(text = Html.span text, ?label = label, onClick = (fun _ -> selected.Value <- text))

let private openAtOwner () =
    RTL.fireEvent.custom (
        "contextMenu",
        RTL.screen.getByTestId "menu-owner",
        {|
            clientX = 40
            clientY = 40
            bubbles = true
        |}
    )

Vitest.afterEach (fun () -> RTL.cleanup ())

Vitest.test (
    "opening a menu registers item refs and typeahead skips divider slots",
    fun () -> promise {
        let selected = ref "none"

        let items =
            ref [
                item "Alpha" (Some "Alpha") selected
                ContextMenuItem(isDivider = true)
                item "Beta" None selected
            ]

        RTL.render (Harness.Menu items) |> ignore
        RTL.fireEvent.click (RTL.screen.getByTestId "menu-trigger")

        let! menu = RTL.screen.findByRole "menu"
        let beta = RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Beta"))
        menu.focus ()
        RTL.fireEvent.custom ("keyDown", menu, {| key = "b"; code = "KeyB" |})
        do! RTL.waitFor (fun () -> Vitest.expect(document.activeElement).toBe beta)

        RTL.fireEvent.click beta
        Vitest.expect(selected.Value).toBe "Beta"
        do! RTL.waitFor (fun () -> Vitest.expect(RTL.screen.queryByRole("menu").IsNone).toBe true)
    }
)

Vitest.test (
    "an open menu can shrink, become empty, and grow again",
    fun () -> promise {
        let selected = ref "none"

        let initialItems = [
            item "Alpha" (Some "Alpha") selected
            ContextMenuItem(isDivider = true)
            item "Beta" None selected
            item "Gamma" (Some "Gamma") selected
        ]

        let items = ref initialItems
        RTL.render (Harness.Menu items) |> ignore
        openAtOwner ()
        let! _ = RTL.screen.findByRole ("button", ByRoleOptions(name = Text "Gamma"))

        // Replace the items while open so React detaches callbacks with obsolete indexes.
        items.Value <- [ item "Delta" None selected ]
        openAtOwner ()
        let! _ = RTL.screen.findByRole ("button", ByRoleOptions(name = Text "Delta"))
        Vitest.expect(RTL.screen.queryByRole("button", ByRoleOptions(name = Text "Gamma")).IsNone).toBe true

        items.Value <- []
        openAtOwner ()
        do! RTL.waitFor (fun () -> Vitest.expect(RTL.screen.queryByRole("menu").IsNone).toBe true)

        items.Value <- initialItems
        openAtOwner ()
        let! gamma = RTL.screen.findByRole ("button", ByRoleOptions(name = Text "Gamma"))
        RTL.fireEvent.click gamma
        Vitest.expect(selected.Value).toBe "Gamma"
        do! RTL.waitFor (fun () -> Vitest.expect(RTL.screen.queryByRole("menu").IsNone).toBe true)
    }
)
