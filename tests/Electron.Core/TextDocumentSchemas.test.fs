module ElectronCore.TextDocumentSchemasTests

open Swate.Components.Composite.TextEditor.SchemaValidation
open Swate.Electron.Shared.TextDocumentSchemas
open Vitest

let private lint (text: string) =
    Lint.lintYaml (tryFindValidator validationPackagesPath) text

let private validFile =
    "arc_specification: 2.0.0-draft\n"
    + "validation_packages:\n"
    + "  -\n"
    + "    name: invenio\n"
    + "    version: 3.2.0\n"
    + "  -\n"
    + "    name: plant-growth\n"
    + "    version: 1.0.0\n"
    + "  -\n"
    + "    name: ceplas-experimental\n"
    + "    version: 1.0.1\n"

Vitest.describe (
    "TextDocumentSchemas validation_packages.yml",
    fun () ->
        Vitest.test ("accepts a valid file", fun () -> Vitest.expect(lint validFile).toHaveLength 0)

        Vitest.test (
            "accepts the specification key of the specification text and a package without version",
            fun () ->
                Vitest.expect(lint "specification: 3.0.0\nvalidation_packages:\n  - name: invenio\n").toHaveLength 0
        )

        Vitest.test (
            "reports a version that is not MAJOR.MINOR.PATCH on the value",
            fun () ->
                let text = "validation_packages:\n  - name: invenio\n    version: 3.2.0-beta\n"
                let found = lint text
                Vitest.expect(found).toHaveLength 1
                Vitest.expect(found.[0].from).toBe (text.IndexOf "3.2.0-beta")
                Vitest.expect(found.[0].message).toContain "MAJOR.MINOR.PATCH"
        )

        Vitest.test (
            "reports a package without name",
            fun () ->
                let found = lint "validation_packages:\n  - version: 1.0.0\n"
                Vitest.expect(found).toHaveLength 1
                Vitest.expect(found.[0].message).toContain "Missing required property \"name\""
        )

        Vitest.test (
            "suggests the right key for a misspelled package key",
            fun () ->
                let found = lint "validation_packages:\n  - nmae: invenio\n"
                Vitest.expect(found).toHaveLength 1
                Vitest.expect(found.[0].message).toContain "Did you mean \"name\"?"
        )

        Vitest.test (
            "suggests the right key for a misspelled top-level key",
            fun () ->
                let found = lint "validation_package:\n  - name: invenio\n"
                Vitest.expect(found).toHaveLength 1
                Vitest.expect(found.[0].message).toContain "Did you mean \"validation_packages\"?"
        )

        Vitest.test (
            "has a validator only for .arc/validation_packages.yml",
            fun () ->
                Vitest.expect((tryFindValidator ".arc\\validation_packages.yml").IsSome).toBe true
                Vitest.expect((tryFindValidator "assays/validation_packages.yml").IsSome).toBe false
        )
)
