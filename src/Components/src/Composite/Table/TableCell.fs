namespace Swate.Components.Composite.Table

open Swate.Components
open Swate.Components.Shared
open Swate.Components.Composite.Table.Context
open Swate.Components.Composite.TermSearch
open Swate.Components.Composite.TermSearch.Types
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open ARCtrl
open Swate.Components.Primitive

type ActiveCellProps<'a> = {|
    data: 'a
    setData: 'a -> unit
    setDataForce: 'a -> unit
    onBlur: Browser.Types.FocusEvent -> unit
    onKeyDown: Browser.Types.KeyboardEvent -> unit
|}

[<Mangle(false); Erase>]
type TableCell =

    [<ReactComponent>]
    static member BaseCell
        (
            rowIndex: int,
            columnIndex: int,
            content: ReactElement,
            ?className: string,
            ?props: IReactProperty list,
            ?debug: bool
        ) =
        let debug = defaultArg debug false

        Html.div [
            prop.key $"BaseCell-{rowIndex}-{columnIndex}"
            if debug then
                prop.testId $"cell-{rowIndex}-{columnIndex}"
            prop.className [
                if className.IsSome then
                    className.Value
            ]
            if props.IsSome then
                yield! props.Value
            prop.children [ content ]
        ]

    [<ReactComponent>]
    static member StringInactiveCell(index: CellCoordinate, text: string, ?disableActivation: bool, ?debug) =

        TableCell.InactiveCell(index, Html.text text, ?disableActivation = disableActivation, ?debug = debug)

    [<ReactComponent>]
    static member InactiveCell(index: CellCoordinate, children: ReactElement, ?disableActivation: bool, ?debug) =
        let disableActivation = defaultArg disableActivation false
        let ctx = useTableStateCtx ()

        let isSelected = ctx.isSelected index

        TableCell.BaseCell(
            index.y,
            index.x,
            Html.div [
                prop.className [
                    if not isSelected && index.y = 0 then
                        "swt:bg-base-300 swt:text-base-content"
                    "swt:flex swt:flex-row swt:gap-2 swt:items-center swt:h-full swt:max-w-full swt:px-2 swt:py-2 swt:w-full swt:truncate"
                ]
                prop.children children
            ],
            props = [
                if not disableActivation then
                    prop.onClick (fun e -> ctx.onClick index e)
            ],
            className = "swt:w-full swt:h-full swt:truncate",
            ?debug = debug
        )

    [<ReactComponent>]
    static member BaseActiveCell<'a>
        (
            index: CellCoordinate,
            data: 'a,
            setData: 'a -> unit,
            dataRenderfn: ActiveCellProps<'a> -> ReactElement,
            ?isStickyHeader: bool,
            ?debug: bool
        ) =

        let tempData, setTempData = React.useState (data)

        React.useEffect ((fun _ -> setTempData data), [| box data |])

        let ctx = useTableStateCtx ()

        let isCancelledRef = React.useRef (false)
        let isSetForced = React.useRef (false)

        let props = [

            if debug.IsSome && debug.Value then
                prop.testId $"active-cell-{index.y}-{index.x}"

            if isStickyHeader.IsSome && isStickyHeader.Value then
                prop.style [
                    style.position.sticky
                    style.height Constants.Table.DefaultRowHeight
                    style.top Constants.Table.DefaultRowHeight
                ]
        ]

        let reset =
            fun () ->
                setTempData data
                isCancelledRef.current <- false
                isSetForced.current <- false

        let setTempData =
            fun data ->
                isSetForced.current <- false
                setTempData data

        let onKeydown =
            fun (e: Browser.Types.KeyboardEvent) ->

                match e.code with
                | kbdEventCode.enter ->

                    if isSetForced.current || isCancelledRef.current then
                        ()
                    else
                        isSetForced.current <- true
                        setData tempData
                | kbdEventCode.escape -> isCancelledRef.current <- true
                | _ -> ()

                ctx.onKeyDown index e

        let onBlur =
            fun e ->

                if not isCancelledRef.current && not isSetForced.current then
                    setData tempData
                else
                    reset ()

                ctx.onBlur index e

        let setDataForce =
            fun (value: 'a) ->
                isSetForced.current <- true
                setData value

        let Renderer =
            // ReactLegacy.createElement (
            //     unbox<ReactElement>
            // )
            dataRenderfn {|
                data = tempData
                setData = setTempData
                setDataForce = setDataForce
                onBlur = onBlur
                onKeyDown = onKeydown
            |}

        TableCell.BaseCell(
            index.y,
            index.x,
            Renderer,
            ?debug = debug,
            props = props,
            className = "swt:w-full swt:h-full"
        )

    [<ReactComponent>]
    static member StringActiveCell(index: CellCoordinate, data: string, setData, ?isStickyHeader: bool, ?debug: bool) =

        TableCell.BaseActiveCell(
            index,
            data,
            setData,
            (fun props ->
                Html.input [
                    prop.autoFocus true
                    prop.onFocus (fun e -> (e.target :?> Browser.Types.HTMLInputElement).select ())
                    if debug.IsSome && debug.Value then
                        prop.testid ($"active-cell-string-input-{index.y}-{index.x}")
                    prop.className "swt:rounded-none swt:w-full swt:h-full swt:input swt:outline-0! swt:border-0!"
                    prop.defaultValue props.data
                    prop.onChange (fun (e: string) -> props.setData e)
                    prop.onKeyDown props.onKeyDown
                    prop.onBlur props.onBlur
                ]
            ),
            ?isStickyHeader = isStickyHeader,
            ?debug = debug
        )

    [<ReactComponent>]
    static member OntologyAnnotationActiveCell
        (
            index: CellCoordinate,
            oa: OntologyAnnotation,
            setOa: OntologyAnnotation -> unit,
            ?isStickyHeader: bool,
            ?debug: bool,
            ?parentId: string,
            ?key: string
        ) =

        let term =
            if oa.isEmpty () then
                None
            else
                Term.fromOntologyAnnotation oa |> Some

        let setTerm =
            fun (t: Term option) ->
                let oa =
                    t
                    |> Option.map Term.toOntologyAnnotation
                    |> Option.defaultValue (OntologyAnnotation())

                setOa (oa)

        let TermSearch =
            fun (props: ActiveCellProps<Term option>) ->
                TermSearch.TermSearch(
                    props.data,
                    props.setData,
                    ?parentId = parentId,
                    onBlur = (fun e -> props.onBlur e),
                    onFocus = (fun e -> (e.target :?> Browser.Types.HTMLInputElement).select ()),
                    onKeyDown = (fun e -> props.onKeyDown e),
                    classNames =
                        TermSearchStyle(!^"swt:rounded-none swt:w-full swt:h-full swt:!outline-0 swt:!border-0"),
                    onTermSelect = (fun term -> props.setDataForce (Some term)),
                    autoFocus = true
                )

        TableCell.BaseActiveCell<Term option>(
            index,
            term,
            setTerm,
            TermSearch,
            ?isStickyHeader = isStickyHeader,
            ?debug = debug
        )

    [<ReactComponent>]
    static member CompositeCellInactiveCell
        (index: CellCoordinate, cell: CompositeCell, ?disableActivation: bool, ?debug)
        =
        let text = cell.ToString()

        let termAccession =
            match cell with
            | term when term.isTerm -> cell.AsTerm.TermAccessionShort
            | unit when unit.isUnitized -> (snd cell.AsUnitized).TermAccessionShort
            | _ -> ""

        let oa = cell.ToOA()

        TableCell.InactiveCell(
            index,
            Html.div [
                prop.className "swt:flex swt:w-full swt:justify-between"
                prop.children [
                    Html.span [ prop.text text; prop.className "swt:truncate" ]
                    if oa.TermAccessionShort |> System.String.IsNullOrWhiteSpace |> not then
                        Html.i [
                            prop.className "swt:text-primary"
                            prop.title termAccession
                            prop.children [ Icons.Check() ]
                        ]
                ]
            ],
            ?debug = debug,
            ?disableActivation = disableActivation
        )

    [<ReactComponent>]
    static member CompositeCellActiveCell
        (
            index: CellCoordinate,
            cell: CompositeCell,
            setCell: CompositeCell -> unit,
            ?parentId: string,
            ?debug,
            ?key: string
        ) =

        match cell with
        | CompositeCell.Term oa ->
            TableCell.OntologyAnnotationActiveCell(
                index,
                oa,
                (fun t -> setCell (CompositeCell.Term t)),
                ?parentId = parentId,
                ?debug = debug,
                ?key = key
            )
        | CompositeCell.FreeText txt ->
            TableCell.StringActiveCell(index, txt, (fun t -> setCell (CompositeCell.FreeText t)), ?debug = debug)
        | CompositeCell.Unitized(v, oa) ->
            TableCell.StringActiveCell(
                index,
                v,
                (fun input -> setCell (CompositeCell.Unitized(input, oa))),
                ?debug = debug
            )
        | CompositeCell.Data d ->
            TableCell.StringActiveCell(
                index,
                Option.defaultValue "" d.Name,
                (fun t ->
                    let nextData = d.Copy()
                    nextData.Name <- t |> Option.whereNot System.String.IsNullOrWhiteSpace
                    setCell (CompositeCell.Data nextData)
                ),
                ?debug = debug
            )

// let tempTerm, setTempTerm = React.useState (term)
// TermSearch.TermSearch(
//     tempTerm,
//     (fun input -> ()),
//     ?parentId = parentId,
//     onBlur = (fun e -> ts.onBlur e),
//     onKeyDown = (fun e -> ts.onKeyDown e),
//     classNames = TermSearchStyle(!^"swt:rounded-none swt:w-full swt:h-full swt:!outline-0 swt:!border-0"),
//     onTermSelect = (fun term -> setTerm (Some term)),
//     autoFocus = true
// )
