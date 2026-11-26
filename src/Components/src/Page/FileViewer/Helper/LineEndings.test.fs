module internal Arcitisa.Components.Page.FileViewer.Helper.LineEndingsTests

open Vitest

Vitest.describe (
    "LineEndings",
    fun () ->
        Vitest.test (
            "mixed line endings get a notice that names the ending a save writes",
            fun () ->
                let crlfFirst = LineEndings.notice "a\r\nb\nc"
                Vitest.expect(crlfFirst.IsSome).toBe true
                Vitest.expect(crlfFirst.Value).toContain "CRLF"

                // The first line break decides, as on save.
                let lfFirst = LineEndings.notice "a\nb\r\n"
                Vitest.expect(lfFirst.IsSome).toBe true
                Vitest.expect(lfFirst.Value).toContain "LF"
                Vitest.expect(lfFirst.Value).not.toContain "CRLF"

                Vitest.expect(LineEndings.notice "a\r\nb\r\n").toEqual None
                Vitest.expect(LineEndings.notice "a\nb\n").toEqual None
                Vitest.expect(LineEndings.notice "one line").toEqual None
        )
)
