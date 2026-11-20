module internal Swate.Components.Page.FileViewer.Helper.LineEndingsTests

open Vitest

Vitest.describe (
    "LineEndings",
    fun () ->
        Vitest.test (
            "mixed line endings get a notice that names the ending a save writes",
            fun () ->
                Vitest
                    .expect(LineEndings.notice "a\r\nb\nc")
                    .toEqual (
                        Some
                            "This file mixes line endings. A save writes all of them as CRLF (Windows), so lines that end differently change too."
                    )

                Vitest.expect(LineEndings.notice "a\r\nb\r\n").toEqual None
                Vitest.expect(LineEndings.notice "a\nb\n").toEqual None
                Vitest.expect(LineEndings.notice "one line").toEqual None
        )
)
