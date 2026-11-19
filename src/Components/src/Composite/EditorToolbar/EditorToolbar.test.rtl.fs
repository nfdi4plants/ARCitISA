module internal Swate.Components.Composite.EditorToolbar.EditorToolbarTests

open Browser.Types
open Feliz
open Vitest
open Swate.Components.Composite.TextEditor
open Swate.Components.Util
open Swate.Components.Composite.EditorToolbar

let private history canUndo canRedo = {|
    canUndo = canUndo
    canRedo = canRedo
|}

let private button (name: string) =
    RTL.screen.getByRole ("button", ByRoleOptions(name = Text name)) :?> HTMLButtonElement

let private isMac = Shortcuts.isMac ()

Vitest.afterEach (fun () -> RTL.cleanup ())

Vitest.test (
    "Undo and Redo are disabled while the history has nothing to undo or redo",
    fun () ->
        RTL.render (EditorToolbar.HistoryButtons(history true false, ignore, ignore))
        |> ignore

        Vitest.expect((button "Undo").disabled).toBe false
        Vitest.expect((button "Redo").disabled).toBe true
        // The disabled one still names itself and its key, because only the history keeps it disabled.
        Vitest.expect((button "Redo").title).toBe (Shortcuts.title isMac "Redo" (Shortcuts.redoKey isMac))
)

Vitest.test (
    "a click on Undo or Redo runs onUndo or onRedo",
    fun () ->
        let steps = ResizeArray<string>()

        RTL.render (
            EditorToolbar.HistoryButtons(history true true, (fun () -> steps.Add "undo"), (fun () -> steps.Add "redo"))
        )
        |> ignore

        RTL.fireEvent.click (button "Undo")
        RTL.fireEvent.click (button "Redo")
        RTL.fireEvent.click (button "Undo")

        Vitest.expect(steps.ToArray()).toEqual [| "undo"; "redo"; "undo" |]
)

Vitest.test (
    "disabled history buttons stay disabled with a step to take and show the reason",
    fun () ->
        let steps = ResizeArray<string>()

        RTL.render (
            EditorToolbar.HistoryButtons(
                history true true,
                (fun () -> steps.Add "undo"),
                (fun () -> steps.Add "redo"),
                disabled = true,
                disabledReason = "This file is write-protected."
            )
        )
        |> ignore

        for name in [ "Undo"; "Redo" ] do
            Vitest.expect((button name).disabled).toBe true
            Vitest.expect((button name).title).toBe "This file is write-protected."
            RTL.fireEvent.click (button name)

        Vitest.expect(steps.Count).toBe 0
)

Vitest.test (
    "the toolbar is named and keeps its children in order",
    fun () ->
        RTL.render (
            EditorToolbar.EditorToolbar(
                ariaLabel = "ARC file",
                children =
                    React.Fragment [
                        EditorToolbar.HistoryButtons(history false false, ignore, ignore, key = "history")
                        EditorToolbar.ToolbarButton("Format", "swt:fluent--braces-24-regular", ignore, key = "format")
                    ]
            )
        )
        |> ignore

        let toolbar =
            RTL.screen.getByRole ("toolbar", ByRoleOptions(name = Text "ARC file"))

        Vitest.expect(toolbar.contains (button "Format")).toBe true

        let names =
            RTL.screen.getAllByRole "button"
            |> Seq.map (fun b -> b.getAttribute "aria-label")
            |> Seq.toArray

        Vitest.expect(names).toEqual [| "Undo"; "Redo"; "Format" |]
)

Vitest.test (
    "a ToolbarButton names all its keys in the tooltip and reports its menu",
    fun () ->
        let mutable clicks = 0

        RTL.render (
            EditorToolbar.ToolbarButton(
                "Heading",
                "swt:fluent--text-font-size-24-regular",
                (fun () -> clicks <- clicks + 1),
                shortcuts = [| "Mod-1"; "Mod-2" |],
                menuOpen = false
            )
        )
        |> ignore

        let heading = button "Heading"

        Vitest
            .expect(heading.title)
            .toBe (
                "Heading ("
                + Shortcuts.label isMac "Mod-1"
                + ", "
                + Shortcuts.label isMac "Mod-2"
                + ")"
            )

        Vitest.expect(heading.getAttribute "aria-expanded").toBe "false"
        RTL.fireEvent.click heading
        Vitest.expect(clicks).toBe 1
)

Vitest.test (
    "a pressed ToolbarButton reports its state, a plain one reports none",
    fun () ->
        RTL.render (
            React.Fragment [
                EditorToolbar.ToolbarButton("Panel", "swt:fluent--braces-24-regular", ignore, pressed = true, key = "a")
                EditorToolbar.ToolbarButton("Plain", "swt:fluent--braces-24-regular", ignore, key = "b")
            ]
        )
        |> ignore

        Vitest.expect((button "Panel").getAttribute "aria-pressed").toBe "true"
        Vitest.expect((button "Plain").hasAttribute "aria-pressed").toBe false
)

Vitest.test (
    "Close file comes last, after the trailing elements, and runs onClose",
    fun () ->
        let mutable closed = 0

        RTL.render (
            EditorToolbar.EditorToolbar(
                children = EditorToolbar.HistoryButtons(history true true, ignore, ignore),
                trailing = EditorToolbar.ToolbarButton("Share", "swt:fluent--share-24-regular", ignore),
                onClose = (fun () -> closed <- closed + 1)
            )
        )
        |> ignore

        let names =
            RTL.screen.getAllByRole "button"
            |> Seq.map (fun b -> b.getAttribute "aria-label")
            |> Seq.toArray

        Vitest.expect(names).toEqual [| "Undo"; "Redo"; "Share"; "Close file" |]
        RTL.fireEvent.click (button "Close file")
        Vitest.expect(closed).toBe 1
)

Vitest.test (
    "without onClose the toolbar has no Close file button",
    fun () ->
        RTL.render (
            EditorToolbar.EditorToolbar(children = EditorToolbar.HistoryButtons(history true true, ignore, ignore))
        )
        |> ignore

        Vitest.expect(RTL.screen.queryByRole("button", ByRoleOptions(name = Text "Close file")).IsNone).toBe true
)
