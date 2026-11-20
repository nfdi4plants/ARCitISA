/// A JSON Schema 2020-12 validator over a set of schema objects that reference each other by $id.
/// Ajv never fetches a schema, so every $ref must point at one of the given schemas.
module Swate.Components.Composite.TextEditor.SchemaValidation.Validator

open Fable.Core
open Fable.Core.JsInterop
open Swate.Components.Composite.TextEditor.JsBindings.Ajv
open Swate.Components.Composite.TextEditor.SchemaValidation.SchemaIndex

type SchemaValidator = {
    RootId: string
    /// The $id of every registered schema, in the given order.
    SchemaIds: string[]
    /// Strict-mode notes of Ajv about the schemas (unknown keywords or formats, for example).
    /// They do not stop validation.
    Warnings: string[]
    /// Empty when the data is valid. Errors come in Ajv's order, which Diagnostics relies on.
    Validate: obj -> AjvError[]
    Index: SchemaIndex
}

/// The formats of JSON Schema 2020-12 that ajv-formats implements. Other formats pass with a warning.
let standardFormats = [|
    "date-time"
    "date"
    "time"
    "duration"
    "email"
    "hostname"
    "ipv4"
    "ipv6"
    "uri"
    "uri-reference"
    "uri-template"
    "uuid"
    "json-pointer"
    "relative-json-pointer"
    "regex"
|]

[<Emit("String($0)")>]
let private text (x: obj) : string = jsNative

[<Emit("$0 !== null && typeof $0 === 'object'")>]
let private isObject (x: obj) : bool = jsNative

[<Emit("Object.entries($0)")>]
let private entries (x: obj) : (string * obj)[] = jsNative

/// Every string value of a "format" keyword in the schemas.
let private formatsIn (schemas: obj[]) : string[] =
    let found = ResizeArray<string>()

    let rec walk (node: obj) =
        if isObject node then
            for key, value in entries node do
                if key = "format" && jsTypeof value = "string" then
                    found.Add(unbox value)
                else
                    walk value

    schemas |> Array.iter walk
    found |> Seq.distinct |> Seq.toArray

/// Registers the schemas and compiles the one with rootId. The error is Ajv's message when a schema
/// is not valid, a $ref does not resolve or a schema has no $id.
let create (schemas: obj[]) (rootId: string) : Result<SchemaValidator, string> =
    let warnings = ResizeArray<string>()
    let ids = schemas |> Array.map (fun s -> (unbox<SchemaNode> s).``$id``)

    match ids |> Array.tryFindIndex Option.isNone with
    | Some i -> Error $"Schema {i + 1} has no $id. Every schema needs one so that $refs can find it."
    | None ->
        try
            let ajv =
                Ajv2020.Create(
                    AjvOptions(
                        allErrors = true,
                        verbose = true,
                        strict = box "log",
                        strictTypes = false,
                        strictRequired = false,
                        allowUnionTypes = true,
                        logger = {|
                            log = ignore
                            warn = fun m -> warnings.Add(text m)
                            error = fun m -> warnings.Add(text m)
                        |}
                    )
                )

            addFormats (ajv, standardFormats)

            // Strict mode throws on an unknown format, also in "log" mode. Such formats pass unchecked instead.
            for format in formatsIn schemas do
                if not (Array.contains format standardFormats) then
                    ajv.addFormat (format, true) |> ignore
                    warnings.Add $"The format \"{format}\" is unknown, so its values are not checked."

            for s in schemas do
                ajv.addSchema s |> ignore

            match ajv.getSchema rootId with
            | null -> Error $"No schema has the $id {rootId}."
            | validate ->
                Ok {
                    RootId = rootId
                    SchemaIds = ids |> Array.choose id
                    Warnings = warnings.ToArray()
                    Validate =
                        fun data ->
                            if validate.Invoke data then
                                [||]
                            else
                                Array.copy validate.errors
                    Index = SchemaIndex.create schemas
                }
        with e ->
            Error e.Message
