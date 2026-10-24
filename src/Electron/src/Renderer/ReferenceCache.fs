/// A cache keyed by object identity. The entries go away with their keys, so it needs no eviction.
module Renderer.ReferenceCache

open Fable.Core
open Fable.Core.JsInterop

[<Emit("new WeakMap()")>]
let private createWeakMap () : obj = jsNative

[<Emit("$0.get($1)")>]
let private weakMapGet (map: obj) (key: obj) : obj = jsNative

[<Emit("$0.set($1, $2)")>]
let private weakMapSet (map: obj) (key: obj) (value: obj) : unit = jsNative

type ReferenceCache<'K, 'V when 'K: not struct and 'V: not struct>() =
    let map = createWeakMap ()

    /// The value of the key, or the result of create, which is then kept for that key instance.
    member _.GetOrAdd(key: 'K, create: 'K -> 'V) : 'V =
        let found = weakMapGet map (box key)

        if isNullOrUndefined found then
            let value = create key
            weakMapSet map (box key) (box value)
            value
        else
            unbox<'V> found
