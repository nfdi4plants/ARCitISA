namespace Swate.Components.Composite.AnnotationTable

open Fable.Core
open Feliz
open Browser.Types
open Swate.Components
open ARCtrl
open Swate.Components.Composite.TermSearch
open Swate.Components.Composite.TermSearch.Types
open Swate.Components.Composite.Table.Types
open Swate.Components.Composite.AnnotationTable.Types
open Swate.Components.Shared
open Swate.Components.Primitive.BaseModal

// 👀 this file is work in progress

// module private ArcTypeModalsUtil =
//     let inputKeydownHandler =
//         fun (e: KeyboardEvent) submit cancel ->
//             match e.code with
//             | kbdEventCode.enter ->
//                 if e.ctrlKey || e.metaKey then
//                     e.preventDefault ()
//                     e.stopPropagation ()
//                     submit ()
//             | kbdEventCode.escape ->
//                 e.preventDefault ()
//                 e.stopPropagation ()
//                 cancel ()
//             | _ -> ()

// type FooterButtons =
//     static member Cancel(rmv: unit -> unit) =
//         Html.button [
//             prop.className "swt:btn swt:btn-outline"
//             prop.text "Cancel"
//             prop.onClick (fun _ -> rmv ())
//         ]

//     static member Submit(submitOnClick: unit -> unit) =
//         Html.button [
//             prop.className "swt:btn swt:btn-primary swt:ml-auto"
//             prop.text "Submit"
//             prop.onClick (fun e -> submitOnClick ())
//         ]

// type private InputFields =

//     static member Input(v: string, setter: string -> unit, label: string, rmv, submit, ?autofocus: bool) =
//         let autofocus = defaultArg autofocus false

//         Html.div [
//             prop.className "swt:flex swt:flex-col swt:gap-2 swt:w-full"
//             prop.children [
//                 Html.label [ prop.className "swt:label"; prop.text label ]
//                 Html.input [
//                     prop.className "swt:input swt:w-full"
//                     prop.autoFocus autofocus
//                     prop.valueOrDefault v
//                     prop.onChange (fun (input: string) -> setter input)
//                     prop.onKeyDown (fun (e: KeyboardEvent) -> ArcTypeModalsUtil.inputKeydownHandler e submit rmv)
//                 ]
//             ]
//         ]

//     static member TermCombi
//         (
//             v: Term option,
//             setter: Term option -> unit,
//             label: string,
//             rmv,
//             submit,
//             ?autofocus: bool,
//             ?parent: OntologyAnnotation
//         ) =
//         let autofocus = defaultArg autofocus false

//         let parentId =
//             parent
//             |> Option.bind (fun id -> id.TermAccessionShort |> Option.whereNot System.String.IsNullOrWhiteSpace)

//         Html.div [
//             prop.className "swt:flex swt:flex-col swt:gap-2"
//             prop.children [
//                 Html.label [ prop.className "swt:label"; prop.text label ]
//                 TermSearch.TermSearch(
//                     v,
//                     setter,
//                     classNames = TermSearchStyle(U2.Case1 "swt:border-current"),
//                     autoFocus = autofocus,
//                     onKeyDown = (fun (e: KeyboardEvent) -> ArcTypeModalsUtil.inputKeydownHandler e submit rmv),
//                     ?parentId = parentId
//                 )
//             ]
//         ]

// [<Erase; Mangle(false)>]
// type Modals =
//     [<ReactComponent>]
//     static member DetailsBodyModal
//         (
//             cell: CompositeCell,
//             setCell: CompositeCell -> unit,
//             isOpen: bool,
//             setIsOpen: bool -> unit,
//             ?header: CompositeHeader,
//             ?debug: string
//         ) =
//         let tempCell, setTempCell = React.useState (cell)

//         let submit =
//             fun () ->
//                 if cell <> tempCell then
//                     setCell tempCell

//         let rmv = fun () -> setIsOpen false



//         let Content =
//             match tempCell with
//             | CompositeCell.Term oa ->
//                 let term = oa |> Term.fromOntologyAnnotation
//                 let parent = header |> Option.map (fun h -> h.ToTerm())

//                 React.Fragment [
//                     InputFields.TermCombi(
//                         Some term,
//                         (fun t ->
//                             t
//                             |> Option.defaultValue (Term())
//                             |> Term.toOntologyAnnotation
//                             |> CompositeCell.Term
//                             |> setTempCell
//                         ),
//                         "Term Name",
//                         rmv,
//                         submit,
//                         autofocus = debug.IsNone,
//                         ?parent = parent
//                     )
//                     InputFields.Input(
//                         (oa.TermSourceREF |> Option.defaultValue ""),
//                         (fun input ->
//                             let input = Option.whereNot System.String.IsNullOrWhiteSpace input
//                             let next = oa.Copy()
//                             next.TermSourceREF <- input
//                             CompositeCell.Term next |> setTempCell
//                         ),
//                         "Source",
//                         rmv,
//                         submit
//                     )
//                     InputFields.Input(
//                         (oa.TermAccessionNumber |> Option.defaultValue ""),
//                         (fun input ->
//                             let input = Option.whereNot System.String.IsNullOrWhiteSpace input
//                             let next = oa.Copy()
//                             next.TermAccessionNumber <- input
//                             CompositeCell.Term next |> setTempCell
//                         ),
//                         "Accession Number",
//                         rmv,
//                         submit
//                     )
//                 ]


//         BaseModal.BaseModal(
//             isOpen,
//             setIsOpen,
//             Html.div "Term",
//             // Content,
//             // footer = React.Fragment [ FooterButtons.Cancel(rmv); FooterButtons.Submit(submit) ],
//             ?debug = debug
//         )


open System
open System.Text.RegularExpressions


module private ArcTypeModalsUtil =
    let inputKeydownHandler =
        fun (e: KeyboardEvent) submit cancel ->
            match e.code with
            | kbdEventCode.enter ->
                e.preventDefault ()
                e.stopPropagation ()
                submit ()
            | kbdEventCode.escape ->
                e.preventDefault ()
                e.stopPropagation ()
                cancel ()
            | _ -> ()

type InputField =
    static member Input(v: string, setter: string -> unit, label: string, rmv, submit, ?autofocus: bool) =
        let autofocus = defaultArg autofocus false

        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-2 swt:w-full"
            prop.children [
                Html.label [ prop.className "swt:label"; prop.text label ]
                Html.input [
                    prop.className "swt:input swt:w-full"
                    prop.autoFocus autofocus
                    prop.valueOrDefault v
                    prop.onChange (fun (input: string) -> setter input)
                    prop.onKeyDown (fun (e: KeyboardEvent) -> ArcTypeModalsUtil.inputKeydownHandler e submit rmv)
                ]
            ]
        ]

    static member TermCombi
        (
            v: Term option,
            setter: Term option -> unit,
            label: string,
            rmv,
            submit,
            ?autofocus: bool,
            ?parentOa: OntologyAnnotation
        ) =
        let autofocus = defaultArg autofocus false

        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-2"
            prop.children [
                Html.label [ prop.className "swt:label"; prop.text label ]
                TermSearch.TermSearch(
                    v,
                    setter,
                    classNames = TermSearchStyle(U2.Case1 "swt:border-current swt:w-full"),
                    autoFocus = autofocus,
                    onKeyDown = (fun (e: KeyboardEvent) -> ArcTypeModalsUtil.inputKeydownHandler e submit rmv),
                    ?parentId = (parentOa |> Option.map (fun oa -> oa.TermAccessionShort))
                )
            ]
        ]

type FooterButtons =
    static member Cancel(rmv: unit -> unit) =
        Html.button [
            prop.className "swt:btn swt:btn-outline"
            prop.text "Cancel"
            prop.onClick (fun _ -> rmv ())
        ]

    static member Submit(submitOnClick: unit -> unit) =
        Html.button [
            prop.className "swt:btn swt:btn-primary swt:ml-auto"
            prop.text "Submit"
            prop.onClick (fun e -> submitOnClick ())
        ]


[<Mangle(false); Erase>]
type CompositeCellModal =

    static member TermModal
        (
            oa: OntologyAnnotation,
            rmv,
            ?relevantCompositeHeader: CompositeHeader,
            ?setOa: OntologyAnnotation -> unit,
            ?setHeader: CompositeHeader -> unit,
            ?debug: string
        ) =

        let initTerm = Some(Term.fromOntologyAnnotation oa)
        let tempTerm, setTempTerm = React.useState (initTerm)

        let submit =
            fun () ->
                if tempTerm.IsSome then
                    let tempTerm = tempTerm.Value

                    if setOa.IsSome then
                        tempTerm |> Term.toOntologyAnnotation |> setOa.Value
                        rmv ()
                    elif setHeader.IsSome then
                        let header =
                            match relevantCompositeHeader.Value with
                            | CompositeHeader.Characteristic _ ->
                                CompositeHeader.Characteristic(Term.toOntologyAnnotation tempTerm)
                            | CompositeHeader.Component _ ->
                                CompositeHeader.Component(Term.toOntologyAnnotation tempTerm)
                            | CompositeHeader.Factor _ -> CompositeHeader.Factor(Term.toOntologyAnnotation tempTerm)
                            | CompositeHeader.Parameter _ ->
                                CompositeHeader.Parameter(Term.toOntologyAnnotation tempTerm)
                            | _ -> failwith $"Unknown CompositeHeader type {relevantCompositeHeader.Value.ToString()}"

                        header |> setHeader.Value
                        rmv ()
                    else
                        failwith "At least one set parameter must be set!"

        let body = TermSearch.ModalDetails(tempTerm, setTempTerm, initTerm)

        BaseModal.Modal(
            true,
            (fun _ -> rmv ()),
            Html.div "Term",
            React.Fragment [ body ],
            footer = React.Fragment [ FooterButtons.Cancel(rmv); FooterButtons.Submit(submit) ],
            //contentClassInfo = CompositeCellModal.BaseModalContentClassOverride,
            ?debug = debug
        )

    static member UnitizedModal
        (
            value: string,
            oa: OntologyAnnotation,
            setUnitized: string -> OntologyAnnotation -> unit,
            rmv,
            ?relevantCompositeHeader: CompositeHeader,
            ?debug
        ) =
        let initTerm = Some(Term.fromOntologyAnnotation oa)
        let tempTerm, setTempTerm = React.useState (initTerm)
        let tempValue, setTempValue = React.useState (value)

        let submit =
            fun () ->
                if tempTerm.IsSome then
                    let oa = tempTerm.Value |> Term.toOntologyAnnotation
                    setUnitized tempValue oa
                    rmv ()

        let body =
            TermSearch.ModalDetails(tempTerm, setTempTerm, initTerm, tempValue, setTempValue, value)

        BaseModal.Modal(
            true,
            (fun _ -> rmv ()),
            Html.div "Unitized",
            React.Fragment [ body ],
            footer = React.Fragment [ FooterButtons.Cancel(rmv); FooterButtons.Submit(submit) ],
            //contentClassInfo = CompositeCellModal.BaseModalContentClassOverride,
            ?debug = debug
        )

    static member private submit
        ((setValue: ('a -> unit) option), (setHeader: (CompositeHeader -> unit) option), value, headerValue, rmv)
        =
        try
            if setValue.IsSome then
                setValue.Value value
                rmv ()
            elif setHeader.IsSome then
                let header = headerValue
                header |> setHeader.Value
                rmv ()
            else
                failwith "At least one set parameter must be given!"
        with _ ->
            ()

    static member FreeTextModal
        (value: string, rmv, ?setText: string -> unit, ?setHeader: CompositeHeader -> unit, ?debug)
        =
        let tempValue, setTempValue = React.useState (value)

        let submit =
            fun () ->
                let headerValue = CompositeHeader.OfHeaderString tempValue
                CompositeCellModal.submit (setText, setHeader, tempValue, headerValue, rmv)

        BaseModal.Modal(
            true,
            (fun _ -> rmv ()),
            Html.div "Freetext",
            React.Fragment [
                InputField.Input(
                    tempValue,
                    (fun input -> setTempValue input),
                    "Value",
                    rmv,
                    submit,
                    autofocus = debug.IsNone
                )
            ],
            footer = React.Fragment [ FooterButtons.Cancel(rmv); FooterButtons.Submit(submit) ],
            //contentClassInfo = CompositeCellModal.BaseModalContentClassOverride,
            ?debug = debug
        )

    static member DataModal
        (
            value: ARCtrl.Data,
            rmv,
            ?relevantCompositeHeader: CompositeHeader,
            ?setData: ARCtrl.Data -> unit,
            ?setHeader: CompositeHeader -> unit,
            ?debug
        ) =
        let tempData, setTempData = React.useState (value)

        let submit =
            fun () ->
                try
                    let filePath = defaultArg tempData.FilePath ""
                    let headerValue = CompositeHeader.OfHeaderString filePath
                    CompositeCellModal.submit (setData, setHeader, tempData, headerValue, rmv)
                with _ ->
                    ()

        BaseModal.Modal(
            true,
            (fun _ -> rmv ()),
            Html.div "Data",
            React.Fragment [

                InputField.Input(
                    (tempData.FilePath |> Option.defaultValue ""),
                    (fun input ->
                        tempData.FilePath <- Option.whereNot System.String.IsNullOrWhiteSpace input
                        setTempData tempData
                    ),
                    "File Path",
                    rmv,
                    submit,
                    autofocus = debug.IsNone
                )
                if setData.IsSome then

                    InputField.Input(
                        (tempData.Format |> Option.defaultValue ""),
                        (fun input ->
                            tempData.Format <- Option.whereNot System.String.IsNullOrWhiteSpace input
                            setTempData tempData
                        ),
                        "Data Format",
                        rmv,
                        submit
                    )

                    InputField.Input(
                        (tempData.Selector |> Option.defaultValue ""),
                        (fun input ->
                            tempData.Selector <- Option.whereNot System.String.IsNullOrWhiteSpace input
                            setTempData tempData
                        ),
                        "Selector",
                        rmv,
                        submit
                    )

                    InputField.Input(
                        (tempData.SelectorFormat |> Option.defaultValue ""),
                        (fun input ->
                            tempData.SelectorFormat <- Option.whereNot System.String.IsNullOrWhiteSpace input
                            setTempData tempData
                        ),
                        "Data Selector Format",
                        rmv,
                        submit
                    )
            ],
            footer = React.Fragment [ FooterButtons.Cancel(rmv); FooterButtons.Submit(submit) ],
            //contentClassInfo = CompositeCellModal.BaseModalContentClassOverride,
            ?debug = debug
        )

    [<ReactComponent>]
    static member CompositeCellModal
        (
            compositeCell: CompositeCell,
            setCell: CompositeCell -> unit,
            rmv: unit -> unit,
            ?relevantCompositeHeader: CompositeHeader,
            ?debug: bool
        ) =

        match compositeCell with
        | CompositeCell.Term oa ->
            let setOa = fun oa -> setCell (CompositeCell.Term oa)

            let debug =
                if debug.IsSome && debug.Value then
                    Some "Details_Term"
                else
                    None

            CompositeCellModal.TermModal(
                oa,
                rmv,
                ?relevantCompositeHeader = relevantCompositeHeader,
                setOa = setOa,
                ?debug = debug
            )
        | CompositeCell.Unitized(v, oa) ->
            let setUnitized = fun v oa -> setCell (CompositeCell.Unitized(v, oa))

            let debug =
                if debug.IsSome && debug.Value then
                    Some "Details_Unitized"
                else
                    None

            CompositeCellModal.UnitizedModal(
                v,
                oa,
                setUnitized,
                rmv,
                ?relevantCompositeHeader = relevantCompositeHeader,
                ?debug = debug
            )
        | CompositeCell.FreeText text ->
            let setText = fun text -> setCell (CompositeCell.FreeText text)

            let debug =
                if debug.IsSome && debug.Value then
                    Some "Details_FreeText"
                else
                    None

            CompositeCellModal.FreeTextModal(text, rmv, setText = setText, ?debug = debug)
        | CompositeCell.Data data ->
            let setData = fun data -> setCell (CompositeCell.Data data)

            let debug =
                if debug.IsSome && debug.Value then
                    Some "Details_Data"
                else
                    None

            CompositeCellModal.DataModal(
                data,
                rmv,
                setData = setData,
                ?relevantCompositeHeader = relevantCompositeHeader,
                ?debug = debug
            )

    [<ReactComponent>]
    static member CompositeHeaderModal(header: CompositeHeader, setHeader: CompositeHeader -> unit, rmv: unit -> unit) =
        match header with
        | compositeHeader when compositeHeader.IsTermColumn ->
            let setOa = fun oa -> setHeader oa
            let oa = header.ToTerm()
            CompositeCellModal.TermModal(oa, rmv, setHeader = setOa, relevantCompositeHeader = header)
        | compositeHeader when compositeHeader.IsDataColumn ->
            let data = Data.create (Name = header.ToString())
            CompositeCellModal.DataModal(data, rmv, relevantCompositeHeader = header, setHeader = setHeader)
        | anyElse ->
            let setText = fun v -> setHeader v
            let text = anyElse.ToString()
            CompositeCellModal.FreeTextModal(text, rmv, setHeader = setText)


type ContextMenuModals =
    static member PasteFullColumnsModal
        (
            arcTable: ArcTable,
            setArcTable,
            addColumns:
                {|
                    data: ResizeArray<CompositeColumn>
                    coordinate: CellCoordinate
                    coordinates: CellCoordinate[][]
                |},
            selectHandle: SelectHandle,
            setModal: AnnotationTable.ModalTypes option -> unit,
            tableRef: IRefValue<TableHandle>
        ) =

        let rmv =
            fun _ ->
                tableRef.current.focus ()
                setModal None

        let compositeColumns = addColumns.data |> Array.ofSeq

        let addColumnsBtn compositeColumns columnIndex =
            Html.button [
                prop.className "swt:btn swt:btn-outline swt:btn-primary"
                prop.text "Add columns"
                prop.onClick (fun _ ->
                    arcTable.AddColumns(compositeColumns, columnIndex, false)
                    arcTable.Copy() |> setArcTable
                    rmv ()
                )
            ]

        let pasteColumnsBtn
            (compositeColumns: ResizeArray<CompositeColumn>)
            (coordinate: CellCoordinate)
            (coordinates: CellCoordinate[][])
            =
            Html.button [
                prop.className "swt:btn swt:btn-outline swt:btn-primary"
                prop.text "Paste cells"
                prop.onClick (fun _ ->
                    let pasteColumns = {|
                        data = compositeColumns
                        coordinates = coordinates
                    |}

                    AnnotationTableContextMenuUtil.pasteCells (
                        pasteColumns,
                        coordinate,
                        selectHandle,
                        arcTable,
                        setArcTable
                    )

                    arcTable.Copy() |> setArcTable
                    rmv ()
                )
            ]

        let rows =
            compositeColumns
            |> Array.map (fun compositeColumn -> compositeColumn.Cells |> Array.ofSeq)
            |> Array.transpose

        BaseModal.Modal(
            true,
            (fun _ -> rmv ()),
            Html.div "Headers have been detected",
            React.Fragment [
                Html.div [
                    Html.text "Preview"
                    Html.div [
                        prop.className "swt:overflow-x-auto"
                        prop.children [
                            Html.table [
                                prop.className "swt:table swt:table-xs"
                                prop.children [
                                    Html.thead [
                                        Html.tr (
                                            compositeColumns
                                            |> Array.map (fun compositeColumn ->
                                                Html.th (compositeColumn.Header.ToString())
                                            )
                                        )
                                    ]
                                    Html.tbody (
                                        rows
                                        |> Array.map (fun compositeColumn ->
                                            Html.tr (
                                                compositeColumn |> Array.map (fun cell -> Html.td (cell.ToString()))
                                            )
                                        )
                                    )
                                ]
                            ]
                        ]
                    ]
                ]
            ],
            footer =
                React.Fragment [
                    FooterButtons.Cancel(rmv)
                    addColumnsBtn compositeColumns addColumns.coordinate.x
                    pasteColumnsBtn addColumns.data addColumns.coordinate addColumns.coordinates
                ]
        )

    [<ReactComponent>]
    static member MoveColumnModal
        (
            arcTable: ArcTable,
            setArcTable: ArcTable -> unit,
            arcTableIndex: CellCoordinate,
            uiTableIndex: CellCoordinate,
            setModal: AnnotationTable.ModalTypes option -> unit,
            tableRef: IRefValue<TableHandle>,
            ?debug: bool
        ) =
        let rmv =
            fun _ ->
                tableRef.current.focus ()
                setModal None

        let isInSelected = tableRef.current.SelectHandle.contains (uiTableIndex)

        let columnIndices =
            if isInSelected then
                let range = tableRef.current.SelectHandle.getSelectedCellRange().Value
                [| range.xStart - 1 .. range.xEnd - 1 |]
            else
                [| arcTableIndex.x - 1 |]

        let Subtable = arcTable.Subtable(columnIndices)

        let tempTable =
            React.useRef (
                let table = arcTable.Copy()
                table.RemoveColumns columnIndices
                table
            )

        /// We try to avoid any out of bounds errors by limiting the range of the index
        let MaxIndex = tempTable.current.ColumnCount - 1

        let selectedIndex, setSelectedIndex = React.useState (0)

        let submit =
            fun _ ->
                let table = tempTable.current.Copy()
                table.Join(Subtable, selectedIndex, TableJoinOptions.WithValues)
                setArcTable (table)
                rmv ()

        let modalActivity =
            Html.label [
                prop.className "swt:input swt:w-fit"
                prop.children [
                    Html.input [
                        prop.type'.number
                        prop.onChange (fun (input: int) -> setSelectedIndex (input - 1))
                        prop.defaultValue (selectedIndex + 1)
                        prop.min 1
                        prop.max (MaxIndex + 2)
                    ]
                    Html.span [
                        prop.className "swt:label"
                        prop.textf "%i|%i" (selectedIndex + 1) (MaxIndex + 2)
                    ]
                ]
            ]

        let content =
            let mkRow (index: int, value: string option, isInserted) =
                Html.tr [
                    if isInserted then
                        prop.className "swt:bg-success swt:success-content swt:border-success-content"
                    prop.children [
                        Html.td (if value.IsSome then sprintf "%i" (index + 1) else "")
                        Html.td (value |> Option.defaultValue "")
                    ]
                ]

            React.Fragment [
                Html.table [
                    prop.className "swt:table"
                    prop.children [
                        Html.thead [ Html.tr [ Html.th "Index"; Html.th "Column" ] ]
                        Html.tbody [
                            for i in 0 .. tempTable.current.ColumnCount do // do columncount instead of columncount - 1 to included append option
                                if i = selectedIndex then
                                    for subIndex in 0 .. Subtable.ColumnCount - 1 do
                                        mkRow (i + subIndex, Subtable.Headers.[subIndex].ToString() |> Some, true)

                                if i < tempTable.current.ColumnCount then
                                    let index = if i < selectedIndex then i else i + Subtable.ColumnCount
                                    mkRow (index, tempTable.current.Headers.[i].ToString() |> Some, false)
                        ]
                    ]
                ]
            ]

        let footer =
            Html.div [
                prop.className "swt:justify-end swt:flex swt:gap-2"
                prop.style [ style.marginLeft length.auto ]
                prop.children [
                    Html.button [
                        prop.className "swt:btn swt:btn-outline"
                        prop.text "Cancel"
                        prop.onClick rmv
                    ]
                    Html.a [
                        prop.className "swt:btn swt:btn-primary"
                        prop.text "Submit"
                        prop.onClick submit
                    ]
                ]
            ]

        let debugString =
            if debug.IsSome && debug.Value then
                Some "Move_Column"
            else
                None

        BaseModal.Modal(
            true,
            (fun _ -> rmv ()),
            Html.p (
                if Subtable.ColumnCount > 1 then
                    "Move Columns"
                else
                    "Move Column"
            ),
            content,
            modalActions = modalActivity,
            footer = footer,
            ?debug = debugString
        )

    [<ReactComponent>]
    static member ErrorModal
        (exn: string, setModal: AnnotationTable.ModalTypes option -> unit, tableRef: IRefValue<TableHandle>)
        =

        let rmv =
            fun _ ->
                tableRef.current.focus ()
                setModal None

        BaseModal.ErrorModalObsolete(true, (fun _ -> rmv ()), exn)

    [<ReactComponent>]
    static member UnknownPasteCase
        (
            data: string[][],
            headers: CompositeHeader[],
            setModal: AnnotationTable.ModalTypes option -> unit,
            tableRef: IRefValue<TableHandle>
        ) =

        let rmv =
            fun _ ->
                tableRef.current.focus ()
                setModal None

        let sHeaders = headers |> Array.map (fun header -> header.ToString())

        let msg =
            [|
                $"We cannot determine the paste case for the data in combination with the selected headers."
                $"data: {data}"
                $"headers: {sHeaders}"
                "Please, create an Issue on Github and provide the data and headers!"
            |]
            |> String.concat System.Environment.NewLine

        BaseModal.ErrorModalObsolete(true, (fun _ -> rmv ()), msg)





type TransformConfig =

    static member ConvertCellType
        (tHeaders: ReactElement[], tBody: ReactElement[], targetType: CompositeCellDiscriminate)
        =
        Html.div [
            Html.div [
                prop.children [
                    Html.div [
                        prop.className "swt:flex swt:flex-col swt:gap-2"
                        prop.children [
                            Html.small
                                $"Transform the existing cell type into {targetType} and adapt the values as depicted on submit."
                            Html.div [
                                prop.className "swt:overflow-x-auto swt:border swt:border-base-content/5"
                                prop.children [
                                    Html.table [
                                        prop.className "swt:table swt:table-xs"
                                        prop.children [
                                            Html.thead [ Html.tr (tHeaders) ]
                                            Html.tbody ([ Html.tr (tBody) ])
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]

[<Mangle(false); Erase>]
type CompositeCellEditModal =

    [<ReactComponent>]
    static member TransformTermUnit
        (cell: CompositeCell, header: CompositeHeader, setUnitized: OntologyAnnotation -> unit, rmv)
        =

        let oa = cell.AsTerm
        let term = Term.fromOntologyAnnotation oa

        let submit =
            fun () ->
                term |> Term.toOntologyAnnotation |> setUnitized
                rmv ()

        let termHeader = header.ToTerm()

        let tHeaders = [|
            Html.th (header.ToString())
            Html.th ("Unit")
            Html.th ($"Term Source REF: {termHeader.TermSourceREF}")
            Html.th ($"Term Accession Number {termHeader.TermAccessionNumber}")
        |]

        let tBody = [|
            Html.td ($"{oa.Name}")
            Html.td ($"{oa.Name}")
            Html.td ($"{oa.TermSourceREF}")
            Html.td ($"{oa.TermAccessionNumber}")
        |]

        BaseModal.Modal(
            true,
            (fun _ -> rmv ()),
            Html.div "Term to Unit",
            React.Fragment [
                TransformConfig.ConvertCellType(tHeaders, tBody, CompositeCellDiscriminate.Unitized)
            ],
            footer = React.Fragment [ FooterButtons.Cancel(rmv); FooterButtons.Submit(submit) ]
        //contentClassInfo = CompositeCellEditModal.BaseModalContentClassOverride
        )

    [<ReactComponent>]
    static member UnitToTerm(cell: CompositeCell, header: CompositeHeader, setTerm: OntologyAnnotation -> unit, rmv) =

        let _, oa = cell.AsUnitized
        let term = Term.fromOntologyAnnotation oa

        let submit =
            fun () ->
                term |> Term.toOntologyAnnotation |> setTerm
                rmv ()

        let termHeader = header.ToTerm()

        let tHeaders = [|
            Html.th (header.ToString())
            Html.th ($"Term Source REF: {termHeader.TermSourceREF}")
            Html.th ($"Term Accession Number {termHeader.TermAccessionNumber}")
        |]

        let tBody = [|
            Html.td ($"{oa.Name}")
            Html.td ($"{oa.TermSourceREF}")
            Html.td ($"{oa.TermAccessionNumber}")
        |]

        BaseModal.Modal(
            true,
            (fun _ -> rmv ()),
            Html.div "Unit to Term",
            React.Fragment [
                TransformConfig.ConvertCellType(tHeaders, tBody, CompositeCellDiscriminate.Term)
            ],
            footer = React.Fragment [ FooterButtons.Cancel(rmv); FooterButtons.Submit(submit) ]
        //contentClassInfo = CompositeCellEditModal.BaseModalContentClassOverride
        )

    [<ReactComponent>]
    static member DataToFreeText(cell: CompositeCell, header: CompositeHeader, setText: string -> unit, rmv) =

        let data = cell.AsData
        let text = defaultArg data.Name ""

        let submit =
            fun () ->
                text |> setText
                rmv ()

        let dataHeader = header.TryIOType()

        if dataHeader.IsNone then
            failwith "No data column available!"

        let tHeaders = [| Html.th (header.ToString()) |]
        let tBody = [| Html.td ($"{text}") |]

        BaseModal.Modal(
            true,
            (fun _ -> rmv ()),
            Html.div "Data to Text",
            React.Fragment [
                TransformConfig.ConvertCellType(tHeaders, tBody, CompositeCellDiscriminate.Text)
            ],
            footer = React.Fragment [ FooterButtons.Cancel(rmv); FooterButtons.Submit(submit) ]
        //contentClassInfo = CompositeCellEditModal.BaseModalContentClassOverride
        )

    [<ReactComponent>]
    static member FreeTextToData(cell: CompositeCell, header: CompositeHeader, setData: Data -> unit, rmv) =

        let text = cell.AsFreeText
        let data = Data.create (Name = text)

        let submit =
            fun () ->
                data |> setData
                rmv ()

        let dataHeader = header.TryIOType()

        if dataHeader.IsNone then
            failwith "No data column available!"

        let tHeaders = [|
            Html.th (header.ToString())
            Html.th ("Selector")
            Html.th ("Format")
            Html.th ("Selector Format")
        |]

        let tBody = [|
            Html.td ($"{data.Name}")
            Html.td ($"{data.Selector}")
            Html.td ($"{data.Format}")
            Html.td ($"{data.SelectorFormat}")
        |]

        BaseModal.Modal(
            true,
            (fun _ -> rmv ()),
            Html.div "Text to Data",
            React.Fragment [
                TransformConfig.ConvertCellType(tHeaders, tBody, CompositeCellDiscriminate.Data)
            ],
            footer = React.Fragment [ FooterButtons.Cancel(rmv); FooterButtons.Submit(submit) ]
        //contentClassInfo = CompositeCellEditModal.BaseModalContentClassOverride
        )

    [<ReactComponent>]
    static member CompositeCellTransformModal
        (compositeCell: CompositeCell, header: CompositeHeader, setCell: CompositeCell -> unit, rmv: unit -> unit)
        =

        match compositeCell with
        | CompositeCell.Term _ ->
            let setUnit = fun term -> setCell (CompositeCell.Unitized("", term))
            CompositeCellEditModal.TransformTermUnit(compositeCell, header, setUnit, rmv)
        | CompositeCell.Unitized _ ->
            let setTerm = fun unit -> setCell (CompositeCell.Term unit)
            CompositeCellEditModal.UnitToTerm(compositeCell, header, setTerm, rmv)
        | CompositeCell.Data _ ->
            let setText = fun text -> setCell (CompositeCell.FreeText text)
            CompositeCellEditModal.DataToFreeText(compositeCell, header, setText, rmv)
        | CompositeCell.FreeText _ ->
            if header.IsDataColumn then
                let setData = fun (data: Data) -> setCell (CompositeCell.Data data)
                CompositeCellEditModal.FreeTextToData(compositeCell, header, setData, rmv)
            else
                Html.none



module ComponentHelper =

    let calculateRegex (regex: string) (input: string) =
        try
            let regex = Regex(regex)
            let m = regex.Match(input)

            match m.Success with
            | true -> m.Index, m.Length
            | false -> 0, 0
        with _ ->
            0, 0

    let split (start: int) (length: int) (str: string) =
        let s0, s1 = str |> Seq.toList |> List.splitAt (start)
        let s1, s2 = s1 |> Seq.toList |> List.splitAt (length)
        String.Join("", s0), String.Join("", s1), String.Join("", s2)

    let PreviewRow (index: int, cell0: string, cell: string, markedIndices: int * int) =
        Html.tr [
            Html.td index
            Html.td [
                let s0, marked, s2 = split (fst markedIndices) (snd markedIndices) cell0
                Html.span s0

                Html.mark [
                    prop.className "swt:bg-info swt:text-info-content"
                    prop.text marked
                ]

                Html.span s2
            ]
            Html.td (cell)
        ]

    let PreviewTable (column: CompositeColumn, cellValues: string[], regex) =
        React.Fragment [
            Html.label [ prop.className "swt:label"; prop.text "Preview" ]
            Html.div [
                prop.className "swt:overflow-x-auto swt:grow"
                prop.children [
                    Html.table [
                        prop.className "swt:table"
                        prop.children [
                            Html.thead [
                                Html.tr [ Html.th ""; Html.th "Before"; Html.th "After" ]
                            ]
                            Html.tbody [
                                let previewCount = 5
                                let preview = Array.truncate previewCount cellValues

                                for i in 0 .. (preview.Length - 1) do
                                    let cell0 = column.Cells.[i].ToString()
                                    let cell = preview.[i]
                                    let regexMarkedIndex = calculateRegex regex cell0
                                    PreviewRow(i, cell0, cell, regexMarkedIndex)
                            ]
                        ]
                    ]
                ]
            ]
        ]

type CreateColumnModal =

    [<ReactComponent>]
    static member private CreateForm(cellValues: string[], setPreview) =
        let baseStr, setBaseStr = React.useState ("")
        let suffix, setSuffix = React.useState (false)

        let updateCells (baseStr: string) (suffix: bool) =
            cellValues
            |> Array.mapi (fun i c ->
                match suffix with
                | true -> baseStr + string (i + 1)
                | false -> baseStr
            )
            |> setPreview

        React.Fragment [
            Html.div [
                prop.className "swt:fieldset"
                prop.children [
                    Html.legend [ prop.className "swt:fieldset-legend"; prop.text "Base" ]
                    Html.input [
                        prop.className "swt:input swt:input-xs swt:sm:input-sm swt:md:input-md"
                        prop.autoFocus true
                        prop.valueOrDefault baseStr
                        prop.onChange (fun (ev: Browser.Types.Event) ->
                            let target = ev.target :?> Browser.Types.HTMLInputElement
                            let value = target.value
                            setBaseStr value
                            updateCells value suffix
                        )
                    ]
                    Html.label [
                        prop.className "swt:label swt:cursor-pointer"
                        prop.children [
                            Html.span "Add number suffix"
                            Html.input [
                                prop.type'.checkbox
                                prop.className "swt:checkbox"
                                prop.isChecked suffix
                                prop.onChange (fun (b: bool) ->
                                    setSuffix b
                                    updateCells baseStr b
                                )
                            ]
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member CreateColumnModal(columnIndex: int, arcTable: ArcTable, setColumn, rmv: unit -> unit, ?debug) =

        let column = arcTable.GetColumn(columnIndex)

        let getCellStrings () =
            column.Cells |> Seq.map (fun c -> c.ToString()) |> Array.ofSeq

        let preview, setPreview = React.useState (getCellStrings)

        /// This state is only used for update logic
        let regex, setRegex = React.useState ("")

        let debug = defaultArg debug false

        let submit =
            fun () ->
                preview
                |> Array.map (fun x -> CompositeCell.FreeText x)
                |> fun x -> CompositeColumn.create (column.Header, x |> ResizeArray)
                |> fun column -> setColumn column

        let content = ComponentHelper.PreviewTable(column, preview, regex)

        let footer =
            Html.div [
                if debug then
                    prop.testId "Create Column"
                prop.className "swt:justify-end swt:flex swt:gap-2"
                prop.style [ style.marginLeft length.auto ]
                prop.children [
                    Html.button [
                        prop.className "swt:btn swt:btn-outline"
                        prop.text "Cancel"
                        prop.onClick (fun _ -> rmv ())
                    ]
                    Html.button [
                        prop.className "swt:btn swt:btn-primary"
                        prop.style [ style.marginLeft length.auto ]
                        prop.text "Submit"
                        prop.onClick (fun _ ->
                            submit ()
                            rmv ()
                        )
                    ]
                ]
            ]

        Html.div [
            prop.className "swt:flex swt:flex-col swt:h-full swt:gap-4 swt:min-h-[500px]"
            prop.children [
                Html.div [
                    prop.className "swt:border-b swt:pb-2 swt:mb-2"
                    prop.children [
                        CreateColumnModal.CreateForm(getCellStrings (), setPreview)
                    ]
                ]
                Html.div [
                    prop.className "swt:grow swt:overflow-y-auto swt:h-[200px]"
                    prop.children [ content ]
                ]
                Html.div [
                    prop.className "swt:border-t swt:pt-2 swt:mt-2"
                    prop.children [ footer ]
                ]
            ]
        ]


type State = {
    NextHeaderType: CompositeHeaderDiscriminate
    NextIOType: IOType option
} with

    static member init(current) = {
        NextHeaderType = current
        NextIOType = None
    }

type EditColumnModal =

    static member SelectHeaderTypeOption(headerType: CompositeHeaderDiscriminate) =
        let txt = headerType.ToString()
        Html.option [ prop.value txt; prop.text txt ]

    static member SelectHeaderType(state, setState) =
        Html.select [
            prop.className "swt:select swt:join-item"
            prop.value (state.NextHeaderType.ToString())
            prop.onChange (fun (e: string) ->
                {
                    state with
                        NextHeaderType = CompositeHeaderDiscriminate.fromString e
                }
                |> setState
            )
            prop.children [
                // -- term columns --
                Html.optgroup [
                    prop.label "Term Columns"
                    prop.children [
                        EditColumnModal.SelectHeaderTypeOption CompositeHeaderDiscriminate.Characteristic
                        EditColumnModal.SelectHeaderTypeOption CompositeHeaderDiscriminate.Component
                        EditColumnModal.SelectHeaderTypeOption CompositeHeaderDiscriminate.Factor
                        EditColumnModal.SelectHeaderTypeOption CompositeHeaderDiscriminate.Parameter
                    ]
                ]
                // -- io columns --
                Html.optgroup [
                    prop.label "IO Columns"
                    prop.children [
                        EditColumnModal.SelectHeaderTypeOption CompositeHeaderDiscriminate.Input
                        EditColumnModal.SelectHeaderTypeOption CompositeHeaderDiscriminate.Output
                    ]
                ]
                // -- single columns --
                EditColumnModal.SelectHeaderTypeOption CompositeHeaderDiscriminate.Date
                EditColumnModal.SelectHeaderTypeOption CompositeHeaderDiscriminate.Performer
                EditColumnModal.SelectHeaderTypeOption CompositeHeaderDiscriminate.ProtocolDescription
                EditColumnModal.SelectHeaderTypeOption CompositeHeaderDiscriminate.ProtocolREF
                EditColumnModal.SelectHeaderTypeOption CompositeHeaderDiscriminate.ProtocolType
                EditColumnModal.SelectHeaderTypeOption CompositeHeaderDiscriminate.ProtocolUri
                EditColumnModal.SelectHeaderTypeOption CompositeHeaderDiscriminate.ProtocolVersion
            ]
        ]

    static member SelectIOTypeOption(ioType: IOType) =
        let txt = ioType.ToString()
        Html.option [ prop.value txt; prop.text txt ]

    static member SelectIOType(state, setState) =
        Html.select [
            prop.className "swt:select swt:join-item"
            prop.value (state.NextIOType |> Option.defaultValue IOType.Sample |> _.ToString())
            prop.onChange (fun (e: string) ->
                {
                    state with
                        NextIOType = Some(IOType.ofString e)
                }
                |> setState
            )
            prop.children [
                EditColumnModal.SelectIOTypeOption IOType.Source
                EditColumnModal.SelectIOTypeOption IOType.Sample
                EditColumnModal.SelectIOTypeOption IOType.Material
                EditColumnModal.SelectIOTypeOption IOType.Data
            ]
        ]

    static member Preview(column: CompositeColumn) =
        let parsedStrList =
            ARCtrl.Spreadsheet.CompositeColumn.toStringCellColumns column |> List.transpose

        let headers, body =
            if column.Cells.Count >= 2 then
                parsedStrList.[0], parsedStrList.[1..]
            else
                parsedStrList.[0], []

        Html.div [
            prop.className "swt:overflow-x-auto swt:grow"
            prop.children [
                Html.table [
                    prop.className "swt:table swt:table-sm"
                    prop.children [
                        Html.thead [
                            Html.tr [
                                for header in headers do
                                    Html.th [
                                        prop.className "swt:truncate swt:max-w-16"
                                        prop.text header
                                        prop.title header
                                    ]
                            ]
                        ]
                        Html.tbody [
                            for row in body do
                                Html.tr [
                                    for cell in row do
                                        Html.td [
                                            prop.className "swt:truncate swt:max-w-16"
                                            prop.text cell
                                            prop.title cell
                                        ]
                                ]
                        ]
                    ]
                ]
            ]
        ]

    static member cellsToTermCells(column: CompositeColumn) =
        [|
            for c in column.Cells do
                if c.isUnitized || c.isTerm then c else c.ToTermCell()
        |]
        |> ResizeArray

    static member cellsToFreeText(column) =
        [|
            for c in column.Cells do
                if c.isFreeText then c else c.ToFreeTextCell()
        |]
        |> ResizeArray

    static member cellsToDataOrFreeText(column) =
        [|
            for c in column.Cells do
                if c.isFreeText || c.isData then c else c.ToDataCell()
        |]
        |> ResizeArray

    static member updateColumn(column: CompositeColumn, state) =
        let header = column.Header

        match state.NextHeaderType, state.NextIOType with
        // -- term columns --
        | CompositeHeaderDiscriminate.Characteristic, _ ->
            CompositeColumn.create (
                CompositeHeader.Characteristic(header.ToTerm()),
                EditColumnModal.cellsToTermCells (column)
            )
        | CompositeHeaderDiscriminate.Parameter, _ ->
            CompositeColumn.create (
                CompositeHeader.Parameter(header.ToTerm()),
                EditColumnModal.cellsToTermCells (column)
            )
        | CompositeHeaderDiscriminate.Component, _ ->
            CompositeColumn.create (
                CompositeHeader.Component(header.ToTerm()),
                EditColumnModal.cellsToTermCells (column)
            )
        | CompositeHeaderDiscriminate.Factor, _ ->
            CompositeColumn.create (CompositeHeader.Factor(header.ToTerm()), EditColumnModal.cellsToTermCells (column))
        // -- input columns --
        | CompositeHeaderDiscriminate.Input, Some IOType.Data ->
            CompositeColumn.create (CompositeHeader.Input IOType.Data, EditColumnModal.cellsToDataOrFreeText (column))
        | CompositeHeaderDiscriminate.Input, Some io ->
            CompositeColumn.create (CompositeHeader.Input io, EditColumnModal.cellsToFreeText (column))
        | CompositeHeaderDiscriminate.Input, None ->
            CompositeColumn.create (CompositeHeader.Input IOType.Sample, EditColumnModal.cellsToFreeText (column))
        // -- output columns --
        | CompositeHeaderDiscriminate.Output, Some IOType.Data ->
            CompositeColumn.create (CompositeHeader.Output IOType.Data, EditColumnModal.cellsToDataOrFreeText (column))
        | CompositeHeaderDiscriminate.Output, Some io ->
            CompositeColumn.create (CompositeHeader.Output io, EditColumnModal.cellsToFreeText (column))
        | CompositeHeaderDiscriminate.Output, None ->
            CompositeColumn.create (CompositeHeader.Output IOType.Sample, EditColumnModal.cellsToFreeText (column))
        // -- single columns --
        | CompositeHeaderDiscriminate.ProtocolREF, _ ->
            CompositeColumn.create (CompositeHeader.ProtocolREF, EditColumnModal.cellsToFreeText (column))
        | CompositeHeaderDiscriminate.Date, _ ->
            CompositeColumn.create (CompositeHeader.Date, EditColumnModal.cellsToFreeText (column))
        | CompositeHeaderDiscriminate.Performer, _ ->
            CompositeColumn.create (CompositeHeader.Performer, EditColumnModal.cellsToFreeText (column))
        | CompositeHeaderDiscriminate.ProtocolDescription, _ ->
            CompositeColumn.create (CompositeHeader.ProtocolDescription, EditColumnModal.cellsToFreeText (column))
        | CompositeHeaderDiscriminate.ProtocolType, _ ->
            CompositeColumn.create (CompositeHeader.ProtocolType, EditColumnModal.cellsToTermCells (column))
        | CompositeHeaderDiscriminate.ProtocolUri, _ ->
            CompositeColumn.create (CompositeHeader.ProtocolUri, EditColumnModal.cellsToFreeText (column))
        | CompositeHeaderDiscriminate.ProtocolVersion, _ ->
            CompositeColumn.create (CompositeHeader.ProtocolVersion, EditColumnModal.cellsToFreeText (column))
        | CompositeHeaderDiscriminate.Comment, _ (*-> failwith "Comment header type is not yet implemented"*)
        | CompositeHeaderDiscriminate.Freetext, _ ->
            CompositeColumn.create (
                CompositeHeader.FreeText(header.ToString()),
                EditColumnModal.cellsToFreeText (column)
            )
    //failwith "Freetext header type is not yet implemented"

    static member modalActivity(state: State, setState) =
        Html.div [
            prop.children [
                Html.div [
                    prop.className "swt:join"
                    prop.children [
                        EditColumnModal.SelectHeaderType(state, setState)
                        match state.NextHeaderType with
                        | CompositeHeaderDiscriminate.Output
                        | CompositeHeaderDiscriminate.Input -> EditColumnModal.SelectIOType(state, setState)
                        | _ -> Html.none
                    ]
                ]
            ]
        ]

    static member placeHolderTermCell =
        CompositeCell.createTermFromString ("Name", "Term-Source-Reference", "Term-Accession-Number")

    static member placeHolderUnitCell =
        CompositeCell.createUnitizedFromString ("Value", "Name", "Term-Source-Reference", "Term-Accession-Number")

    static member placeHolderDataCell =
        CompositeCell.createDataFromString ("Value", "Format", "SelectorFormat")

    static member content(column0, state) =
        let previewColumn =
            let cells = Array.truncate 15 (column0.Cells |> Array.ofSeq)
            //Replace empty cells with placeholder data to represent meaningfull information in the Preview
            cells
            |> Array.iteri (fun i cell ->
                let content = cell.GetContent()

                if (Array.filter (fun item -> item = "") content).Length > 0 then
                    match cell with
                    | cell when cell.isTerm -> cells.[i] <- EditColumnModal.placeHolderTermCell
                    | cell when cell.isUnitized -> cells.[i] <- EditColumnModal.placeHolderUnitCell
                    | cell when cell.isData -> cells.[i] <- EditColumnModal.placeHolderDataCell
                    | _ -> cells.[i] <- EditColumnModal.placeHolderTermCell
            )

            EditColumnModal.updateColumn (
                {
                    column0 with
                        Cells = cells |> ResizeArray
                },
                state
            )

        React.Fragment [
            Html.label [ prop.text "Preview:" ]
            Html.div [
                prop.style [
                    style.maxHeight (length.perc 85)
                    style.overflow.hidden
                    style.display.flex
                ]
                prop.children [ EditColumnModal.Preview(previewColumn) ]
            ]
        ]

    static member footer(column0, state, setColumn, rmv) =
        let submit () =
            let updatedColumn = EditColumnModal.updateColumn (column0, state)
            setColumn updatedColumn
            rmv ()

        Html.div [
            prop.className "swt:justify-end swt:flex swt:gap-2"
            prop.style [ style.marginLeft length.auto ]
            prop.children [
                Html.button [
                    prop.className "swt:btn swt:btn-outline"
                    prop.text "Cancel"
                    prop.onClick (fun _ -> rmv ())
                ]
                Html.button [
                    prop.className "swt:btn swt:btn-primary"
                    prop.style [ style.marginLeft length.auto ]
                    prop.text "Submit"
                    prop.onClick (fun _ -> submit ())
                ]
            ]
        ]

    [<ReactComponent>]
    static member EditColumnModal(columnIndex: int, table: ArcTable, setColumn, rmv, ?debug) =

        let column = table.GetColumn columnIndex
        let state, setState = React.useState (State.init column.Header.AsDiscriminate)
        let debug = defaultArg debug false

        Html.div [
            prop.className "swt:flex swt:flex-col swt:h-full swt:gap-4 swt:min-h-[500px]"
            if debug then
                prop.testId "Edit Column"
            prop.children [
                Html.div [
                    prop.className "swt:border-b swt:pb-2 swt:mb-2"
                    prop.children [ EditColumnModal.modalActivity (state, setState) ]
                ]
                Html.div [
                    prop.className "swt:grow swt:overflow-y-auto swt:h-[200px]"
                    prop.children [ EditColumnModal.content (column, state) ]
                ]
                Html.div [
                    prop.className "swt:border-t swt:pt-2 swt:mt-2"
                    prop.children [ EditColumnModal.footer (column, state, setColumn, rmv) ]
                ]
            ]
        ]



type UpdateColumnModal =

    [<ReactComponent>]
    static member private UpdateForm(cellValues: string[], setPreview, regex: string, setRegex: string -> unit) =
        let replacement, setReplacement = React.useState ("")

        let updateCells (replacement: string) (regex: string) =
            if regex <> "" then
                try
                    let regex = Regex(regex)

                    cellValues
                    |> Array.mapi (fun i c ->
                        let m = regex.Match(c)

                        match m.Success with
                        | true ->
                            // regex.Replace(c, replacement)
                            let replaced = c.Replace(m.Value, replacement)
                            replaced
                        | false -> c
                    )
                    |> setPreview
                with _ ->
                    ()
            else
                ()

        Html.div [
            prop.className "swt:flex gap-2"
            prop.children [
                Html.div [
                    prop.className "swt:fieldset"
                    prop.children [
                        Html.legend [ prop.className "swt:fieldset-legend"; prop.text "Regex" ]
                        Html.input [
                            prop.autoFocus true
                            prop.className "swt:input swt:input-xs swt:sm:input-sm swt:md:input-md"
                            prop.valueOrDefault regex
                            prop.onChange (fun (ev: Browser.Types.Event) ->
                                let target = ev.target :?> Browser.Types.HTMLInputElement
                                let value = target.value
                                setRegex value
                                updateCells replacement value
                            )
                        ]
                        Html.legend [
                            prop.className "swt:fieldset-legend"
                            prop.text "Replacement"
                        ]
                        Html.input [
                            prop.className "swt:input swt:input-xs swt:sm:input-sm swt:md:input-md"
                            prop.valueOrDefault replacement
                            prop.onChange (fun (ev: Browser.Types.Event) ->
                                let target = ev.target :?> Browser.Types.HTMLInputElement
                                let value = target.value
                                setReplacement value
                                updateCells value regex
                            )
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member UpdateColumnModal(columnIndex: int, arcTable: ArcTable, setColumn, rmv: unit -> unit, ?debug) =

        let column = arcTable.GetColumn(columnIndex)

        let getCellStrings () =
            column.Cells |> Array.ofSeq |> Array.map (fun c -> c.ToString())

        let preview, setPreview = React.useState (getCellStrings)

        /// This state is only used for update logic
        let regex, setRegex = React.useState ("")

        let debug = defaultArg debug false

        let submit =
            fun () ->
                preview
                |> Array.map (fun x -> CompositeCell.FreeText x)
                |> fun x -> CompositeColumn.create (column.Header, x |> ResizeArray)
                |> fun column -> setColumn column

        let content = ComponentHelper.PreviewTable(column, preview, regex)

        let footer =
            Html.div [
                if debug then
                    prop.testId "Update Column"
                prop.className "swt:justify-end swt:flex swt:gap-2"
                prop.style [ style.marginLeft length.auto ]
                prop.children [
                    Html.button [
                        prop.className "swt:btn swt:btn-outline"
                        prop.text "Cancel"
                        prop.onClick (fun _ -> rmv ())
                    ]
                    Html.button [
                        prop.className "swt:btn swt:btn-primary"
                        prop.style [ style.marginLeft length.auto ]
                        prop.text "Submit"
                        prop.onClick (fun _ ->
                            submit ()
                            rmv ()
                        )
                    ]
                ]
            ]

        Html.div [
            prop.className "swt:flex swt:flex-col swt:h-full swt:gap-4 swt:min-h-[500px]"
            prop.children [
                Html.div [
                    prop.className "swt:border-b swt:pb-2 swt:mb-2"
                    prop.children [
                        UpdateColumnModal.UpdateForm(getCellStrings (), setPreview, regex, setRegex)
                    ]
                ]
                Html.div [
                    prop.className "swt:grow swt:overflow-y-auto swt:h-[200px]"
                    prop.children [ content ]
                ]
                Html.div [
                    prop.className "swt:border-t swt:pt-2 swt:mt-2"
                    prop.children [ footer ]
                ]
            ]
        ]


type EditConfig =

    static member EditTabs(columnIndex, table, selectedTab, setSelectedTab, setColumn, rmv, ?debug) =
        Html.div [
            Html.div [
                prop.className "swt:flex swt:flex-col swt:gap-2"
                prop.children [
                    Html.div [
                        prop.className "swt:tabs swt:tabs-box swt:my-1 swt:w-fit swt:mx-auto"
                        prop.children [
                            Html.div [
                                prop.className [
                                    "swt:tab"
                                    if selectedTab = 0 then
                                        "swt:tab-active"
                                ]
                                prop.text "Edit Column"
                                prop.onClick (fun _ -> setSelectedTab 0)
                            ]
                            Html.div [
                                prop.className [
                                    "swt:tab"
                                    if selectedTab = 1 then
                                        "swt:tab-active"
                                ]
                                prop.text "Generate Rows"
                                prop.onClick (fun _ -> setSelectedTab 1)
                            ]
                            Html.div [
                                prop.className [
                                    "swt:tab"
                                    if selectedTab = 2 then
                                        "swt:tab-active"
                                ]
                                prop.text "Update Rows"
                                prop.onClick (fun _ -> setSelectedTab 2)
                            ]
                        ]
                    ]
                ]
            ]
            Html.div [
                prop.children [
                    match selectedTab with
                    | 0 -> EditColumnModal.EditColumnModal(columnIndex, table, setColumn, rmv, ?debug = debug)
                    | 1 -> CreateColumnModal.CreateColumnModal(columnIndex, table, setColumn, rmv, ?debug = debug)
                    | 2 -> UpdateColumnModal.UpdateColumnModal(columnIndex, table, setColumn, rmv, ?debug = debug)
                    | _ -> Html.none
                ]
            ]
        ]

    [<ReactComponent>]
    static member CompositeCellEditModal(columnIndex, table: ArcTable, setArcTable, rmv: unit -> unit, ?debug: bool) =

        let selectedTab, setSelectedTab = React.useState (0)

        let setColumn =
            fun (column: CompositeColumn) ->
                table.UpdateColumn(columnIndex, column.Header, column.Cells)
                setArcTable table

        let debugString = if debug.IsSome && debug.Value then Some "Edit" else None

        BaseModal.Modal(
            true,
            (fun _ -> rmv ()),
            Html.div "Edit Column",
            React.Fragment [
                EditConfig.EditTabs(columnIndex, table, selectedTab, setSelectedTab, setColumn, rmv, ?debug = debug)
            ],
            ?debug = debugString
        )
