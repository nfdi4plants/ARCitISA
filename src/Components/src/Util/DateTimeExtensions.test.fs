module internal Arcitisa.Components.Tests.DateTimeExtensions

open System
open Vitest
open Arcitisa.Components.DateTimeExtensions

let private now = DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc)

Vitest.describe (
    "timeAgo",
    fun () ->
        Vitest.test (
            "counts seconds below one minute",
            fun () -> Vitest.expect(timeAgo now (now.AddSeconds -45.0)).toBe "45 seconds ago"
        )

        Vitest.test (
            "counts minutes below one hour",
            fun () -> Vitest.expect(timeAgo now (now.AddMinutes -5.0)).toBe "5 minutes ago"
        )

        Vitest.test (
            "counts hours below one day",
            fun () -> Vitest.expect(timeAgo now (now.AddHours -7.0)).toBe "7 hours ago"
        )

        Vitest.test (
            "counts days below thirty days",
            fun () -> Vitest.expect(timeAgo now (now.AddDays -12.0)).toBe "12 days ago"
        )

        Vitest.test (
            "counts months of thirty days below one year",
            fun () -> Vitest.expect(timeAgo now (now.AddDays -95.0)).toBe "3 months ago"
        )

        Vitest.test (
            "counts years of 365 days",
            fun () -> Vitest.expect(timeAgo now (now.AddDays -800.0)).toBe "2 years ago"
        )

        Vitest.test (
            "shows a time ahead of now as zero seconds",
            fun () ->
                Vitest.expect(timeAgo now (now.AddSeconds 90.0)).toBe "0 seconds ago"
                Vitest.expect(timeAgo now (now.AddHours 1.0)).toBe "0 seconds ago"
        )
)
