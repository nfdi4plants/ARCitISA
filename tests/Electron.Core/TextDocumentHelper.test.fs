module ElectronCore.TextDocumentHelperTests

open Swate.Electron.Shared.TextDocumentHelper
open Vitest

Vitest.describe (
    "TextDocumentHelper",
    fun () ->
        Vitest.test (
            "documentUri escapes each segment and keeps the slashes",
            fun () -> Vitest.expect(documentUri "assays/a 1/#notes.md").toBe "file:///assays/a%201/%23notes.md"
        )

        Vitest.test (
            "documentUri ignores backslashes and leading or trailing slashes",
            fun () -> Vitest.expect(documentUri "\\docs\\README.md/").toBe "file:///docs/README.md"
        )

        Vitest.test (
            "isSameDocumentPath ignores separators and trailing slashes",
            fun () -> Vitest.expect(isSameDocumentPath "docs\\a.md" "docs/a.md/").toBe true
        )

        Vitest.test (
            "isSameDocumentPath tells different files apart",
            fun () -> Vitest.expect(isSameDocumentPath "docs/a.md" "docs/b.md").toBe false
        )

        Vitest.test (
            "isSameDocumentPath tells apart paths that differ only in case",
            fun () -> Vitest.expect(isSameDocumentPath "Docs/A.md" "docs/a.md").toBe false
        )

        Vitest.test (
            "isPathOrAncestor ignores case",
            fun () -> Vitest.expect(isPathOrAncestor "DOCS" "docs/sub/a.md").toBe true
        )

        Vitest.test (
            "isPathOrAncestor accepts the document itself",
            fun () -> Vitest.expect(isPathOrAncestor "docs/a.md" "docs/a.md").toBe true
        )

        Vitest.test (
            "isPathOrAncestor accepts a folder above the document",
            fun () -> Vitest.expect(isPathOrAncestor "docs/" "docs/sub/a.md").toBe true
        )

        Vitest.test (
            "isPathOrAncestor rejects a sibling with a common prefix",
            fun () -> Vitest.expect(isPathOrAncestor "docs" "docs2/a.md").toBe false
        )

        Vitest.test (
            "isPathOrAncestor rejects a path below the document",
            fun () -> Vitest.expect(isPathOrAncestor "docs/a.md/x" "docs/a.md").toBe false
        )
)
