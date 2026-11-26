module internal Arcitisa.Components.Primitive.ContextMenu.ContextMenuTests

open Browser.Dom
open Fable.Core
open Feliz
open Vitest
open Arcitisa.Components.Primitive.ContextMenu.Types

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

[<Erase; Mangle(false)>]
type private DialogHarness =

    /// A row that opens a rename dialog from its context menu, like a row of the file tree.
    [<ReactComponent>]
    static member RowWithRename() =
        let owner = React.useElementRef ()
        let isOpen, setIsOpen = React.useState false

        Html.div [
            prop.ref owner
            prop.children [
                Html.button [ prop.testId "row"; prop.text "notes.md" ]
                ContextMenu.ContextMenu(
                    (fun _ -> [
                        ContextMenuItem(
                            text = Html.span "Rename",
                            label = "Rename",
                            onClick = (fun _ -> setIsOpen true)
                        )
                    ]),
                    owner
                )
                Arcitisa.Components.Primitive.Dialog.Dialog.StringSubmissionDialog(
                    isOpen = isOpen,
                    title = "Rename",
                    description = "Rename the item.",
                    fieldLabel = "Name",
                    initialValue = "notes.md",
                    close = (fun () -> setIsOpen false),
                    submit = ignore,
                    validate = Ok,
                    submitLabel = "Rename"
                )
            ]
        ]

Vitest.test (
    "a dialog that a menu item opens keeps the focus after the menu closes",
    fun () -> promise {
        RTL.render (DialogHarness.RowWithRename()) |> ignore
        let row = RTL.screen.getByTestId "row"
        row.focus ()

        RTL.fireEvent.custom (
            "contextMenu",
            row,
            {|
                clientX = 40
                clientY = 40
                bubbles = true
            |}
        )

        let! rename = RTL.screen.findByRole ("button", ByRoleOptions(name = Text "Rename"))
        RTL.fireEvent.click rename

        let input = RTL.screen.getByRole "textbox"
        do! RTL.waitFor (fun () -> Vitest.expect(document.activeElement).toBe input)

        // The closed menu returns the focus to the row in a microtask. The dialog must still hold it afterwards.
        do!
            Fable.Core.JS.Constructors.Promise.Create(fun resolve _ ->
                Fable.Core.JS.setTimeout (fun () -> resolve ()) 50 |> ignore
            )

        Vitest.expect(document.activeElement).toBe input
    }
)
