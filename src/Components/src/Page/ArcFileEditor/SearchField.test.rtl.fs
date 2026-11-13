module internal Swate.Components.Page.ArcFileEditor.SearchFieldTests

open ARCtrl
open Browser.Dom
open Browser.Types
open Fable.Core
open Feliz
open Vitest
open Swate.Components.Shared
open Swate.Components.Page.ArcFileEditor.Types

[<Emit("$0.spyOn(HTMLElement.prototype, $1, 'get').mockReturnValue($2)")>]
let private mockGeometry (vi: Vi) (property: string) (value: int) : unit = jsNative

[<Emit("$0.spyOn(navigator, 'platform', 'get').mockReturnValue($1)")>]
let private mockPlatform (vi: Vi) (platform: string) : unit = jsNative

[<Emit("new KeyboardEvent('keydown', { key: 'f', code: 'KeyF', ctrlKey: $0, metaKey: $1, shiftKey: $2, altKey: $3, bubbles: true, cancelable: true })")>]
let private findEvent (ctrl: bool) (meta: bool) (shift: bool) (alt: bool) : KeyboardEvent = jsNative

let private createArcFile () =
    let assay = ArcAssay.init "Search"
    let table = ArcTable.init "First"

    table.AddColumn(
        CompositeHeader.Input IOType.Source,
        ResizeArray [
            CompositeCell.FreeText "Alpha"
            CompositeCell.FreeText "Beta alpha"
        ]
    )

    let otherTable = ArcTable.init "Second"
    otherTable.AddColumn(CompositeHeader.Input IOType.Source, ResizeArray [ CompositeCell.FreeText "Alpha" ])
    assay.AddTable table
    assay.AddTable otherTable

    assay.Datamap <-
        Some(
            Datamap(
                ResizeArray [
                    DataContext(name = "Alpha.csv")
                    DataContext(name = "Beta.csv")
                ]
            )
        )

    ArcFiles.Assay assay

let private view arcFile =
    Main.ArcFileEditor(arcFile, ignore, (fun () -> promise { return [||] }), startingActiveView = ActiveView.Table 0)

let private renderEditor () =
    mockGeometry Vitest.vi "offsetWidth" 800
    mockGeometry Vitest.vi "offsetHeight" 600
    RTL.render (view (createArcFile ()))

let private searchInput () =
    RTL.screen.getByRole "searchbox" :?> HTMLInputElement

let private status () =
    RTL.screen.getByRole ("status", ByRoleOptions(name = Text "Search results"))

let private search query =
    RTL.fireEvent.change (searchInput (), {| target = {| value = query |} |})

let private press key shiftKey =
    RTL.fireEvent.custom ("keyDown", searchInput (), {| key = key; shiftKey = shiftKey |})

let private selectedCell () =
    document.querySelector "[data-selected='true']"

let private expectSelected column row =
    let selected = selectedCell ()
    Vitest.expect(isNull selected).toBe false
    Vitest.expect(selected.getAttribute "data-column").toBe (string column)
    Vitest.expect(selected.getAttribute "data-row").toBe (string row)

Vitest.afterEach (fun () ->
    RTL.cleanup ()
    Vitest.vi.restoreAllMocks ()
)

for platform, modifier, ctrl, meta in
    [
        "Win32", "Ctrl", true, false
        "MacIntel", "⌘", false, true
    ] do
    Vitest.test (
        $"{modifier}+F focuses search and selects the current query on {platform}",
        fun () ->
            mockPlatform Vitest.vi platform
            renderEditor () |> ignore
            Vitest.expect((RTL.screen.getByText $"{modifier} + F").tagName).toBe "KBD"
            search "alpha"
            let input = searchInput ()
            let event = findEvent ctrl meta false false
            document.body.dispatchEvent event |> ignore
            Vitest.expect(event.defaultPrevented).toBe true
            Vitest.expect(document.activeElement).toBe input
            Vitest.expect(input.selectionStart).toBe 0
            Vitest.expect(input.selectionEnd).toBe 5
    )

    Vitest.test (
        $"the find shortcut on {platform} leaves other modifiers and disabled search alone, and cleans up on unmount",
        fun () ->
            mockPlatform Vitest.vi platform
            let rendered = renderEditor ()

            for event in
                [
                    findEvent meta ctrl false false
                    findEvent ctrl meta true false
                    findEvent ctrl meta false true
                ] do
                document.body.dispatchEvent event |> ignore
                Vitest.expect(event.defaultPrevented).toBe false
                Vitest.expect(document.activeElement = (searchInput () :> Element)).toBe false

            RTL.fireEvent.click (document.querySelector "[data-footertabid='metadata']" :?> HTMLElement)
            let disabledEvent = findEvent ctrl meta false false
            document.body.dispatchEvent disabledEvent |> ignore
            Vitest.expect(disabledEvent.defaultPrevented).toBe false
            rendered.unmount ()
            let unmountedEvent = findEvent ctrl meta false false
            document.body.dispatchEvent unmountedEvent |> ignore
            Vitest.expect(unmountedEvent.defaultPrevented).toBe false
    )

Vitest.test (
    "find shortcut from an active cell editor focuses search and preserves the edited value",
    fun () -> promise {
        mockPlatform Vitest.vi "Win32"
        renderEditor () |> ignore

        let cell () =
            document.querySelector "[data-row='1'][data-column='1'] > div" :?> HTMLElement

        do! RTL.waitFor (fun () -> Vitest.expect(isNull (cell ())).toBe false)
        RTL.fireEvent.custom ("click", cell (), {| detail = 2 |})
        let editor = RTL.screen.getByRole "textbox" :?> HTMLInputElement
        RTL.fireEvent.change (editor, {| target = {| value = "Updated" |} |})
        editor.dispatchEvent (findEvent true false false false) |> ignore
        Vitest.expect(document.activeElement).toBe (searchInput ())
        search "Updated"
        Vitest.expect((status ()).textContent).toBe "0 / 1"
    }
)

Vitest.test (
    "navbar search counts matches and Enter selects, advances and wraps without moving focus",
    fun () -> promise {
        renderEditor () |> ignore
        let input = searchInput ()
        input.focus ()
        search "ALPHA"
        Vitest.expect((status ()).textContent).toBe "0 / 2"
        press "Enter" false
        do! RTL.waitFor (fun () -> expectSelected 1 1)
        Vitest.expect((status ()).textContent).toBe "1 / 2"
        Vitest.expect(document.activeElement).toBe input
        press "Enter" false
        expectSelected 1 2
        Vitest.expect((status ()).textContent).toBe "2 / 2"
        press "Enter" false
        expectSelected 1 1
        press "Enter" true
        expectSelected 1 2
        Vitest.expect((status ()).textContent).toBe "2 / 2"
        Vitest.expect(document.activeElement).toBe input
    }
)

Vitest.test (
    "changing or clearing the query resets navigation and no matches safely ignores Enter",
    fun () -> promise {
        renderEditor () |> ignore
        search "alpha"
        press "Enter" false
        press "Enter" false
        search "beta"
        Vitest.expect((status ()).textContent).toBe "0 / 1"
        press "Enter" false
        do! RTL.waitFor (fun () -> expectSelected 1 2)
        Vitest.expect((status ()).textContent).toBe "1 / 1"
        search "missing"
        press "Enter" false
        Vitest.expect((status ()).textContent).toBe "0 / 0"
        expectSelected 1 2
        press "Escape" false
        Vitest.expect((searchInput ()).value).toBe ""
        Vitest.expect((status ()).textContent).toBe ""
    }
)

Vitest.test (
    "switching tables and DataMap refreshes results and navigation; metadata disables search",
    fun () -> promise {
        renderEditor () |> ignore
        search "alpha"
        press "Enter" false
        RTL.fireEvent.click (RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Second")))
        Vitest.expect((status ()).textContent).toBe "0 / 1"
        press "Enter" false
        do! RTL.waitFor (fun () -> expectSelected 1 1)
        RTL.fireEvent.click (document.querySelector "[data-footertabid='datamap']" :?> HTMLElement)
        let! _ = RTL.screen.findByText ("Alpha.csv", ByTextOptions(exact = true))
        Vitest.expect((status ()).textContent).toBe "0 / 1"
        press "Enter" false
        do! RTL.waitFor (fun () -> expectSelected 1 1)
        Vitest.expect((status ()).textContent).toBe "1 / 1"
        RTL.fireEvent.click (document.querySelector "[data-footertabid='metadata']" :?> HTMLElement)
        Vitest.expect((searchInput ()).disabled).toBe true
        Vitest.expect((status ()).textContent).toBe ""
    }
)

Vitest.test (
    "replacing the ARC file refreshes matches and resets the current result",
    fun () -> promise {
        let rendered = renderEditor ()
        search "alpha"
        press "Enter" false
        Vitest.expect((status ()).textContent).toBe "1 / 2"
        let replacement = createArcFile ()
        replacement.Tables().[0].SetCellAt(0, 0, CompositeCell.FreeText "replacement")
        rendered.rerender (view replacement)
        Vitest.expect((status ()).textContent).toBe "0 / 1"
        press "Enter" false
        do! RTL.waitFor (fun () -> expectSelected 1 2)
        Vitest.expect((status ()).textContent).toBe "1 / 1"
    }
)
