module internal Swate.Components.Composite.Table.KeyboardNavigationTests

open Browser.Types
open Fable.Core
open Vitest
open Swate.Components

let private selectedCells row : GridSelect.GridSelectHandle = {|
    SelectOrigin = Some {| x = 1; y = row |}
    lastAppend = None
    selectedCells =
        Some {|
            xStart = 1
            xEnd = 1
            yStart = row
            yEnd = row
        |}
    contains = fun _ -> true
    selectBy = fun _ -> false
    selectAt = ignore
    clear = ignore
    selectedCellsReducedSet = Set.singleton {| x = 1; y = row |}
    count = 1
|}

let private event code ctrl meta shift alt : KeyboardEvent =
    unbox {|
        code = code
        ctrlKey = ctrl
        metaKey = meta
        shiftKey = shift
        altKey = alt
    |}

Vitest.test (
    "table shortcuts recognize Control and Command chords and preserve native editing",
    fun () ->
        let selection = selectedCells 1

        for ctrl, meta in [ true, false; false, true ] do
            for code, expected in
                [
                    "KeyC", GridSelect.KeyboardShortcut.Copy
                    "KeyX", GridSelect.KeyboardShortcut.Cut
                    "KeyV", GridSelect.KeyboardShortcut.Paste
                    "Enter", GridSelect.KeyboardShortcut.Details
                ] do
                let shortcut = event code ctrl meta false false
                Vitest.expect(GridSelect.tryGetKeyboardShortcut shortcut selection None).toEqual (Some expected)

                Vitest.expect(GridSelect.tryGetKeyboardShortcut shortcut selection (Some {| x = 1; y = 1 |})).toEqual
                    None

        Vitest
            .expect(GridSelect.tryGetKeyboardShortcut (event "F2" false false false false) selection None)
            .toEqual (Some GridSelect.KeyboardShortcut.Details)

        Vitest
            .expect(GridSelect.tryGetKeyboardShortcut (event "Delete" false false false false) selection None)
            .toEqual (Some GridSelect.KeyboardShortcut.Delete)
)

Vitest.test (
    "table shortcuts reject additional modifiers, unmodified letters and header cuts",
    fun () ->
        let selection = selectedCells 1

        for code in [ "KeyC"; "KeyX"; "KeyV"; "Enter"; "Delete"; "F2" ] do
            Vitest.expect(GridSelect.tryGetKeyboardShortcut (event code true false true false) selection None).toEqual
                None

            Vitest.expect(GridSelect.tryGetKeyboardShortcut (event code true false false true) selection None).toEqual
                None

        for code in [ "KeyC"; "KeyX"; "KeyV"; "KeyD"; "KeyE"; "KeyF" ] do
            Vitest.expect(GridSelect.tryGetKeyboardShortcut (event code false false false false) selection None).toEqual
                None

        for ctrl, meta in [ true, false; false, true ] do
            Vitest
                .expect(GridSelect.tryGetKeyboardShortcut (event "KeyX" ctrl meta false false) (selectedCells 0) None)
                .toEqual
                None

        Vitest.expect(GridSelect.tryGetKeyboardShortcut (event "Delete" true false false false) selection None).toEqual
            None

        Vitest
            .expect(
                GridSelect.tryGetKeyboardShortcut
                    (event "KeyC" true false false false)
                    {| selection with count = 0 |}
                    None
            )
            .toEqual
            None
)
