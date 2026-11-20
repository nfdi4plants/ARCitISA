module internal Swate.Components.Composite.TextEditor.SchemaValidation.LintTests

open Fable.Core
open Vitest
open Swate.Components.Composite.TextEditor.Types
open Swate.Components.Composite.TextEditor.SchemaValidation

let private personSchema () =
    JS.JSON.parse
        """{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://example.org/person.schema.json",
  "title": "Person",
  "type": "object",
  "required": ["givenName", "familyName"],
  "properties": {
    "givenName": { "type": "string" },
    "familyName": { "type": "string" },
    "age": { "type": "integer", "minimum": 0 }
  },
  "additionalProperties": false
}"""

let private teamSchema () =
    JS.JSON.parse
        """{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://example.org/team.schema.json",
  "title": "Team",
  "type": "object",
  "required": ["name"],
  "properties": {
    "name": { "type": "string" },
    "status": { "enum": ["active", "paused"] },
    "members": { "type": "array", "items": { "$ref": "person.schema.json" } },
    "lead": { "oneOf": [{ "$ref": "person.schema.json" }, { "type": "string", "title": "a member name" }] },
    "tags": { "type": "array", "contains": { "const": "core", "title": "the tag \"core\"" } }
  }
}"""

let private validator () =
    match Validator.create [| teamSchema (); personSchema () |] "https://example.org/team.schema.json" with
    | Ok v -> v
    | Error message -> failwith message

let private yaml (text: string) = Lint.lintYaml (Some(validator ())) text
let private json (text: string) = Lint.lintJson (Some(validator ())) text

/// The span of the first occurrence of token after the first occurrence of after.
let private spanOf (text: string) (after: string) (token: string) =
    let from = text.IndexOf(token, text.IndexOf after)
    from, from + token.Length

let private expectSpan (text: string) (d: LintDiagnostic) (after: string) (token: string) =
    let from, ``to`` = spanOf text after token
    Vitest.expect(d.from).toBe from
    Vitest.expect(d.``to``).toBe ``to``

Vitest.describe (
    "SchemaValidation Validator",
    fun () ->
        Vitest.test (
            "resolves a $ref to another registered schema",
            fun () -> Vitest.expect(yaml "name: A\nmembers:\n  - givenName: B\n    familyName: C\n").toHaveLength 0
        )

        Vitest.test (
            "reports a $ref that points at no registered schema",
            fun () ->
                match Validator.create [| teamSchema () |] "https://example.org/team.schema.json" with
                | Ok _ -> failwith "expected an error"
                | Error message -> Vitest.expect(message).toContain "person.schema.json"
        )

        Vitest.test (
            "rejects a schema without $id",
            fun () ->
                match Validator.create [| JS.JSON.parse """{ "type": "object" }""" |] "x" with
                | Ok _ -> failwith "expected an error"
                | Error message -> Vitest.expect(message).toContain "$id"
        )

        Vitest.test (
            "collects strict-mode notes as warnings",
            fun () ->
                let schema =
                    JS.JSON.parse """{ "$id": "https://example.org/w.json", "type": "string", "format": "made-up" }"""

                match Validator.create [| schema |] "https://example.org/w.json" with
                | Ok v -> Vitest.expect(v.Warnings.Length).toBeGreaterThan 0
                | Error message -> failwith message
        )
)

Vitest.describe (
    "SchemaValidation Lint",
    fun () ->
        Vitest.test (
            "puts an enum error on the value and suggests the closest value",
            fun () ->
                let text = "name: A\nstatus: activ\n"
                let found = yaml text
                Vitest.expect(found).toHaveLength 1
                expectSpan text found.[0] "status" "activ"
                Vitest.expect(found.[0].message).toContain "Did you mean \"active\"?"
        )

        Vitest.test (
            "merges a missing required key and its misspelled twin into one diagnostic on the key",
            fun () ->
                let text = "name: A\nmembers:\n  - givenName: B\n    familyNmae: C\n"
                let found = yaml text
                Vitest.expect(found).toHaveLength 1
                expectSpan text found.[0] "members" "familyNmae"
                Vitest.expect(found.[0].message).toContain "Did you mean \"familyName\"?"
        )

        Vitest.test (
            "puts a type error in JSON on the value",
            fun () ->
                let text =
                    """{ "name": "A", "members": [{ "givenName": "B", "familyName": "C", "age": "ten" }] }"""

                let found = json text
                Vitest.expect(found).toHaveLength 1
                expectSpan text found.[0] "age" "\"ten\""
                Vitest.expect(found.[0].message).toContain "must be a whole number"
        )

        Vitest.test (
            "puts a missing property of an array item on the first key of that item",
            fun () ->
                let text = """{ "name": "A", "members": [{ "givenName": "B" }] }"""
                let found = json text
                Vitest.expect(found).toHaveLength 1
                expectSpan text found.[0] "members" "\"givenName\""
                Vitest.expect(found.[0].message).toContain "Missing required property \"familyName\" in members[0]"
        )

        Vitest.test (
            "folds oneOf branches that all fail on their type into one message with the branch titles",
            fun () ->
                let found = yaml "name: A\nlead: 42\n"
                Vitest.expect(found).toHaveLength 1
                Vitest.expect(found.[0].message).toContain "must be Person or a member name"
        )

        Vitest.test (
            "keeps only the errors of the oneOf branch with the right shape",
            fun () ->
                let found = yaml "name: A\nlead:\n  givenName: B\n"
                Vitest.expect(found).toHaveLength 1
                Vitest.expect(found.[0].message).toContain "Missing required property \"familyName\""
        )

        Vitest.test (
            "reports a failed contains once, on the key of the list",
            fun () ->
                let text = "name: A\ntags: [x, y, z]\n"
                let found = yaml text
                Vitest.expect(found).toHaveLength 1
                expectSpan text found.[0] "" "tags"
                Vitest.expect(found.[0].message).toContain "must contain the tag \"core\""
        )

        Vitest.test (
            "reports a JSON trailing comma at the comma",
            fun () ->
                let text = "{ \"name\": \"A\", }"
                let found = json text
                Vitest.expect(found).toHaveLength 1
                Vitest.expect(found.[0].from).toBe (text.IndexOf ',')
                Vitest.expect(found.[0].message).toContain "Trailing comma"
                Vitest.expect(found.[0].source).toBe "syntax"
        )

        Vitest.test (
            "reports a duplicate YAML key as a syntax error",
            fun () ->
                let text = "name: A\nname: B\n"
                let found = yaml text
                Vitest.expect(found).toHaveLength 1
                Vitest.expect(found.[0].source).toBe "syntax"
                // The mark covers the whole second key, not only its first letter.
                expectSpan text found.[0] "\n" "name"
        )

        Vitest.test (
            "treats yes as a string in YAML 1.2",
            fun () ->
                let schema =
                    JS.JSON.parse
                        """{ "$id": "https://example.org/b.json", "type": "object", "properties": { "on": { "type": "boolean" } } }"""

                match Validator.create [| schema |] "https://example.org/b.json" with
                | Ok v ->
                    let found = Lint.lintYaml (Some v) "on: yes\n"
                    Vitest.expect(found).toHaveLength 1
                    Vitest.expect(found.[0].message).toContain "must be a boolean, not the string \"yes\""
                | Error message -> failwith message
        )

        Vitest.test (
            "reports only syntax errors without a validator",
            fun () ->
                Vitest.expect(Lint.lintJson None """{ "anything": 1 }""").toHaveLength 0
                Vitest.expect(Lint.lintJson None "{ \"a\": }").toHaveLength 1
        )

        Vitest.test (
            "gives no diagnostics for a valid document",
            fun () -> Vitest.expect(json """{ "name": "A", "status": "active", "tags": ["core"] }""").toHaveLength 0
        )
)
