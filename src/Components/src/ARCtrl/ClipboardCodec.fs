module Arcitisa.Components.ClipboardCodec

open ARCtrl
open Browser.Dom
open Fable.Core

// Renamed from "web application/x-swate-cells+json" and "data-swate-cells" in 4.0.0. Cells copied
// between 3.x and 4.x paste as plain text.
[<Literal>]
let MimeType = "web application/x-arcitisa-cells+json"

[<Literal>]
let private CurrentVersion = 1

[<Literal>]
let private HtmlPayloadAttribute = "data-arcitisa-cells"

type CellDto = {
    Kind: string
    Value: string
    Name: string
    TermSourceRef: string
    TermAccessionNumber: string
    Selector: string
    Format: string
    SelectorFormat: string
}

type Payload = { Version: int; Rows: CellDto[][] }

let ofCompositeCell (cell: CompositeCell) =
    match cell with
    | CompositeCell.FreeText value -> {
        Kind = "freetext"
        Value = value
        Name = ""
        TermSourceRef = ""
        TermAccessionNumber = ""
        Selector = ""
        Format = ""
        SelectorFormat = ""
      }
    | CompositeCell.Term term -> {
        Kind = "term"
        Value = ""
        Name = term.NameText
        TermSourceRef = term.TermSourceREF |> Option.defaultValue ""
        TermAccessionNumber = term.TermAccessionNumber |> Option.defaultValue ""
        Selector = ""
        Format = ""
        SelectorFormat = ""
      }
    | CompositeCell.Unitized(value, unit) -> {
        Kind = "unitized"
        Value = value
        Name = unit.NameText
        TermSourceRef = unit.TermSourceREF |> Option.defaultValue ""
        TermAccessionNumber = unit.TermAccessionNumber |> Option.defaultValue ""
        Selector = ""
        Format = ""
        SelectorFormat = ""
      }
    | CompositeCell.Data data -> {
        Kind = "data"
        Value = data.FilePath |> Option.defaultValue ""
        Name = ""
        TermSourceRef = ""
        TermAccessionNumber = ""
        Selector = data.Selector |> Option.defaultValue ""
        Format = data.Format |> Option.defaultValue ""
        SelectorFormat = data.SelectorFormat |> Option.defaultValue ""
      }

let toCompositeCell (cell: CellDto) =
    match cell.Kind with
    | "term" -> CompositeCell.createTermFromString (cell.Name, cell.TermSourceRef, cell.TermAccessionNumber)
    | "unitized" ->
        CompositeCell.createUnitizedFromString (cell.Value, cell.Name, cell.TermSourceRef, cell.TermAccessionNumber)
    | "data" ->
        let data = Data.empty

        data.FilePath <-
            cell.Value
            |> Option.ofObj
            |> Option.filter (System.String.IsNullOrWhiteSpace >> not)

        data.Selector <-
            cell.Selector
            |> Option.ofObj
            |> Option.filter (System.String.IsNullOrWhiteSpace >> not)

        data.Format <-
            cell.Format
            |> Option.ofObj
            |> Option.filter (System.String.IsNullOrWhiteSpace >> not)

        data.SelectorFormat <-
            cell.SelectorFormat
            |> Option.ofObj
            |> Option.filter (System.String.IsNullOrWhiteSpace >> not)

        CompositeCell.createData data
    | "freetext" -> CompositeCell.createFreeText cell.Value
    | kind -> failwith $"Unknown clipboard cell kind: {kind}"

let validateCellDto (cell: CellDto) =
    if isNull (box cell) then
        false
    else
        let requiredFields =
            match cell.Kind |> Option.ofObj with
            | Some "freetext" -> Some [| cell.Value |]
            | Some "term" ->
                Some [|
                    cell.Name
                    cell.TermSourceRef
                    cell.TermAccessionNumber
                |]
            | Some "unitized" ->
                Some [|
                    cell.Value
                    cell.Name
                    cell.TermSourceRef
                    cell.TermAccessionNumber
                |]
            | Some "data" ->
                Some [|
                    cell.Value
                    cell.Selector
                    cell.Format
                    cell.SelectorFormat
                |]
            | _ -> None

        requiredFields
        |> Option.exists (
            Array.forall (fun field -> not (isNull (box field)) && ClipboardBindings.isString (box field))
        )

let private validateRow (row: CellDto[]) =
    not (isNull (box row)) && row.Length > 0 && Array.forall validateCellDto row

let createPayload (cells: CompositeCell[][]) = {
    Version = CurrentVersion
    Rows = cells |> Array.map (Array.map ofCompositeCell)
}

let encode payload = JS.JSON.stringify payload

let tryDecode json =
    try
        let payload = JS.JSON.parse (json) |> unbox<Payload>

        if
            isNull (box payload)
            || payload.Version <> CurrentVersion
            || isNull (box payload.Rows)
            || payload.Rows.Length = 0
            || not (payload.Rows |> Array.forall validateRow)
            || payload.Rows |> Array.exists (fun row -> row.Length <> payload.Rows.[0].Length)
        then
            None
        else
            Some payload
    with _ ->
        None

let private escapeHtml (text: string) =
    text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;")

let private createHtmlTable (plainText: string) (payloadText: string) =
    let encodedPayload = payloadText |> JS.encodeURIComponent

    let tableRows =
        plainText.Split(Arcitisa.Components.ClipboardContract.Contract.LineBreaks, System.StringSplitOptions.None)
        |> Array.map (fun row -> row.Split([| '\t' |], System.StringSplitOptions.None))
        |> Array.map (fun row ->
            row
            |> Array.map (fun value -> $"<td>{value |> escapeHtml}</td>")
            |> String.concat ""
            |> fun columns -> $"<tr>{columns}</tr>"
        )
        |> String.concat ""

    $"<table {HtmlPayloadAttribute}=\"{encodedPayload}\">{tableRows}</table>"

let tryDecodeHtml (htmlText: string) =
    if isNull htmlText then
        None
    else
        try
            htmlText
            |> ClipboardBindings.parseHtml
            |> _.querySelector($"[{HtmlPayloadAttribute}]")
            |> Option.ofObj
            |> Option.bind (fun element -> element.getAttribute HtmlPayloadAttribute |> Option.ofObj)
            |> Option.bind (JS.decodeURIComponent >> tryDecode)
        with _ ->
            None

let private hasType mimeType (item: ClipboardItem) = item.types |> Array.contains mimeType

let private tryReadTypedPayload (item: ClipboardItem) = promise {
    try
        if hasType MimeType item then
            let! blob = item.getType MimeType
            let! json = blob.text ()
            return tryDecode json
        else
            return None
    with _ ->
        return None
}

let private tryReadHtmlPayload (item: ClipboardItem) = promise {
    try
        if hasType "text/html" item then
            let! blob = item.getType "text/html"
            let! htmlText = blob.text ()
            return tryDecodeHtml htmlText
        else
            return None
    with _ ->
        return None
}

let write (plainText: string) (cells: CompositeCell[][] option) = promise {
    match cells with
    | None -> do! Arcitisa.Components.GlobalBindings.navigator.clipboard.writeText plainText
    | Some cells ->
        let clipboard = Arcitisa.Components.GlobalBindings.navigator.clipboard
        let payloadText = cells |> createPayload |> encode
        let htmlText = createHtmlTable plainText payloadText

        try
            do!
                ClipboardBindings.createItemWithTypedContent plainText MimeType payloadText htmlText
                |> Array.singleton
                |> clipboard.write
        with _ ->
            try
                do!
                    ClipboardBindings.createItemWithHtmlContent plainText htmlText
                    |> Array.singleton
                    |> clipboard.write
            with _ ->
                do! clipboard.writeText plainText
}

let private createContent cells plainText : Arcitisa.Components.ClipboardContract.Types.ClipboardContent = {
    Cells = cells
    PlainText = plainText
}

let read () = promise {
    let clipboard = Arcitisa.Components.GlobalBindings.navigator.clipboard

    try
        let! items = clipboard.read ()

        let! payloads =
            items
            |> Array.filter (hasType MimeType)
            |> Array.map tryReadTypedPayload
            |> Promise.all

        let! htmlPayloads =
            items
            |> Array.filter (hasType "text/html")
            |> Array.map tryReadHtmlPayload
            |> Promise.all

        let payload =
            payloads
            |> Array.tryPick id
            |> Option.orElseWith (fun () -> htmlPayloads |> Array.tryPick id)

        let plainItem = items |> Array.tryFind (hasType "text/plain")

        let! plainText =
            match plainItem with
            | Some item -> promise {
                let! blob = item.getType "text/plain"
                return! blob.text ()
              }
            | None -> clipboard.readText ()

        return
            createContent
                (payload
                 |> Option.map (fun value -> value.Rows |> Array.map (Array.map toCompositeCell)))
                plainText
    with _ ->
        let! plainText = clipboard.readText ()

        return createContent None plainText
}
