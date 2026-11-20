/// Typed bindings for Ajv with JSON Schema 2020-12 (https://ajv.js.org) and ajv-formats.
/// Ajv compiles validators with `new Function`. A Content-Security-Policy without 'unsafe-eval' blocks it.
module Swate.Components.Composite.TextEditor.JsBindings.Ajv

open Fable.Core

/// The params of an error. Which fields are set depends on the keyword.
[<AllowNullLiteral>]
type ErrorParams =
    abstract missingProperty: string option
    abstract additionalProperty: string option
    abstract unevaluatedProperty: string option
    abstract property: string option
    abstract propertyName: string option
    abstract allowedValues: obj[] option
    abstract allowedValue: obj option
    abstract format: string option
    abstract pattern: string option
    abstract limit: float option
    abstract comparison: string option
    abstract ``type``: string option
    abstract minContains: float option
    abstract maxContains: float option
    abstract i: int option
    abstract j: int option

/// With the verbose option, data, schema and parentSchema are the original objects.
[<AllowNullLiteral>]
type AjvError =
    abstract instancePath: string
    abstract schemaPath: string
    abstract keyword: string
    abstract message: string
    abstract ``params``: ErrorParams
    abstract data: obj
    abstract schema: obj
    abstract parentSchema: obj

[<AllowNullLiteral>]
type ValidateFunction =
    [<Emit("$0($1)")>]
    abstract Invoke: data: obj -> bool

    /// Null after a successful validation.
    abstract errors: AjvError[]

type AjvLogger = {|
    log: obj -> unit
    warn: obj -> unit
    error: obj -> unit
|}

/// Compiles to a plain object.
[<Global>]
type AjvOptions
    [<ParamObject; Emit("$0")>]
    (
        ?allErrors: bool,
        ?verbose: bool,
        ?strict: obj,
        ?strictTypes: bool,
        ?strictRequired: bool,
        ?allowUnionTypes: bool,
        ?logger: AjvLogger
    ) =
    member val allErrors: bool option = jsNative with get, set
    member val verbose: bool option = jsNative with get, set
    member val strict: obj option = jsNative with get, set
    member val strictTypes: bool option = jsNative with get, set
    member val strictRequired: bool option = jsNative with get, set
    member val allowUnionTypes: bool option = jsNative with get, set
    member val logger: AjvLogger option = jsNative with get, set

[<AllowNullLiteral>]
type Ajv =
    /// Throws when the schema is not valid against its meta-schema or its $id is taken.
    abstract addSchema: schema: obj -> Ajv
    /// Compiles on first use and throws when a $ref does not resolve. Undefined for an unknown id.
    abstract getSchema: id: string -> ValidateFunction
    /// With true, every value passes the format.
    abstract addFormat: name: string * format: bool -> Ajv

type AjvStatic =
    [<EmitConstructor>]
    abstract Create: AjvOptions -> Ajv

[<Import("default", "ajv/dist/2020.js")>]
let Ajv2020: AjvStatic = jsNative

/// Adds the named formats of ajv-formats.
[<Import("default", "ajv-formats")>]
let addFormats (ajv: Ajv, formats: string[]) : unit = jsNative
