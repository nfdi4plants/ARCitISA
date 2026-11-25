/// Finds the schema objects behind Ajv errors, for titles, descriptions and property names.
/// Ajv's schemaPath restarts at every $ref'd resource, but verbose errors hand back the original
/// schema objects, so a map from object to resource is enough.
module Swate.Components.Composite.TextEditor.SchemaValidation.SchemaIndex

open Fable.Core

[<AllowNullLiteral>]
type private Url =
    abstract href: string
    abstract hash: string with get, set

module private Js =

    [<Emit("$0 !== null && typeof $0 === 'object'")>]
    let isObject (x: obj) : bool = jsNative

    [<Emit("Object.values($0)")>]
    let values (x: obj) : obj[] = jsNative

    [<Emit("Object.keys($0)")>]
    let keys (x: obj) : string[] = jsNative

    [<Emit("$0[$1]")>]
    let get (x: obj) (key: string) : obj = jsNative

    [<Emit("$0 === undefined")>]
    let isUndefined (x: obj) : bool = jsNative

    /// Resolves ref against baseUri ("" for none). Null when that is not possible, as for a relative ref without a base.
    [<Emit("(() => { try { return $1 ? new URL($0, $1) : new URL($0); } catch { return null; } })()")>]
    let resolveUrl (ref: string) (baseUri: string) : Url = jsNative

    [<Emit("decodeURIComponent($0)")>]
    let decodeUri (s: string) : string = jsNative

/// The schema fields the messages read. Every field is optional.
[<AllowNullLiteral>]
type SchemaNode =
    abstract ``$id``: string option
    abstract ``$ref``: string option
    abstract title: string option
    abstract description: string option
    abstract properties: obj option
    abstract allOf: obj[] option
    abstract ``type``: obj option

type SchemaIndex = {
    /// Follows $ref through registered schemas and JSON pointer fragments. Null when it does not resolve.
    Deref: obj -> obj
    /// The property names a schema object declares, following $ref and allOf one level deep.
    PropertyNames: obj -> string[]
    /// Whether target can be reached from the schema object start, following $refs.
    Reaches: obj -> obj -> bool
}

let private decodeSegment (s: string) =
    Js.decodeUri(s).Replace("~1", "/").Replace("~0", "~")

let private pointer (root: obj) (fragment: string) : obj =
    fragment.TrimStart('/').Split('/')
    |> Array.filter (fun s -> s <> "")
    |> Array.fold
        (fun node seg ->
            if Js.isObject node then
                Js.get node (decodeSegment seg)
            else
                null
        )
        root

let create (schemas: obj[]) : SchemaIndex =
    let byId = JS.Constructors.Map.Create<string, obj>()
    let owner = JS.Constructors.WeakMap.Create<obj, string>()

    // An embedded $id starts a new resource below it.
    let rec walk (baseUri: string) (node: obj) =
        if Js.isObject node && not (owner.has node) then
            let here =
                match (unbox<SchemaNode> node).``$id`` with
                | Some id ->
                    match Js.resolveUrl id baseUri with
                    | null -> baseUri
                    | url ->
                        byId.set (url.href.TrimEnd('#'), node) |> ignore
                        url.href.TrimEnd('#')
                | None -> baseUri

            owner.set (node, here) |> ignore

            for v in Js.values node do
                walk here v

    for schema in schemas do
        walk "" schema

    let rec deref (depth: int) (node: obj) : obj =
        if not (Js.isObject node) || depth > 10 then
            node
        else
            match (unbox<SchemaNode> node).``$ref`` with
            | None -> node
            | Some ref ->
                let baseUri = owner.get node

                match Js.resolveUrl ref (if Js.isUndefined baseUri then "" else baseUri) with
                | null -> null
                | url ->
                    let fragment = url.hash.TrimStart('#')
                    url.hash <- ""
                    let root = byId.get (url.href.TrimEnd('#'))

                    if Js.isUndefined root then
                        null
                    else
                        deref (depth + 1) (if fragment = "" then root else pointer root fragment)

    let deref0 = deref 0

    let ownProperties (node: obj) =
        match deref0 node with
        | null -> [||]
        | n ->
            match (unbox<SchemaNode> n).properties with
            | Some p when Js.isObject p -> Js.keys p
            | _ -> [||]

    let propertyNames (node: obj) =
        let fromAllOf =
            match deref0 node with
            | null -> [||]
            | n ->
                (unbox<SchemaNode> n).allOf
                |> Option.defaultValue [||]
                |> Array.collect ownProperties

        Array.append (ownProperties node) fromAllOf |> Array.distinct

    let reachCache = JS.Constructors.WeakMap.Create<obj, JS.Set<obj>>()

    let reachable (start: obj) : JS.Set<obj> =
        if reachCache.has start then
            reachCache.get start
        else
            let seen = JS.Constructors.Set.Create<obj>()

            let rec visit (n: obj) =
                if Js.isObject n && not (seen.has n) then
                    seen.add n |> ignore

                    match (unbox<SchemaNode> n).``$ref`` with
                    | Some _ -> visit (deref0 n)
                    | None -> ()

                    for v in Js.values n do
                        visit v

            visit start
            reachCache.set (start, seen) |> ignore
            seen

    {
        Deref = deref0
        PropertyNames = propertyNames
        Reaches = fun start target -> Js.isObject start && (reachable start).has target
    }
