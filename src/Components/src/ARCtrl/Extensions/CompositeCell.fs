[<AutoOpen>]
module ARCtrl.CompositeCellExtensions

open ARCtrl

type CompositeCell with

    /// <summary>
    /// This is an override of an existing ARCtrl version which does not return what i want 😤
    /// </summary>
    member this.GetContentArcitisa() =
        match this with
        | CompositeCell.FreeText s -> [| s |]
        | CompositeCell.Term oa -> [|
            oa.NameText
            defaultArg oa.TermSourceREF ""
            defaultArg oa.TermAccessionNumber ""
          |]
        | CompositeCell.Unitized(v, oa) -> [|
            v
            oa.NameText
            defaultArg oa.TermSourceREF ""
            defaultArg oa.TermAccessionNumber ""
          |]
        | CompositeCell.Data d -> [|
            defaultArg d.FilePath ""
            defaultArg d.Selector ""
            defaultArg d.Format ""
            defaultArg d.SelectorFormat ""
          |]

    //static member fromContent (content: string []) =
    //    match tryFromContent' content with
    //    | Ok r -> r
    //    | Error msg -> raise (exn msg)

    member this.GetEmptyCellFixed() =

        match this with
        | CompositeCell.FreeText _ -> CompositeCell.FreeText ""
        | CompositeCell.Term _ -> CompositeCell.Term(OntologyAnnotation())
        | CompositeCell.Unitized _ -> CompositeCell.Unitized("", OntologyAnnotation())
        | CompositeCell.Data _ -> CompositeCell.Data(Data())

    /// <summary>
    ///
    /// </summary>
    /// <param name="content"></param>
    /// <param name="header"></param>
    static member fromContentValid(content: string[], ?header: CompositeHeader) =
        if header.IsSome then
            let header = header.Value

            let isNumber (input: string) =
                let success, _ = System.Double.TryParse(input)
                success

            match content with
            | arr when arr.Length > 0 && arr.Length < 4 && header.IsTermColumn && isNumber arr.[0] ->
                CompositeCell.createUnitizedFromString (arr.[0]) |> _.ConvertToValidCell(header)
            | [| freetext |] when header.IsSingleColumn -> CompositeCell.createFreeText freetext
            | [| freetext |] -> CompositeCell.createFreeText freetext |> _.ConvertToValidCell(header)
            | [| name; tsr; tan |] when header.IsTermColumn -> CompositeCell.createTermFromString (name, tsr, tan)
            | [| name; tsr; tan |] ->
                CompositeCell.createTermFromString (name, tsr, tan)
                |> _.ConvertToValidCell(header)
            | [| path; selector; format; selectorFormat |] when header.IsDataColumn ->
                let data = Data.empty
                data.FilePath <- Some path
                data.Selector <- Some selector
                data.Format <- Some format
                data.SelectorFormat <- Some selectorFormat
                CompositeCell.createData data
            | [| value; name; tsr; tan |] when header.IsTermColumn ->
                CompositeCell.createUnitizedFromString (value, name, tsr, tan)
            | [| value; name; tsr; tan |] ->
                CompositeCell.createUnitizedFromString (value, name, tsr, tan)
                |> _.ConvertToValidCell(header)
            | anyElse -> failwithf "Invalid content for header: %A" anyElse
        else
            match content with
            | [| freetext |] -> CompositeCell.createFreeText freetext
            | [| name; tsr; tan |] -> CompositeCell.createTermFromString (name, tsr, tan)
            | [| value; name; tsr; tan |] -> CompositeCell.createUnitizedFromString (value, name, tsr, tan)
            | anyElse -> failwithf "Invalid content to parse to CompositeCell: %A" anyElse

    member this.ToTabStr() =
        this.GetContentArcitisa() |> String.concat "\t"

    /// Serialize one displayed grid cell for the clipboard. Data cells keep their
    /// selector in the compact `path#selector` representation used by the table UI.
    member this.ToClipboardStr() =
        match this with
        | CompositeCell.Data data -> data.Name |> Option.defaultValue ""
        | _ -> this.ToTabStr()

    member this.ToVisibleString() =
        match this with
        | CompositeCell.Unitized(value, unit) ->
            [| value; unit.NameText |]
            |> Array.filter (System.String.IsNullOrWhiteSpace >> not)
            |> String.concat " "
        | _ -> this.ToString()

    static member fromTabStr(str: string, header: CompositeHeader) =
        let content = str.Split('\t') |> Array.map _.Trim()
        CompositeCell.fromContentValid (content, header)

    static member getHeaderParsingInfo(headers: CompositeHeader[]) =

        let termIndices, lengthWithoutTerms =
            let termIndices, expectedLength =
                headers
                |> Array.mapi (fun i header ->
                    match header with
                    | item when item.IsSingleColumn -> -1, 1
                    | item when item.IsDataColumn -> -1, 4
                    | item when item.IsTermColumn -> i, 0
                    | anyElse -> failwithf "Error-getHeaderParsingInfo: Encountered unsupported case: %A" anyElse
                )
                |> Array.unzip

            termIndices |> Array.filter (fun item -> item > -1), expectedLength |> Array.sum

        termIndices, lengthWithoutTerms

    static member ToTabTxt(cells: CompositeCell[]) =
        cells
        |> Array.map (fun c -> c.ToTabStr())
        |> String.concat (System.Environment.NewLine)

    static member ToTableTxt(cells: CompositeCell[][]) =
        let rows =
            cells
            |> Array.map (fun row -> row |> Array.map (fun cell -> cell.ToTabStr()) |> String.concat "\t")

        rows |> String.concat (System.Environment.NewLine)

    static member ToClipboardTableTxt(cells: CompositeCell[][]) =
        cells
        |> Array.map (Array.map (fun cell -> CompositeCell.FreeText(cell.ToClipboardStr())))
        |> CompositeCell.ToTableTxt

    static member fromTabTxt (tabTxt: string) (header: CompositeHeader) =
        let lines =
            tabTxt.Split([| System.Environment.NewLine |], System.StringSplitOptions.None)

        let cells = lines |> Array.map (fun line -> CompositeCell.fromTabStr (line, header))
        cells

    member this.ConvertToValidCell(header: CompositeHeader) =
        match this with
        // term header
        | CompositeCell.Term _ when header.IsTermColumn -> this
        | CompositeCell.Unitized _ when header.IsTermColumn -> this
        | CompositeCell.FreeText _ when header.IsTermColumn -> this.ToTermCell()
        | CompositeCell.Data _ when header.IsTermColumn -> this.ToTermCell()
        // data header
        | CompositeCell.Term _ when header.IsDataColumn -> this.ToDataCell()
        | CompositeCell.Unitized _ when header.IsDataColumn -> this.ToDataCell()
        | CompositeCell.FreeText _ when header.IsDataColumn -> this.ToDataCell()
        | CompositeCell.Data _ when header.IsDataColumn -> this
        // freetext header?
        | CompositeCell.Term _ -> this.ToFreeTextCell()
        | CompositeCell.Unitized _ -> this.ToVisibleString() |> CompositeCell.createFreeText
        | CompositeCell.FreeText _ -> this
        | CompositeCell.Data _ -> this.ToFreeTextCell()

    member this.UpdateWithData(d: Data) =
        match this with
        | CompositeCell.Term _ -> CompositeCell.createTerm (OntologyAnnotation.create d.NameText)
        | CompositeCell.Unitized(v, _) -> CompositeCell.createUnitized (v, OntologyAnnotation.create d.NameText)
        | CompositeCell.FreeText _ -> CompositeCell.createFreeText d.NameText
        | CompositeCell.Data _ -> CompositeCell.createData d

    member this.ToOA() =
        match this with
        | CompositeCell.Term oa -> oa
        | CompositeCell.Unitized(v, oa) -> oa
        | CompositeCell.FreeText t -> OntologyAnnotation.create t
        | CompositeCell.Data d -> OntologyAnnotation.create d.NameText

    member this.UpdateMainField(s: string) =
        match this.Copy() with
        | CompositeCell.Term oa ->
            oa.Name <- Some s
            CompositeCell.Term oa
        | CompositeCell.Unitized(_, oa) -> CompositeCell.Unitized(s, oa)
        | CompositeCell.FreeText _ -> CompositeCell.FreeText s
        | CompositeCell.Data d ->
            // Data.Name parses the `path#selector` representation used by DataMaps. Assigning
            // FilePath directly would incorrectly treat the selector as part of the path.
            d.Name <- Some s
            CompositeCell.Data d

    /// <summary>
    /// Will return `this` if executed on Freetext cell.
    /// </summary>
    /// <param name="tsr"></param>
    member this.UpdateTSR(tsr: string) =
        let updateTSR (oa: OntologyAnnotation) =
            let next = oa.Copy()
            next.TermSourceREF <- Some tsr
            next

        match this with
        | CompositeCell.Term oa -> CompositeCell.Term(updateTSR oa)
        | CompositeCell.Unitized(v, oa) -> CompositeCell.Unitized(v, updateTSR oa)
        | _ -> this

    /// <summary>
    /// Will return `this` if executed on Freetext cell.
    /// </summary>
    /// <param name="tsr"></param>
    member this.UpdateTAN(tan: string) =
        let updateTAN (oa: OntologyAnnotation) =
            let next = oa.Copy()
            next.TermSourceREF <- Some tan
            next

        match this with
        | CompositeCell.Term oa -> CompositeCell.Term(updateTAN oa)
        | CompositeCell.Unitized(v, oa) -> CompositeCell.Unitized(v, updateTAN oa)
        | _ -> this
