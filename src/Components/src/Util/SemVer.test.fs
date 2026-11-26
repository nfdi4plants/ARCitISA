module internal Arcitisa.Components.Tests.SemVer

open Vitest
open Arcitisa.Components.Util.SemVer
open ARCtrl.Helper.SemVer

Vitest.describe (
    "Release version comparisons",
    fun () ->
        Vitest.test (
            "compares numeric versions and v-prefixed tags",
            fun () ->
                Vitest.expect(SemVer.isOlderVersion "1.9.0" "v1.10.0").toBe true
                Vitest.expect(SemVer.isOlderVersion "2.0.0" "1.10.0").toBe false
        )

        Vitest.test (
            "orders prereleases and ignores build metadata",
            fun () ->
                Vitest.expect(SemVer.isOlderVersion "2.0.0-alpha.9" "2.0.0-alpha.10").toBe true
                Vitest.expect(SemVer.isOlderVersion "2.0.0-rc.1" "2.0.0").toBe true
                Vitest.expect(SemVer.isOlderVersion "2.0.0" "2.0.0-rc.1").toBe false
                Vitest.expect(SemVer.isOlderVersion "2.0.0+one" "2.0.0+two").toBe false
        )

        Vitest.test (
            "does not treat invalid or equal versions as an upgrade",
            fun () ->
                Vitest.expect(SemVer.isOlderVersion "bad" "2.0.0").toBe false
                Vitest.expect(SemVer.isOlderVersion "2.0.0" "bad").toBe false
                Vitest.expect(SemVer.isOlderVersion "2.0.0" "v2.0.0").toBe false
        )
)
