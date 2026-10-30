module Main.Bindings.Sharp

open Fable.Core

type SharpImage =
    abstract metadata: unit -> JS.Promise<obj>

[<Import("default", "sharp")>]
let sharp (filePath: string) : SharpImage = jsNative

[<Emit("JSON.stringify($0, null, 2)")>]
let stringifyPretty (value: obj) : string = jsNative
