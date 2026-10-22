module internal Swate.Components.Composite.GraphCanvas.MinimapTests

open Vitest
open Swate.Components.Composite.GraphCanvas.Types
open Swate.Components.Composite.GraphCanvas.Minimap

// A root of 400 by 300 px that shows a drawing at zoom 1 without any scroll or offset.
let private geometry: ScrollGeometry = {
    ScrollLeft = 0.0
    ScrollTop = 0.0
    ClientWidth = 400.0
    ClientHeight = 300.0
    ScrollWidth = 400.0
    ScrollHeight = 300.0
    OffsetX = 0.0
    OffsetY = 0.0
    Zoom = 1.0
}

Vitest.describe (
    "scale",
    fun () ->
        Vitest.test (
            "fits a landscape layout by its width and a portrait layout by its height",
            fun () ->
                Vitest.expect(scale 1000.0 400.0).toBe 0.2
                Vitest.expect(scale 400.0 1000.0).toBe 0.14
                Vitest.expect(scale 100.0 100.0).toBe 1.4
        )

        Vitest.test (
            "is zero for a layout without a usable size",
            fun () ->
                Vitest.expect(scale 0.0 400.0).toBe 0.0
                Vitest.expect(scale 400.0 -5.0).toBe 0.0
                Vitest.expect(scale nan 400.0).toBe 0.0
                Vitest.expect(scale infinity 400.0).toBe 0.0
        )
)

Vitest.describe (
    "overflows",
    fun () ->
        Vitest.test ("is false when the content fits the root", fun () -> Vitest.expect(overflows geometry).toBe false)

        Vitest.test (
            "is true when the content is wider or taller than the root",
            fun () ->
                Vitest.expect(overflows { geometry with ScrollWidth = 401.0 }).toBe true
                Vitest.expect(overflows { geometry with ScrollHeight = 301.0 }).toBe true
        )

        Vitest.test (
            "is false while the root has no size",
            fun () ->
                let unmeasured = {
                    geometry with
                        ClientWidth = 0.0
                        ClientHeight = 0.0
                        ScrollWidth = 900.0
                        ScrollHeight = 900.0
                }

                Vitest.expect(overflows unmeasured).toBe false
        )
)

Vitest.describe (
    "viewport",
    fun () ->
        Vitest.test (
            "is the whole layout when the drawing is centered in a larger root",
            fun () ->
                let centered = {
                    geometry with
                        OffsetX = 100.0
                        OffsetY = 100.0
                }

                Vitest
                    .expect(viewport centered 200.0 100.0)
                    .toEqual (
                        Some {
                            Left = 0.0
                            Top = 0.0
                            Right = 200.0
                            Bottom = 100.0
                        }
                    )
        )

        Vitest.test (
            "is the visible half of a zoomed drawing, in layout units",
            fun () ->
                let scrolled = {
                    geometry with
                        ScrollLeft = 800.0
                        ScrollTop = 350.0
                        ScrollWidth = 2000.0
                        ScrollHeight = 1000.0
                        Zoom = 2.0
                }

                Vitest
                    .expect(viewport scrolled 1000.0 500.0)
                    .toEqual (
                        Some {
                            Left = 400.0
                            Top = 175.0
                            Right = 600.0
                            Bottom = 325.0
                        }
                    )
        )

        Vitest.test (
            "ends at the layout when the visible area reaches past it",
            fun () ->
                let scrolled = {
                    geometry with
                        ScrollLeft = 800.0
                        ScrollTop = 400.0
                        ScrollWidth = 1200.0
                        ScrollHeight = 700.0
                }

                Vitest
                    .expect(viewport scrolled 1000.0 500.0)
                    .toEqual (
                        Some {
                            Left = 800.0
                            Top = 400.0
                            Right = 1000.0
                            Bottom = 500.0
                        }
                    )
        )

        Vitest.test (
            "is None without a usable zoom, root size or layout size",
            fun () ->
                Vitest.expect((viewport { geometry with Zoom = 0.0 } 1000.0 500.0).IsNone).toBe true
                Vitest.expect((viewport { geometry with ClientWidth = 0.0 } 1000.0 500.0).IsNone).toBe true
                Vitest.expect((viewport geometry 0.0 500.0).IsNone).toBe true
        )
)

Vitest.describe (
    "centerOn",
    fun () ->
        let large = {
            geometry with
                ScrollWidth = 1000.0
                ScrollHeight = 500.0
        }

        Vitest.test (
            "stops at the start of the scroll range",
            fun () -> Vitest.expect(centerOn large 10.0 20.0).toEqual ((0.0, 0.0))
        )

        Vitest.test (
            "stops at the end of the scroll range",
            fun () -> Vitest.expect(centerOn large 990.0 490.0).toEqual ((600.0, 200.0))
        )

        Vitest.test (
            "puts the point in the middle of the root, with the offset and the zoom",
            fun () ->
                let zoomed = {
                    large with
                        ScrollWidth = 2100.0
                        ScrollHeight = 1100.0
                        OffsetX = 50.0
                        OffsetY = 40.0
                        Zoom = 2.0
                }

                Vitest.expect(centerOn zoomed 500.0 250.0).toEqual ((850.0, 390.0))
        )

        Vitest.test (
            "stays at zero when the content fits the root",
            fun () -> Vitest.expect(centerOn geometry 200.0 150.0).toEqual ((0.0, 0.0))
        )
)
