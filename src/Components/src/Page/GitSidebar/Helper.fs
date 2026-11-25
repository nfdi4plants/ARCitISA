module Arcitisa.Components.Page.GitSidebarHelper

open Browser.Types
open Feliz
open Arcitisa.Components
open Arcitisa.Components.Page.GitSidebarTypes

type ChangePresentation = {
    Label: string
    Shorthand: string
    ToneClassName: string
}

/// How a change shows in a list. A change with an original path shows as renamed, unless it is conflicted.
let changePresentation (change: GitSidebarChange) : ChangePresentation =
    let kind =
        match change.Kind, change.OriginalPath with
        | GitSidebarChangeKind.Conflicted, _ -> GitSidebarChangeKind.Conflicted
        | _, Some _ -> GitSidebarChangeKind.Renamed
        | kind, None -> kind

    match kind with
    | GitSidebarChangeKind.Added -> {
        Label = "Added"
        Shorthand = "A"
        ToneClassName = "swt:text-success"
      }
    | GitSidebarChangeKind.Modified -> {
        Label = "Modified"
        Shorthand = "M"
        ToneClassName = "swt:text-warning"
      }
    | GitSidebarChangeKind.Deleted -> {
        Label = "Deleted"
        Shorthand = "D"
        ToneClassName = "swt:text-error"
      }
    | GitSidebarChangeKind.Renamed -> {
        Label = "Renamed"
        Shorthand = "R"
        ToneClassName = "swt:text-info"
      }
    | GitSidebarChangeKind.Conflicted -> {
        Label = "Conflict"
        Shorthand = "C"
        ToneClassName = "swt:text-error"
      }

/// Splits a repository-relative path into the file name and its parent directory. A trailing slash stays with the name.
let splitPath (path: string) : string * string option =
    match path.TrimEnd('/').LastIndexOf '/' with
    | index when index > 0 -> path.Substring(index + 1), Some(path.Substring(0, index))
    | _ -> path, None

/// One row that the list renders, and the offset it starts at.
type VirtualRow = { Index: int; Start: int }

[<NoEquality; NoComparison>]
type VirtualRows = {
    ScrollContainerRef: IRefValue<HTMLElement option>
    Rows: VirtualRow[]
    TotalSize: int
    MeasureElement: VirtualMeasureElementRef
}

/// Sets up the virtualizer of a scrolling list and returns the rows to render. A list without a layout (the first
/// paint, jsdom) makes the virtualizer return no rows. The first rows are then laid out from their estimated sizes.
/// getItemKey has to keep its identity between renders, otherwise the virtualizer computes every offset again.
[<Hook>]
let useVirtualRows
    (count: int)
    (getItemKey: int -> string)
    (estimateSize: int -> int)
    (gap: int)
    (overscan: int)
    : VirtualRows =
    let scrollContainerRef: IRefValue<HTMLElement option> = React.useElementRef ()

    let virtualizer =
        Virtual.useVirtualizer (
            count = count,
            getScrollElement = (fun () -> scrollContainerRef.current),
            estimateSize = estimateSize,
            getItemKey = getItemKey,
            overscan = overscan,
            gap = gap
        )

    let measuredItems = virtualizer.getVirtualItems ()

    let rows =
        if measuredItems.Length = 0 && count > 0 then
            let isInitialScrollPosition =
                scrollContainerRef.current
                |> Option.map (fun element -> element.scrollTop = 0.0)
                |> Option.defaultValue true

            if not isInitialScrollPosition then
                [||]
            else
                let indexes = [| 0 .. min (count - 1) overscan |]

                let starts =
                    indexes |> Array.scan (fun start index -> start + estimateSize index + gap) 0

                indexes
                |> Array.map (fun index -> {
                    Index = index
                    Start = starts.[index]
                })
        else
            measuredItems
            |> Array.map (fun item -> {
                Index = item.index
                Start = item.start
            })

    {
        ScrollContainerRef = scrollContainerRef
        Rows = rows
        TotalSize = virtualizer.getTotalSize ()
        MeasureElement = virtualizer.measureElement
    }
