/// Ajv errors -> editor diagnostics: drop noise, pick a source span, write a readable message.
/// The noise rules rely on the order of Ajv's errors (allErrors mode), so keep the Lint tests
/// passing when Ajv is updated.
module Arcitisa.Components.Composite.TextEditor.SchemaValidation.Diagnostics

open System.Collections.Generic
open Fable.Core
open Arcitisa.Components.Composite.TextEditor.Types
open Arcitisa.Components.Composite.TextEditor.JsBindings.Ajv
open Arcitisa.Components.Composite.TextEditor.SchemaValidation.Types
open Arcitisa.Components.Composite.TextEditor.SchemaValidation.SchemaIndex

module private Js =

    [<Emit("JSON.stringify($0)")>]
    let stringify (x: obj) : string = jsNative

    [<Emit("Array.isArray($0)")>]
    let isArray (x: obj) : bool = jsNative

    [<Emit("typeof $0")>]
    let typeOf (x: obj) : string = jsNative

    [<Emit("$0 === null")>]
    let isNull (x: obj) : bool = jsNative

    [<Emit("String($0)")>]
    let text (x: obj) : string = jsNative

    [<Emit("($0 !== null && typeof $0 === 'object') ? $0[$1] : undefined")>]
    let get (x: obj) (key: string) : obj = jsNative

    [<Emit("$0 !== null && typeof $0 === 'object' && !Array.isArray($0)")>]
    let isRecord (x: obj) : bool = jsNative

type private IssueKind =
    | Plain
    /// Every oneOf/anyOf branch failed on its type. Holds the branch schemas.
    | Shape of branches: obj[]
    /// An unknown property that looks like a typo of a missing required one.
    | Renamed of found: string * expected: string

/// One error during shaping. Ajv errors are read-only, and shaping adds kinds of its own.
type private Issue = {
    Path: string
    Keyword: string
    Params: ErrorParams
    Message: string
    Data: obj
    Schema: obj
    ParentSchema: obj
    Kind: IssueKind
}

let private ofAjv (e: AjvError) = {
    Path = e.instancePath
    Keyword = e.keyword
    Params = e.``params``
    Message = e.message
    Data = e.data
    Schema = e.schema
    ParentSchema = e.parentSchema
    Kind = Plain
}

let pointerSegments (pointer: string) : string list =
    if pointer = "" then
        []
    else
        pointer.Substring(1).Split('/')
        |> Array.map (fun s -> s.Replace("~1", "/").Replace("~0", "~"))
        |> Array.toList

/// Optimal string alignment distance: edits, plus swapping two neighbours.
let private distance (a: string) (b: string) : int =
    let d =
        Array.init
            (a.Length + 1)
            (fun i ->
                Array.init
                    (b.Length + 1)
                    (fun j ->
                        if i = 0 then j
                        elif j = 0 then i
                        else 0
                    )
            )

    for i in 1 .. a.Length do
        for j in 1 .. b.Length do
            let cost = if a.[i - 1] = b.[j - 1] then 0 else 1
            d.[i].[j] <- min (min (d.[i - 1].[j] + 1) (d.[i].[j - 1] + 1)) (d.[i - 1].[j - 1] + cost)

            if i > 1 && j > 1 && a.[i - 1] = b.[j - 2] && a.[i - 2] = b.[j - 1] then
                d.[i].[j] <- min d.[i].[j] (d.[i - 2].[j - 2] + 1)

    d.[a.Length].[b.Length]

/// The candidate closest to word, if it is within a third of its length (at least one edit).
let closest (word: string) (candidates: string seq) : string option =
    candidates
    |> Seq.map (fun c -> c, distance (word.ToLowerInvariant()) (c.ToLowerInvariant()))
    |> Seq.sortBy snd
    |> Seq.tryHead
    |> Option.filter (fun (c, d) -> d <= max 1 (c.Length / 3))
    |> Option.map fst

let private truncate (n: int) (s: string) =
    if s.Length > n then s.Substring(0, n - 3) + "..." else s

let private describe (v: obj) : string =
    match Js.typeOf v with
    | "undefined" -> "nothing"
    | "string" -> "the string " + Js.stringify (truncate 50 (unbox<string> v))
    | "object" when Js.isNull v -> "null"
    | "object" when Js.isArray v -> "a list"
    | "object" -> "an object"
    | t -> $"the {t} {Js.text v}"

let private quoted (v: obj) : string =
    if Js.typeOf v = "string" then
        Js.stringify (truncate 60 (unbox<string> v))
    else
        Js.stringify v

let private plainValue (v: obj) : string =
    if Js.typeOf v = "string" then
        unbox<string> v
    else
        Js.stringify v

let private typeWord (t: string) =
    match t with
    | "object" -> "an object"
    | "array" -> "a list"
    | "string" -> "a string"
    | "boolean" -> "a boolean"
    | "number" -> "a number"
    | "integer" -> "a whole number"
    | "null" -> "null"
    | other -> other

let private formatWord (f: string) =
    match f with
    | "email" -> "an email address"
    | "uri" -> "an absolute URI"
    | "uri-reference" -> "a URI reference"
    | "date" -> "a date such as 2026-01-17"
    | "date-time" -> "a date-time with time zone such as 2026-01-17T17:57:48Z"
    | "time" -> "a time with time zone such as 17:57:48Z"
    | "duration" -> "an ISO 8601 duration such as P3D"
    | "uuid" -> "a UUID"
    | "hostname" -> "a host name"
    | "ipv4" -> "an IPv4 address"
    | "ipv6" -> "an IPv6 address"
    | other -> $"a valid {other}"

let private unknownName (i: Issue) : string option =
    match i.Keyword with
    | "unevaluatedProperties" -> i.Params.unevaluatedProperty
    | "additionalProperties" -> i.Params.additionalProperty
    | _ -> None

let private isUnder (parent: string) (path: string) = path.StartsWith(parent + "/")

let private popWhile (out: ResizeArray<Issue>) (keep: Issue -> bool) : Issue list =
    let mutable taken = []

    while out.Count > 0 && keep out.[out.Count - 1] do
        taken <- out.[out.Count - 1] :: taken
        out.RemoveAt(out.Count - 1)

    taken

/// The noise rules, in Ajv's error order:
/// 1. `if` errors go, the `then` errors already say what is wrong.
/// 2. A failed `contains` keeps the error of every item it tried. Those sit right before it,
///    under the array, and were raised by schema objects of the contains subschema. They go.
/// 3. oneOf/anyOf: the branch errors sit right before the summary, at its path or below. Branches
///    that failed on `type` at that path had the wrong shape. They go when another branch has
///    specific errors. Otherwise all of them fold into one Shape issue.
/// 4. "missing X" and "unknown Y" at the same path merge when Y looks like a typo of X.
/// 5. Identical issues show once.
let private simplify (index: SchemaIndex) (errors: AjvError[]) : Issue list =
    let out = ResizeArray<Issue>()

    for e in errors do
        let i = ofAjv e

        match i.Keyword with
        | "if" -> ()
        | "contains" ->
            popWhile out (fun prev -> isUnder i.Path prev.Path && index.Reaches i.Schema prev.ParentSchema)
            |> ignore

            out.Add i
        | "oneOf"
        | "anyOf" ->
            let group =
                popWhile out (fun prev -> prev.Path = i.Path || isUnder i.Path prev.Path)

            let shape, specific =
                group |> List.partition (fun g -> g.Keyword = "type" && g.Path = i.Path)

            if not specific.IsEmpty then
                out.AddRange specific
            elif shape.IsEmpty then
                // Nothing failed inside: more than one oneOf branch passed. Ajv's message says so.
                out.Add i
            else
                let branches =
                    if Js.isArray i.Schema then
                        unbox<obj[]> i.Schema |> Array.map index.Deref
                    else
                        [||]

                out.Add {
                    i with
                        Keyword = "shape"
                        Kind = Shape branches
                }
        | _ -> out.Add i

    let issues = out.ToArray()
    let consumed = HashSet<int>()
    let renamedAt = Dictionary<int, Issue>()

    issues
    |> Array.iteri (fun ri r ->
        match r.Keyword, r.Params.missingProperty with
        | "required", Some missing ->
            issues
            |> Array.tryFindIndex (fun o ->
                o.Path = r.Path
                && match unknownName o with
                   | Some n -> (closest n [ missing ]).IsSome
                   | None -> false
            )
            |> Option.filter (fun ti -> not (consumed.Contains ti))
            |> Option.iter (fun ti ->
                let t = issues.[ti]
                consumed.Add ti |> ignore

                renamedAt.[ri] <- {
                    t with
                        Keyword = "renamedRequired"
                        Kind = Renamed(Option.get (unknownName t), missing)
                }
            )
        | _ -> ()
    )

    let seen = HashSet<string>()

    issues
    |> Array.mapi (fun idx i ->
        match renamedAt.TryGetValue idx with
        | true, renamed -> Some renamed
        | _ when consumed.Contains idx -> None
        | _ -> Some i
    )
    |> Array.choose id
    |> Array.filter (fun i ->
        let detail =
            match i.Kind with
            | Plain -> Js.stringify i.Params
            | Shape _ -> ""
            | Renamed(found, expected) -> found + "|" + expected

        seen.Add(i.Path + "|" + i.Keyword + "|" + detail)
    )
    |> Array.toList

let private lineEnd (text: string) (pos: int) =
    let i = text.IndexOf('\n', pos)
    if i < 0 then text.Length else i

/// Where an issue belongs in the source. Property problems go on the key, value problems on the
/// value, and problems of a whole container on the key that holds it (or its first line).
let private rangeFor (locate: Locator) (text: string) (i: Issue) : Span =
    let loc = locate (pointerSegments i.Path)

    let containerStart () =
        loc.Key
        |> Option.orElse loc.FirstKey
        |> Option.orElse (
            loc.Value
            |> Option.map (fun v -> {
                From = v.From
                To = max (v.From + 1) (lineEnd text v.From)
            })
        )

    let picked =
        match i.Kind, i.Keyword with
        | Renamed(found, _), _ -> loc.ChildKey found
        | _, "required" -> containerStart ()
        | _, ("additionalProperties" | "unevaluatedProperties") -> unknownName i |> Option.bind loc.ChildKey
        | _, "dependentRequired" -> i.Params.property |> Option.bind loc.ChildKey
        | _, "propertyNames" -> i.Params.propertyName |> Option.bind loc.ChildKey
        | _ when loc.Kind = ScalarNode -> loc.Value
        | _ -> containerStart ()

    let r =
        picked
        |> Option.orElse loc.Value
        |> Option.defaultValue { From = 0; To = min 1 text.Length }

    if r.To <= r.From then
        {
            r with
                To = min text.Length (r.From + 1)
        }
    else
        r

let private isIndex (s: string) =
    s <> "" && s |> Seq.forall System.Char.IsDigit

/// "contacts[0].email" style path, for messages.
let private pathLabel (segs: string list) : string =
    segs
    |> List.fold
        (fun acc s ->
            if isIndex s then $"{acc}[{s}]"
            elif acc = "" then s
            else $"{acc}.{s}"
        )
        ""

/// The quoted name of the property at the end of the path, with the indices after it.
let private propName (segs: string list) : string =
    let rev = List.rev segs

    match rev |> List.tryFindIndex (fun s -> not (isIndex s)) with
    | Some k ->
        let name = rev.[k]

        let indices =
            rev
            |> List.take k
            |> List.rev
            |> List.map (fun s -> $"[{s}]")
            |> String.concat ""

        $"\"{name}\"{indices}"
    | None when segs.IsEmpty -> "The document"
    | None -> pathLabel segs

/// Where an object sits, for "missing" messages. A JSON-LD node is named by its @type and @id.
let private objectLabel (data: obj) (segs: string list) : string option =
    let target = segs |> List.fold Js.get data

    if Js.isRecord target && not (Js.typeOf (Js.get target "@type") = "undefined") then
        let t = Js.get target "@type"

        let typeText =
            if Js.isArray t then
                (unbox<obj[]> t |> Array.map Js.text |> String.concat "/")
            else
                Js.text t

        let id = Js.get target "@id"

        Some(
            if Js.typeOf id = "undefined" then
                typeText
            else
                $"{typeText} {quoted id}"
        )
    elif segs.IsEmpty then
        None
    else
        Some(pathLabel segs)

let private titleOf (index: SchemaIndex) (schema: obj) : string option =
    match index.Deref schema with
    | null -> None
    | s -> (unbox<SchemaNode> s).title

let private messageFor (data: obj) (index: SchemaIndex) (i: Issue) : string =
    let segs = pointerSegments i.Path
    let name = propName segs
    let p = i.Params

    let inWhere =
        match objectLabel data segs with
        | Some l -> $" in {l}"
        | None -> ""

    let limit () =
        p.limit |> Option.map (fun l -> Js.text l) |> Option.defaultValue "?"

    let orUnknown (x: string option) = Option.defaultValue "?" x

    match i.Kind with
    | Renamed(found, expected) ->
        $"Unknown property \"{found}\". Did you mean \"{expected}\"? \"{expected}\" is required."
    | Shape branches ->
        let words =
            branches
            |> Array.map (fun b ->
                match titleOf index b with
                | Some t -> t
                | None ->
                    match Js.get b "type" with
                    | t when Js.typeOf t = "string" -> typeWord (unbox t)
                    | _ -> "another shape"
            )

        $"""{name} must be {String.concat " or " words}, not {describe i.Data}."""
    | Plain ->
        match i.Keyword with
        | "required" ->
            let missing = orUnknown p.missingProperty
            $"Missing required property \"{missing}\"{inWhere}."
        | "additionalProperties"
        | "unevaluatedProperties" ->
            let bad = unknownName i |> Option.defaultValue "?"

            let forTitle =
                titleOf index i.ParentSchema
                |> Option.map (fun t -> $" for {t}")
                |> Option.defaultValue ""

            match closest bad (index.PropertyNames i.ParentSchema) with
            | Some hint -> $"Unknown property \"{bad}\"{forTitle}. Did you mean \"{hint}\"?"
            | None -> $"Unknown property \"{bad}\"{forTitle}."
        | "type" ->
            let expected = typeWord (orUnknown p.``type``)
            $"{name} must be {expected}, not {describe i.Data}."
        | "enum" ->
            let allowed = p.allowedValues |> Option.defaultValue [||]
            let list = allowed |> Array.map plainValue |> String.concat ", "

            let hint =
                if Js.typeOf i.Data = "string" then
                    closest
                        (unbox<string> i.Data)
                        (allowed
                         |> Array.filter (fun a -> Js.typeOf a = "string")
                         |> Array.map unbox<string>)
                else
                    None

            match hint with
            | Some h -> $"{name} must be one of {list}, not {describe i.Data}. Did you mean \"{h}\"?"
            | None -> $"{name} must be one of {list}, not {describe i.Data}."
        | "const" ->
            let allowed = p.allowedValue |> Option.map quoted |> orUnknown
            $"{name} must be {allowed}, not {describe i.Data}."
        | "format" ->
            let expected = formatWord (orUnknown p.format)
            $"{name} must be {expected}, not {describe i.Data}."
        | "pattern" ->
            let rule =
                match index.Deref i.ParentSchema with
                | null -> None
                | s -> (unbox<SchemaNode> s).description
                |> Option.defaultValue ("must match /" + orUnknown p.pattern + "/")

            $"{name} {rule}, not {describe i.Data}."
        | "minLength" when p.limit = Some 1.0 -> $"{name} must not be empty."
        | "minLength" -> $"{name} must have at least {limit ()} characters."
        | "maxLength" -> $"{name} must have at most {limit ()} characters."
        | "minItems" -> $"""{name} must have at least {limit ()} {if p.limit = Some 1.0 then "item" else "items"}."""
        | "maxItems" -> $"{name} must have at most {limit ()} items."
        | "uniqueItems" ->
            let at (x: int option) =
                x |> Option.map string |> Option.defaultValue "?"
            // Ajv's i is the earlier index of the two.
            $"{name} has the same item twice, at positions {at p.i} and {at p.j}."
        | "minimum"
        | "maximum"
        | "exclusiveMinimum"
        | "exclusiveMaximum" ->
            let words =
                match p.comparison with
                | Some ">=" -> "at least"
                | Some "<=" -> "at most"
                | Some ">" -> "greater than"
                | Some "<" -> "less than"
                | _ -> "within"

            $"{name} must be {words} {limit ()}, not {describe i.Data}."
        | "contains" ->
            let title = titleOf index i.Schema |> Option.defaultValue "a matching item"

            match p.minContains, p.maxContains with
            | Some mn, Some mx when mn = mx && Js.isArray i.Data ->
                let times = if mx = 1.0 then "once" else $"{Js.text mx} times"
                $"{name} must contain {title} exactly {times}."
            | _, Some mx when Js.isArray i.Data -> $"{name} must contain {title} at most {Js.text mx} times."
            | _ -> $"{name} must contain {title}."
        | "dependentRequired" ->
            let property = orUnknown p.property
            let missing = orUnknown p.missingProperty
            $"\"{property}\" needs \"{missing}\" as well{inWhere}."
        | "items" -> $"{name} must not have more than {limit ()} items."
        | "false schema" -> $"{name} is not allowed here."
        | _ -> $"{name} {i.Message}."

let toDiagnostics
    (errors: AjvError[])
    (locate: Locator)
    (text: string)
    (data: obj)
    (index: SchemaIndex)
    : LintDiagnostic[] =
    simplify index errors
    |> List.map (fun i ->
        let r = rangeFor locate text i

        {|
            from = r.From
            ``to`` = r.To
            severity = "error"
            message = messageFor data index i
            source = "schema"
        |}
    )
    |> List.toArray
