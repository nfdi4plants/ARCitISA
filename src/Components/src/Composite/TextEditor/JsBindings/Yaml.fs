/// Typed bindings for the document API of the yaml package (https://eemeli.org/yaml).
/// Every node carries range = [start, valueEnd, nodeEnd]. valueEnd leaves out trailing comments.
module Swate.Components.Composite.TextEditor.JsBindings.Yaml

open Fable.Core

/// Any node: Scalar, YAMLMap, YAMLSeq or Alias. Use the is* guards before the casts below.
[<AllowNullLiteral>]
type YamlNode =
    abstract range: int[]

[<AllowNullLiteral>]
type YamlScalar =
    inherit YamlNode
    abstract value: obj

/// A pair has no range of its own. Its value is null for `key:` with nothing after it.
[<AllowNullLiteral>]
type YamlPair =
    abstract key: YamlNode
    abstract value: YamlNode

[<AllowNullLiteral>]
type YamlMap =
    inherit YamlNode
    abstract items: YamlPair[]

[<AllowNullLiteral>]
type YamlSeq =
    inherit YamlNode
    abstract items: YamlNode[]

[<AllowNullLiteral>]
type YamlError =
    abstract message: string
    abstract pos: int[]

[<AllowNullLiteral>]
type YamlDocument =
    abstract contents: YamlNode
    abstract errors: YamlError[]
    abstract toJS: options: {| maxAliasCount: int |} -> obj

[<AllowNullLiteral>]
type YamlAlias =
    inherit YamlNode
    abstract resolve: YamlDocument -> YamlNode

[<Import("parseDocument", "yaml")>]
let parseDocument (source: string, options: {| prettyErrors: bool |}) : YamlDocument = jsNative

[<Import("isMap", "yaml")>]
let isMap (node: obj) : bool = jsNative

[<Import("isSeq", "yaml")>]
let isSeq (node: obj) : bool = jsNative

[<Import("isScalar", "yaml")>]
let isScalar (node: obj) : bool = jsNative

[<Import("isAlias", "yaml")>]
let isAlias (node: obj) : bool = jsNative
