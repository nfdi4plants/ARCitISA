namespace Swate.Components.Composite.Table

open Fable.Core
open Swate.Components

module GridSelect =

    type GridSelectHandle = {|
        SelectOrigin: CellCoordinate option
        lastAppend: CellCoordinate option
        selectedCells: CellCoordinateRange option
        contains: CellCoordinate -> bool
        selectBy: Browser.Types.KeyboardEvent -> bool
        selectAt: CellCoordinate * bool -> unit
        clear: unit -> unit
        selectedCellsReducedSet: Set<CellCoordinate>
        count: int
    |}

    [<StringEnum>]
    type Kbd =
        | ArrowUp
        | ArrowDown
        | ArrowLeft
        | ArrowRight

        static member tryFromKey(key: string) =
            match key with
            | kbdEventCode.arrowUp -> Some ArrowUp
            | kbdEventCode.arrowDown -> Some ArrowDown
            | kbdEventCode.arrowLeft -> Some ArrowLeft
            | kbdEventCode.arrowRight -> Some ArrowRight
            | _ -> None

        static member fromKey(key: string) =
            match Kbd.tryFromKey key with
            | Some kbd -> kbd
            | None -> failwithf "Unknown key: %s" key

    [<RequireQualifiedAccess>]
    type KeyboardShortcut =
        | Details
        | Delete
        | Paste
        | Copy
        | Cut

    let tryGetKeyboardShortcut
        (event: Browser.Types.KeyboardEvent)
        (selectedCells: GridSelectHandle)
        (activeCell: CellCoordinate option)
        =
        if activeCell.IsSome || selectedCells.count = 0 || event.altKey || event.shiftKey then
            None
        else
            let isControl = event.ctrlKey || event.metaKey

            match event.code with
            | code when
                (code = kbdEventCode.f2 && not isControl)
                || (code = kbdEventCode.enter && isControl)
                ->
                Some KeyboardShortcut.Details
            | code when code = kbdEventCode.delete && not isControl -> Some KeyboardShortcut.Delete
            | code when code = kbdEventCode.key ("v") && isControl -> Some KeyboardShortcut.Paste
            | code when code = kbdEventCode.key ("c") && isControl -> Some KeyboardShortcut.Copy
            | code when
                code = kbdEventCode.key ("x")
                && isControl
                && (selectedCells.selectedCells |> Option.exists (fun range -> range.yStart > 0))
                ->
                Some KeyboardShortcut.Cut
            | _ -> None

    type OnSelect = CellCoordinate -> CellCoordinateRange option -> unit

    module SelectedCellRange =

        let make xStart yStart xEnd yEnd = {|
            xStart = xStart
            yStart = yStart
            xEnd = xEnd
            yEnd = yEnd
        |}

        let create (xStart, yStart, xEnd, yEnd) = {|
            xStart = xStart
            yStart = yStart
            xEnd = xEnd
            yEnd = yEnd
        |}

        let singleton (cell: CellCoordinate) : CellCoordinateRange = {|
            yStart = cell.y
            yEnd = cell.y
            xStart = cell.x
            xEnd = cell.x
        |}

        let toReducedSet (range: CellCoordinateRange option) =
            match range with
            | Some range ->
                set [
                    {| x = range.xStart; y = range.yStart |}
                    {| x = range.xEnd; y = range.yEnd |}
                ]
            | _ -> Set.empty

        let fromSet (selectedCells: Set<CellCoordinate>) =
            if selectedCells.IsEmpty then
                None
            else
                let min = selectedCells.MinimumElement
                let max = selectedCells.MaximumElement

                Some {|
                    xStart = min.x
                    yStart = min.y
                    xEnd = max.x
                    yEnd = max.y
                |}

        let count (selectedCellRange: CellCoordinateRange option) =
            match selectedCellRange with
            | Some range -> (range.xEnd - range.xStart + 1) * (range.yEnd - range.yStart + 1)
            | None -> 0

        let toString (range: CellCoordinateRange option) =
            match range with
            | Some range ->
                let xStart = range.xStart
                let yStart = range.yStart
                let xEnd = range.xEnd
                let yEnd = range.yEnd
                sprintf "x: %d-%d, y: %d-%d" xStart xEnd yStart yEnd
            | None -> "None"

open GridSelect

type GridSelect() =

    member val Origin: CellCoordinate option = None with get, set

    member val internal LastAppend: CellCoordinate option = None with get, set

    member private this.GetNextIndex
        (kbd: Kbd, jump: bool, selectedCells: Set<CellCoordinate>, maxRow, maxCol, minRow, minCol)
        =
        let current =
            match this.LastAppend, this.Origin with
            | Some lastAppend, _ -> lastAppend
            | None, Some origin -> origin
            | None, None ->
                let isIncrease = kbd = ArrowDown || kbd = ArrowRight

                if isIncrease then
                    selectedCells.MaximumElement
                else
                    selectedCells.MinimumElement

        match jump with
        | true ->
            match kbd with
            | ArrowDown -> {| current with y = maxRow |}
            | ArrowUp -> {| current with y = minRow |}
            | ArrowLeft -> {| current with x = minCol |}
            | ArrowRight -> {| current with x = maxCol |}
        | false ->
            match kbd with
            | ArrowDown -> {|
                current with
                    y = min (current.y + 1) maxRow
              |}
            | ArrowUp -> {|
                current with
                    y = max (current.y - 1) minRow
              |}
            | ArrowLeft -> {|
                current with
                    x = max (current.x - 1) minCol
              |}
            | ArrowRight -> {|
                current with
                    x = min (current.x + 1) maxCol
              |}

    member this.SelectBy
        (
            e: Browser.Types.KeyboardEvent,
            selectedCells: CellCoordinateRange option,
            setter: CellCoordinateRange option -> unit,
            maxRow,
            maxCol,
            ?minRow,
            ?minCol,
            ?onSelect
        ) =
        let kbd = Kbd.tryFromKey e.key

        match kbd with
        | Some kbd ->
            e.preventDefault ()
            let jump = e.ctrlKey || e.metaKey

            this.SelectBy(
                kbd,
                jump,
                e.shiftKey,
                selectedCells,
                setter,
                maxRow,
                maxCol,
                ?minRow = minRow,
                ?minCol = minCol,
                ?onSelect = onSelect
            )

            true
        | None -> false

    member this.SelectBy
        (
            kbd: Kbd,
            jump: bool,
            isAppend: bool,
            selectedCells: CellCoordinateRange option,
            setter: CellCoordinateRange option -> unit,
            maxRow: int,
            maxCol: int,
            ?minRow: int,
            ?minCol: int,
            ?onSelect
        ) =
        let minRow = defaultArg minRow 0
        let minCol = defaultArg minCol 0

        if selectedCells.IsNone then
            failwith "No selected cells"

        if not isAppend then
            this.LastAppend <- None
            this.Origin <- None

        let selectedCellsSet = SelectedCellRange.toReducedSet selectedCells

        let nextIndex =
            this.GetNextIndex(kbd, jump, selectedCellsSet, maxRow, maxCol, minRow, minCol)

        this.SelectAt(nextIndex, isAppend, selectedCells, setter, ?onSelect = onSelect)

    member this.SelectAt
        (
            nextIndex: CellCoordinate,
            isAppend: bool,
            selectedCellRange: CellCoordinateRange option,
            setter: CellCoordinateRange option -> unit,
            ?onSelect: OnSelect
        ) =
        match isAppend, this.Origin with // add append origin if we start appending
        | true, None ->
            this.Origin <-
                selectedCellRange
                |> Option.map (fun r -> {| x = r.xStart; y = r.yStart |})
                |> Option.defaultValue nextIndex
                |> Some

            this.LastAppend <- Some nextIndex
        | true, _ -> this.LastAppend <- Some nextIndex
        | false, _ ->
            this.Origin <- None
            this.LastAppend <- None

        let newCellRange =
            if not isAppend then
                Set.singleton (nextIndex) |> SelectedCellRange.fromSet
            else
                let selectedCells =
                    selectedCellRange
                    |> SelectedCellRange.toReducedSet
                    |> function
                        | isEmpty when isEmpty.Count = 0 -> Set.singleton nextIndex
                        | x -> x

                let origin = this.Origin.Value
                let lastAppend = defaultArg this.LastAppend this.Origin.Value

                let minRow =
                    if nextIndex.y <= origin.y then nextIndex.y
                    else if origin.y < lastAppend.y then origin.y
                    else selectedCells.MinimumElement.y

                let maxRow =
                    if nextIndex.y >= origin.y then nextIndex.y
                    else if origin.y > lastAppend.y then origin.y
                    else selectedCells.MaximumElement.y

                let minCol =
                    if nextIndex.x <= origin.x then nextIndex.x
                    else if origin.x < lastAppend.x then origin.x
                    else selectedCells.MinimumElement.x

                let maxCol =
                    if nextIndex.x >= origin.x then nextIndex.x
                    else if origin.x > lastAppend.x then origin.x
                    else selectedCells.MaximumElement.x

                let newCellRange = {|
                    xStart = minCol
                    yStart = minRow
                    xEnd = maxCol
                    yEnd = maxRow
                |}

                Some newCellRange

        setter newCellRange
        onSelect |> Option.iter (fun f -> f nextIndex newCellRange)

    member this.Clear() =
        this.Origin <- None
        this.LastAppend <- None

[<AutoOpen>]
module KeyboardNavExtensions =

    open Feliz

    type React with

        [<Hook>]
        static member useGridSelect
            (rowCount: int, columnCount: int, ?minRow, ?minCol, ?onSelect: OnSelect, ?seed: ResizeArray<CellCoordinate>)
            : GridSelectHandle =
            let minRow = defaultArg minRow 0
            let minCol = defaultArg minCol 0

            let seed: CellCoordinateRange option =
                seed
                |> Option.map Set.ofSeq
                |> Option.map (fun seed -> {|
                    xStart = seed.MinimumElement.x
                    yStart = seed.MinimumElement.y
                    xEnd = seed.MaximumElement.x
                    yEnd = seed.MaximumElement.y
                |})

            let (selectedCells: CellCoordinateRange option), setSelectedCells =
                React.useState (seed)

            let select = React.useRef (GridSelect())

            let selectBy =
                (fun e ->
                    match selectedCells with
                    | Some _ ->
                        select.current.SelectBy(
                            e,
                            selectedCells,
                            setSelectedCells,
                            rowCount - 1,
                            columnCount - 1,
                            minRow,
                            minCol,
                            (fun newIndex newCellRange ->
                                onSelect |> Option.iter (fun onSelect -> onSelect newIndex newCellRange)
                            )
                        )
                    | None -> false
                )

            let selectAt =
                fun (newIndex, isAppend) ->
                    select.current.SelectAt(
                        newIndex,
                        isAppend,
                        selectedCells,
                        setSelectedCells,
                        (fun newIndex newCellRange ->
                            onSelect |> Option.iter (fun onSelect -> onSelect newIndex newCellRange)
                        )
                    )

            let contains =
                fun (cell: CellCoordinate) ->

                    selectedCells.IsSome
                    && cell.x <= selectedCells.Value.xEnd
                    && cell.x >= selectedCells.Value.xStart
                    && cell.y <= selectedCells.Value.yEnd
                    && cell.y >= selectedCells.Value.yStart

            {|
                SelectOrigin = select.current.Origin
                lastAppend = select.current.LastAppend
                selectedCells = selectedCells
                contains = contains
                selectBy = selectBy
                selectAt = selectAt
                clear =
                    fun () ->
                        select.current.Clear()
                        setSelectedCells None

                        selectedCells
                        |> Option.iter (fun range ->
                            onSelect
                            |> Option.iter (fun notify -> notify {| x = range.xStart; y = range.yStart |} None)
                        )
                selectedCellsReducedSet = selectedCells |> SelectedCellRange.toReducedSet
                count = selectedCells |> SelectedCellRange.count
            |}
