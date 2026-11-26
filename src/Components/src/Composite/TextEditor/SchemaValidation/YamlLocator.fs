/// Maps JSON pointers to source spans in a yaml Document.
module Arcitisa.Components.Composite.TextEditor.SchemaValidation.YamlLocator

open Fable.Core
open Arcitisa.Components.Composite.TextEditor.JsBindings.Yaml
open Arcitisa.Components.Composite.TextEditor.SchemaValidation.Types

[<Emit("String($0)")>]
let private text (x: obj) : string = jsNative

let private keyName (k: YamlNode) : string =
    if isScalar k then
        text (unbox<YamlScalar> k).value
    else
        text k

let private span (n: YamlNode) : Span option =
    if isNull n || isNull (box n.range) then
        None
    else
        Some { From = n.range.[0]; To = n.range.[1] }

let create (doc: YamlDocument) : Locator =
    let maps = JS.Constructors.Map.Create<obj, JS.Map<string, YamlPair>>()

    let pairs (m: YamlMap) =
        if maps.has m then
            maps.get m
        else
            let p = JS.Constructors.Map.Create<string, YamlPair>()

            for pair in m.items do
                p.set (keyName pair.key, pair) |> ignore

            maps.set (m, p) |> ignore
            p

    // A path into an alias lands on the anchored node, so the diagnostic shows at the anchor.
    let resolve (n: YamlNode) =
        if not (isNull n) && isAlias n then
            (unbox<YamlAlias> n).resolve doc
        else
            n

    fun segments ->
        let rec walk (node: YamlNode) (key: YamlNode) depth segs =
            let node = resolve node

            match segs with
            | [] -> node, key, depth
            | seg :: rest when not (isNull node) ->
                if isMap node then
                    let p = pairs (unbox node)

                    if p.has seg then
                        let pair = p.get seg
                        // `key:` with nothing after it has no value node. The key stands in for it.
                        walk pair.value pair.key (depth + 1) rest
                    else
                        node, key, depth
                elif isSeq node then
                    let items = (unbox<YamlSeq> node).items

                    match System.Int32.TryParse seg with
                    | true, i when i >= 0 && i < items.Length -> walk items.[i] null (depth + 1) rest
                    | _ -> node, key, depth
                else
                    node, key, depth
            | _ -> node, key, depth

        let node, key, depth = walk doc.contents null 0 segments
        let isObject = not (isNull node) && isMap node

        {
            Found = depth = List.length segments
            Value = span node |> Option.orElse (span key)
            Key = span key
            Kind =
                if isObject then ObjectNode
                elif not (isNull node) && isSeq node then ArrayNode
                else ScalarNode
            FirstKey =
                if isObject then
                    (unbox<YamlMap> node).items
                    |> Array.tryHead
                    |> Option.bind (fun p -> span p.key)
                else
                    None
            ChildKey =
                fun name ->
                    if isObject then
                        let p = pairs (unbox node)
                        if p.has name then span (p.get name).key else None
                    else
                        None
        }
