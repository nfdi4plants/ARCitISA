/// A cache keyed by object identity. The entries go away with their keys, so it needs no eviction.
module Renderer.ReferenceCache

open Fable.Core

type ReferenceCache<'K, 'V when 'K: not struct and 'V: not struct>() =
    let map = JS.Constructors.WeakMap.Create<'K, 'V>()

    /// The value of the key, or the result of create, which is then kept for that key instance.
    member _.GetOrAdd(key: 'K, create: 'K -> 'V) : 'V =
        if map.has key then
            map.get key
        else
            let value = create key
            map.set (key, value) |> ignore
            value
