module internal Arcitisa.Components.Tests.ValidationPackageBrowser.ValidationPackagesYaml

open ARCtrl.ValidationPackages
open ARCtrl.Yaml
open Arcitisa.Components.Page.ValidationPackageBrowser.ValidationPackagesYaml
open Vitest

let private sampleConfig () =
    ValidationPackagesConfig.make
        (ResizeArray [
            ValidationPackage("invenio", ?version = Some "3.2.0")
            ValidationPackage("custom")
        ])
        (Some "2.0.0-draft")

/// The layout ARCitect writes.
let private arcitectLayout =
    "arc_specification: 2.0.0-draft\nvalidation_packages:\n  - name: invenio\n    version: 3.2.0\n  - name: custom"

Vitest.describe (
    "ValidationPackagesYaml.compactSequenceItems",
    fun () ->
        Vitest.test (
            "moves the dash onto the first key line",
            fun () ->
                let input = "validation_packages:\n  -\n    name: invenio\n    version: 3.2.0\n"
                let expected = "validation_packages:\n  - name: invenio\n    version: 3.2.0\n"
                Vitest.expect(compactSequenceItems input).toBe (expected)
        )

        Vitest.test (
            "shifts wider indentation and nested sequences so keys stay aligned",
            fun () ->
                let input =
                    "items:\n    -\n        name: a\n        tags:\n            -\n                x: 1\n"

                let expected = "items:\n    - name: a\n      tags:\n          - x: 1\n"
                Vitest.expect(compactSequenceItems input).toBe (expected)
        )

        Vitest.test (
            "leaves scalar sequences and plain mappings untouched",
            fun () ->
                let input = "tags:\n  - a\n  - b\nname: x\n"
                Vitest.expect(compactSequenceItems input).toBe (input)
        )
)

Vitest.describe (
    "ValidationPackagesYaml.toCompactYamlString",
    fun () ->
        Vitest.test (
            "produces the layout ARCitect writes",
            fun () ->
                let yaml = toCompactYamlString (sampleConfig ())
                Vitest.expect(yaml.TrimEnd()).toBe (arcitectLayout)
        )

        Vitest.test (
            "still parses back with the ARCtrl decoder",
            fun () ->
                let config = sampleConfig ()
                let parsed = ValidationPackagesConfig.fromYamlString (toCompactYamlString config)
                Vitest.expect(parsed.StructurallyEquals config).toBe (true)
        )
)
