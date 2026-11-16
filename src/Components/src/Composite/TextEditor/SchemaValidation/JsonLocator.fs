/// Maps JSON pointers to source spans in a Lezer JSON tree, the same tree @codemirror/lang-json builds.
module Swate.Components.Composite.TextEditor.SchemaValidation.JsonLocator

open System.Collections.Generic
open Fable.Core
open Swate.Components.Composite.TextEditor.JsBindings.Lezer
open Swate.Components.Composite.TextEditor.SchemaValidation.Types

let private valueNames =
    set [
        "Object"
        "Array"
        "String"
        "Number"
        "True"
        "False"
        "Null"
    ]

let private isValue (n: SyntaxNode) =
    not (isNull n) && valueNames.Contains n.name

[<Emit("(() => { try { return JSON.parse($0); } catch { return null; } })()")>]
let private parseJsonString (raw: string) : string = jsNative

let private unquote (raw: string) : string =
    match parseJsonString raw with
    | null -> raw.Trim('"')
    | s -> s

let private children (n: SyntaxNode) : SyntaxNode list =
    let rec go (c: SyntaxNode) acc =
        if isNull c then
            List.rev acc
        else
            go c.nextSibling (c :: acc)

    go n.firstChild []

let private propertyValue (prop: SyntaxNode) : SyntaxNode =
    children prop |> List.rev |> List.tryFind isValue |> Option.toObj

let private span (n: SyntaxNode) : Span option =
    if isNull n then
        None
    else
        Some { From = n.from; To = n.``to`` }

/// Children of big arrays and objects are cached, so one lint pass walks each container once.
let create (tree: Tree) (text: string) : Locator =
    let arrays = Dictionary<int, SyntaxNode[]>()
    let objects = Dictionary<int, Dictionary<string, SyntaxNode * SyntaxNode>>()

    let arrayItems (node: SyntaxNode) =
        match arrays.TryGetValue node.from with
        | true, items -> items
        | _ ->
            let items = children node |> List.filter isValue |> List.toArray
            arrays.[node.from] <- items
            items

    let objectProps (node: SyntaxNode) =
        match objects.TryGetValue node.from with
        | true, props -> props
        | _ ->
            let props = Dictionary<string, SyntaxNode * SyntaxNode>()

            for c in children node do
                if c.name = "Property" then
                    let nameNode = c.firstChild

                    if not (isNull nameNode) && nameNode.name = "PropertyName" then
                        // The last duplicate wins, like JSON.parse.
                        props.[unquote (text.Substring(nameNode.from, nameNode.``to`` - nameNode.from))] <-
                            (nameNode, propertyValue c)

            objects.[node.from] <- props
            props

    let root = children tree.topNode |> List.tryFind isValue |> Option.toObj

    fun segments ->
        let rec walk (node: SyntaxNode) (key: SyntaxNode) depth segs =
            match segs with
            | [] -> node, key, depth
            | seg :: rest when not (isNull node) ->
                match node.name with
                | "Object" ->
                    match (objectProps node).TryGetValue seg with
                    | true, (k, v) when not (isNull v) -> walk v k (depth + 1) rest
                    | _ -> node, key, depth
                | "Array" ->
                    let items = arrayItems node

                    match System.Int32.TryParse seg with
                    | true, i when i >= 0 && i < items.Length -> walk items.[i] null (depth + 1) rest
                    | _ -> node, key, depth
                | _ -> node, key, depth
            | _ -> node, key, depth

        let node, key, depth = walk root null 0 segments
        let isObject = not (isNull node) && node.name = "Object"

        {
            Found = depth = List.length segments
            Value = span node
            Key = span key
            Kind =
                if isObject then ObjectNode
                elif not (isNull node) && node.name = "Array" then ArrayNode
                else ScalarNode
            FirstKey =
                if isObject then
                    children node
                    |> List.tryFind (fun c -> c.name = "Property")
                    |> Option.bind (fun p -> span p.firstChild)
                else
                    None
            ChildKey =
                fun name ->
                    if isObject then
                        match (objectProps node).TryGetValue name with
                        | true, (k, _) -> span k
                        | _ -> None
                    else
                        None
        }

/// The first error node of the tree, for JSON syntax errors.
let firstSyntaxError (tree: Tree) : Span option =
    let rec find (n: SyntaxNode) : Span option =
        if isNull n then
            None
        elif n.``type``.isError then
            Some {
                From = n.from
                To = max n.``to`` (n.from + 1)
            }
        else
            match find n.firstChild with
            | Some s -> Some s
            | None -> find n.nextSibling

    find tree.topNode
