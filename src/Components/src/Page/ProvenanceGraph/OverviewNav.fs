namespace Swate.Components.Page.ProvenanceGraph

open Browser.Types
open Fable.Core
open Feliz
open Swate.Components
open Swate.Components.Composite.GraphCanvas.Types
open Swate.Components.Page.ProvenanceGraph.Types

module private OverviewNavHelper =

    /// The row with the window that the overview kept for its level. A level that the overview has not drawn yet
    /// keeps the window of the row.
    let withKeptWindow (starts: Map<string, int>) (row: OverviewRow) : OverviewRow =
        match Map.tryFind row.Key starts with
        | Some start ->
            let selected = row.Items |> Array.tryFindIndex (fun item -> item.Selected)

            let start, count =
                Overview.stableWindow row.Items.Length selected Overview.maxItems (Some start)

            {
                row with
                    WindowStart = start
                    WindowCount = count
            }
        | None -> row

    /// The items that a row of the overview shows, and the counts of the items left out before and after them.
    /// An expanded row shows every item.
    let overviewShown (row: OverviewRow) (isExpanded: bool) : OverviewItem[] * int * int =
        if isExpanded then
            row.Items, 0, 0
        else
            Array.sub row.Items row.WindowStart row.WindowCount,
            row.WindowStart,
            row.Items.Length - row.WindowStart - row.WindowCount

    /// The key of the chip of an item. The keys of the controls tell them apart for the single tab stop.
    let overviewItemKey (depth: int) (itemId: string) : string = $"{depth}:item:{itemId}"

    /// The keys of the controls of a row in the order of the page. The selected item of the deepest row marks the
    /// place and is no control.
    let overviewControlKeys (depth: int) (row: OverviewRow) (isExpanded: bool) (isDeepest: bool) : string[] =
        let shown, before, after = overviewShown row isExpanded

        [|
            if isExpanded && row.Items.Length > row.WindowCount then
                $"{depth}:less"

            if before > 0 then
                $"{depth}:more-before"

            for item in shown do
                if not (item.Selected && isDeepest) then
                    overviewItemKey depth item.Id

            if after > 0 then
                $"{depth}:more-after"
        |]

    /// The control that holds the tab stop while the user has not focused one. It is the selected item of the
    /// deepest row above the last one, which leads back to that level. A view without such an item takes the
    /// first control of the last row.
    let defaultOverviewControl (rows: OverviewRow[]) (expanded: Set<string>) : string option =
        let selectedAbove =
            rows
            |> Array.indexed
            |> Array.take (max 0 (rows.Length - 1))
            |> Array.rev
            |> Array.tryPick (fun (depth, row) ->
                row.Items
                |> Array.tryFind (fun item -> item.Selected)
                |> Option.map (fun item -> overviewItemKey depth item.Id)
            )

        match selectedAbove, Array.tryLast rows with
        | Some key, _ -> Some key
        | None, Some last ->
            let depth = rows.Length - 1

            overviewControlKeys depth last (expanded.Contains last.Key) true
            |> Array.tryHead
        | None, None -> None

    /// The key that a control of the overview carries in its data attribute.
    let overviewControlKey (control: HTMLElement) : string =
        control.getAttribute "data-overview-key"

    /// The buttons of each row of the overview list, in the order of the page.
    let overviewControls (list: HTMLElement) : HTMLElement[][] = [|
        for row in 0 .. list.children.length - 1 ->
            let buttons = list.children.[row].querySelectorAll "button"

            [|
                for i in 0 .. buttons.length - 1 -> buttons.[i] :?> HTMLElement
            |]
    |]

    /// The control that a key moves the focus to from `current`. Left and right stay in the row and stop at its
    /// ends, and Home and End go to its first and last control. Up and down go to the nearest row with controls,
    /// to its selected control or else to its first one. The answer is None when the key does not move the focus.
    let nextOverviewControl (controls: HTMLElement[][]) (current: HTMLElement) (key: string) : HTMLElement option =
        let isCurrent (control: HTMLElement) = obj.ReferenceEquals(control, current)

        match controls |> Array.tryFindIndex (Array.exists isCurrent) with
        | None -> None
        | Some rowIndex ->
            let row = controls.[rowIndex]
            let position = row |> Array.findIndex isCurrent

            let entryOf (target: HTMLElement[]) =
                target
                |> Array.tryFind (fun control -> control.getAttribute "aria-current" = "true")
                |> Option.orElse (Array.tryHead target)

            let nonEmptyRows =
                controls |> Array.indexed |> Array.filter (fun (_, target) -> target.Length > 0)

            match key with
            | "ArrowRight" ->
                if position + 1 < row.Length then
                    Some row.[position + 1]
                else
                    None
            | "ArrowLeft" -> if position > 0 then Some row.[position - 1] else None
            | "Home" -> Array.tryHead row
            | "End" -> Array.tryLast row
            | "ArrowDown" ->
                nonEmptyRows
                |> Array.tryFind (fun (index, _) -> index > rowIndex)
                |> Option.bind (snd >> entryOf)
            | "ArrowUp" ->
                nonEmptyRows
                |> Array.tryFindBack (fun (index, _) -> index < rowIndex)
                |> Option.bind (snd >> entryOf)
            | _ -> None

open OverviewNavHelper

/// The levels above the current view, one row per level, with the item that leads to the view marked. A click on
/// an item opens the view of that item, like a click on a node of the level. A row that has more items than it
/// shows expands on request. A level keeps its state while the user navigates: an expanded row stays expanded and
/// a collapsed row keeps its window of items as long as the selected item lies inside it. The overview has one tab
/// stop, which a new view resets to the default control. The arrow keys move the focus between its controls, and
/// Home and End jump to the ends of a row.
[<Erase; Mangle(false)>]
type OverviewNav =

    [<ReactComponent>]
    static member OverviewNav(rows: OverviewRow[], id: string, viewKey: string, onNavigate: ProvenanceView -> unit) =
        // React lets a component reset its state while it renders, so a new view never draws the tab stop of the
        // old one.
        let seenKey, setSeenKey = React.useState viewKey
        let focusedState, setFocusedState = React.useState ""
        let isSameView = seenKey = viewKey

        if not isSameView then
            setSeenKey viewKey
            setFocusedState ""

        let focused = if isSameView then focusedState else ""

        // The expanded levels and the window starts belong to the levels, not to the view. They live as long as the
        // overview does, so the rows hold still when the user picks an item or goes down a level and back up.
        let noLevels: Set<string> = Set.empty
        let expanded, setExpanded = React.useState noLevels
        let noStarts: Map<string, int> = Map.empty
        let windowStarts, setWindowStarts = React.useState noStarts

        // The render reads the starts of the previous render, and the layout effect stores the ones that changed
        // before the browser paints.
        let rows =
            React.useMemo (
                (fun () -> rows |> Array.map (withKeptWindow windowStarts)),
                [| box rows; box windowStarts |]
            )

        React.useLayoutEffect (
            (fun () ->
                let changed =
                    rows
                    |> Array.filter (fun row -> Map.tryFind row.Key windowStarts <> Some row.WindowStart)

                if changed.Length > 0 then
                    setWindowStarts (
                        changed
                        |> Array.fold (fun starts row -> Map.add row.Key row.WindowStart starts) windowStarts
                    )
            ),
            [| box rows |]
        )

        // The last focused control keeps the tab stop as long as it is on the page. Otherwise the default control
        // takes it.
        let activeKey =
            let allKeys =
                rows
                |> Array.indexed
                |> Array.collect (fun (depth, row) ->
                    overviewControlKeys depth row (expanded.Contains row.Key) (depth = rows.Length - 1)
                )

            if focused <> "" && Array.contains focused allKeys then
                Some focused
            else
                defaultOverviewControl rows expanded

        // The button that expands or collapses a row leaves the page with the click, and the focus would fall to the
        // page. The row waits in the ref until the render has committed, and the layout effect below moves the focus.
        let listRef = React.useRef<HTMLElement option> None
        let pendingFocus = React.useRef<(int * bool) option> None

        let setRowOpen (depth: int) (key: string) (isOpen: bool) =
            pendingFocus.current <- Some(depth, isOpen)

            setExpanded (
                if isOpen then
                    Set.add key expanded
                else
                    Set.remove key expanded
            )

        // An expanded row focuses its "Show less" button. A collapsed row focuses its last "+N" button, which is
        // the count after the window when the row has one. The focus also moves the tab stop to the button.
        React.useLayoutEffect (
            (fun () ->
                match pendingFocus.current, listRef.current with
                | Some(depth, isOpen), Some list ->
                    pendingFocus.current <- None
                    let controls = overviewControls list

                    if depth < controls.Length then
                        let wanted =
                            if isOpen then
                                controls.[depth]
                                |> Array.tryFind (fun control -> overviewControlKey control = $"{depth}:less")
                            else
                                controls.[depth]
                                |> Array.filter (fun control ->
                                    let key = overviewControlKey control
                                    key = $"{depth}:more-before" || key = $"{depth}:more-after"
                                )
                                |> Array.tryLast

                        wanted |> Option.iter (fun control -> control.focus ())
                | _ -> ()
            ),
            [| box expanded |]
        )

        // Only the control with the tab stop can be reached with the Tab key. The others take the arrow keys.
        let hintId = React.useId ()

        let controlProps (key: string) : IReactProperty list = [
            prop.custom ("data-overview-key", key)
            prop.custom ("aria-describedby", hintId)
            prop.tabIndex (if activeKey = Some key then 0 else -1)
        ]

        let controlClass =
            "swt:flex-none swt:cursor-pointer swt:rounded swt:px-1 swt:text-base-content/80 swt:hover:bg-base-200 swt:focus-visible:-outline-offset-2"

        // The accessible name of the button is its visible text. The screen reader text completes the count.
        let moreButton (key: string) (depth: int) (row: OverviewRow) (count: int) : ReactElement =
            Html.button (
                controlProps $"{depth}:more-{key}"
                @ [
                    prop.key key
                    prop.type'.button
                    prop.testId "provenance-overview-more"
                    prop.className controlClass
                    prop.onClick (fun _ -> setRowOpen depth row.Key true)
                    prop.children [
                        Html.text $"+{count}"
                        Html.span [ prop.className "swt:sr-only"; prop.text " more" ]
                    ]
                ]
            )

        let lessButton (depth: int) (row: OverviewRow) : ReactElement =
            Html.button (
                controlProps $"{depth}:less"
                @ [
                    prop.key "less"
                    prop.type'.button
                    prop.testId "provenance-overview-less"
                    prop.className controlClass
                    prop.onClick (fun _ -> setRowOpen depth row.Key false)
                    prop.text "Show less"
                ]
            )

        // The selected item of the deepest row leads to the current view. It marks the place and takes no click.
        // Every chip has the same font weight and the pin takes no inline space, so a chip keeps its width when it
        // becomes the selected one. The pin sits over the corner and the label truncates in an inner element,
        // because a truncating chip would clip the pin.
        let chip (key: string) (depth: int) (isDeepest: bool) (item: OverviewItem) : ReactElement =
            let isHere = item.Selected && isDeepest

            let chipStyle =
                if item.Selected then
                    [
                        style.custom ("borderColor", accentColor)
                        style.custom ("backgroundColor", "color-mix(in oklch, var(--color-primary) 12%, transparent)")
                    ]
                else
                    [ style.custom ("borderColor", neutralBorder) ]

            let common: IReactProperty list = [
                prop.key key
                prop.testId "provenance-overview-item"
                prop.custom ("data-selected", item.Selected)
                prop.title item.Label
                prop.style chipStyle
            ]

            if isHere then
                Html.span (
                    common
                    @ [
                        prop.custom ("aria-current", "location")
                        prop.className
                            "swt:relative swt:max-w-40 swt:min-w-0 swt:flex-none swt:rounded-full swt:border swt:px-2 swt:py-px"
                        prop.children [
                            Html.span [
                                prop.testId "provenance-overview-location"
                                prop.ariaHidden true
                                prop.className
                                    "swt:pointer-events-none swt:absolute swt:-top-1.5 swt:-left-1.5 swt:flex swt:size-3.5 swt:items-center swt:justify-center swt:rounded-full swt:bg-base-100"
                                prop.children [
                                    Html.i [
                                        prop.className "swt:iconify swt:fluent--location-16-filled swt:size-3"
                                    ]
                                ]
                            ]

                            Html.span [
                                prop.className "swt:block swt:truncate"
                                prop.text item.Label
                            ]
                        ]
                    ]
                )
            elif item.Selected then
                Html.button (
                    common
                    @ controlProps (overviewItemKey depth item.Id)
                    @ [
                        prop.type'.button
                        prop.custom ("aria-current", "true")
                        prop.className
                            "swt:max-w-40 swt:min-w-0 swt:flex-none swt:cursor-pointer swt:truncate swt:rounded-full swt:border swt:px-2 swt:py-px swt:focus-visible:-outline-offset-2"
                        prop.text item.Label
                        prop.onClick (fun _ -> onNavigate item.Target)
                    ]
                )
            else
                Html.button (
                    common
                    @ controlProps (overviewItemKey depth item.Id)
                    @ [
                        prop.type'.button
                        prop.className
                            "swt:max-w-40 swt:min-w-10 swt:cursor-pointer swt:truncate swt:rounded-full swt:border swt:px-2 swt:py-px swt:text-base-content/80 swt:hover:bg-base-200 swt:focus-visible:-outline-offset-2"
                        prop.text item.Label
                        prop.onClick (fun _ -> onNavigate item.Target)
                    ]
                )

        // Every child of a row has a key, so the chips and buttons stay mounted when a row expands. A row wraps
        // when it is wider than the panel, so no control sticks out of it.
        let rowItem (depth: int) (row: OverviewRow) : ReactElement =
            let isDeepest = depth = rows.Length - 1
            let isExpanded = expanded.Contains row.Key
            let hasMore = row.Items.Length > row.WindowCount
            let shown, before, after = overviewShown row isExpanded

            Html.li [
                prop.key depth
                prop.testId $"provenance-overview-row-{depth}"
                prop.style [ style.marginLeft (depth * 14) ]
                prop.className "swt:flex swt:min-w-0 swt:flex-wrap swt:items-center swt:gap-1.5"
                prop.children [
                    if depth > 0 then
                        Html.i [
                            prop.key "connector"
                            prop.ariaHidden true
                            prop.className
                                "swt:iconify swt:fluent--arrow-turn-down-right-20-regular swt:size-3.5 swt:flex-none swt:opacity-60"
                        ]

                    // The heading gives way first when the row is too narrow.
                    Html.span [
                        prop.key "heading"
                        prop.title row.Heading
                        prop.className "swt:min-w-12 swt:shrink-[1000] swt:truncate swt:text-base-content/80"
                        prop.text $"{row.Heading}:"
                    ]

                    // An expanded row can be long. The button to collapse it stays at the start of the row.
                    if isExpanded && hasMore then
                        lessButton depth row

                    if before > 0 then
                        moreButton "before" depth row before

                    for item in shown do
                        chip $"item-{item.Id}" depth isDeepest item

                    if after > 0 then
                        moreButton "after" depth row after

                    if row.Extra.IsSome then
                        Html.span [
                            prop.key "separator"
                            prop.ariaHidden true
                            prop.className "swt:flex-none swt:text-base-content/80"
                            prop.text "·"
                        ]

                    match row.Extra with
                    | Some extra -> chip "extra" depth isDeepest extra
                    | None -> ()
                ]
            ]

        // The panel is at most 40 percent of the page. The page section is its percentage base. A row with
        // hundreds of items scrolls inside the panel and leaves the graph its room. Below 17.75rem of page width
        // the panel hides with its toggle, because it could not be collapsed there. The named container is the
        // page section.
        Html.nav [
            prop.id id
            prop.testId "provenance-overview"
            prop.ariaLabel "Location in the ARC"
            prop.className
                "swt:max-h-[40%] swt:flex-none swt:overflow-y-auto swt:border-b swt:border-base-300 swt:bg-base-100 swt:px-3.5 swt:py-2 swt:text-xs swt:@max-[17.75rem]/graph:hidden"
            prop.onFocus (fun (event: FocusEvent) ->
                let key = overviewControlKey (event.target :?> HTMLElement)

                if not (isNull key) && focused <> key then
                    setFocusedState key
            )
            prop.onKeyDown (fun (event: KeyboardEvent) ->
                if not (event.altKey || event.ctrlKey || event.metaKey) then
                    match listRef.current with
                    | Some list ->
                        match nextOverviewControl (overviewControls list) (event.target :?> HTMLElement) event.key with
                        | Some control ->
                            event.preventDefault ()
                            control.focus ()
                        | None -> ()
                    | None -> ()
            )
            prop.children [
                // The hint tells screen reader users about the arrow keys. It stays hidden on the page, and every
                // control points to it with aria-describedby.
                Html.span [
                    prop.id hintId
                    prop.hidden true
                    prop.text "Use the arrow keys to move between the items."
                ]
                Html.ol [
                    prop.ref listRef
                    prop.className "swt:m-0 swt:flex swt:list-none swt:flex-col swt:gap-1 swt:p-0"
                    prop.children [
                        for depth, row in Array.indexed rows -> rowItem depth row
                    ]
                ]
            ]
        ]
