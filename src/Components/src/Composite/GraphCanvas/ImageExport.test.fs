module internal Swate.Components.Composite.GraphCanvas.ImageExportTests

open Vitest
open Swate.Components.Composite.GraphCanvas.ImageExport

Vitest.describe (
    "scale",
    fun () ->
        Vitest.test ("doubles a small graph", fun () -> Vitest.expect(scale 600.0 300.0).toBe 2.0)

        Vitest.test (
            "limits a very wide graph to 16384 px on its long side",
            fun () ->
                let factor = scale 40000.0 100.0
                Vitest.expect(40000.0 * factor).toBeCloseTo 16384.0
                Vitest.expect(factor).toBeLessThan 2.0
        )

        Vitest.test (
            "keeps the factor 2 for a graph that is 8000 px wide",
            fun () -> Vitest.expect(scale 8000.0 100.0).toBe 2.0
        )

        Vitest.test (
            "limits a large square graph by its area",
            fun () ->
                let factor = scale 6000.0 6000.0
                Vitest.expect(6000.0 * factor * 6000.0 * factor).toBeLessThanOrEqual 16777216.5
                // The side limit alone would allow 2, so the area limit is the one at work.
                Vitest.expect(factor).toBeLessThan 0.7
        )

        Vitest.test (
            "is zero for a layout without a usable size",
            fun () ->
                Vitest.expect(scale 0.0 300.0).toBe 0.0
                Vitest.expect(scale 600.0 -1.0).toBe 0.0
                Vitest.expect(scale nan 300.0).toBe 0.0
                Vitest.expect(scale 600.0 infinity).toBe 0.0
        )
)
