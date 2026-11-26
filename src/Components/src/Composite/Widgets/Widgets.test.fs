module internal Arcitisa.Components.Composite.Widgets.WidgetsTests

open Vitest
open Arcitisa.Components.Composite.Widgets.Types

let private host = { X = 800; Y = 500 }
let private element = { X = 300; Y = 200 }

Vitest.test (
    "a position inside the host stays where it is",
    fun () -> Vitest.expect(WidgetPosition.clampIntoHost host element { X = 120; Y = 80 }).toEqual { X = 120; Y = 80 }
)

Vitest.test (
    "a position above or left of the host moves to its edge",
    fun () -> Vitest.expect(WidgetPosition.clampIntoHost host element { X = -40; Y = -25 }).toEqual { X = 0; Y = 0 }
)

Vitest.test (
    "a position right of or below the host leaves the whole widget inside",
    fun () -> Vitest.expect(WidgetPosition.clampIntoHost host element { X = 900; Y = 700 }).toEqual { X = 500; Y = 300 }
)

Vitest.test (
    "a widget larger than the host sits at the top left corner",
    fun () ->
        let tooLarge = { X = 1000; Y = 600 }
        Vitest.expect(WidgetPosition.clampIntoHost host tooLarge { X = 50; Y = 50 }).toEqual { X = 0; Y = 0 }
)
