module internal Swate.Components.Page.FileExplorer.Tests

open Fable.Core
open Feliz
open Fable.Core.JsInterop
open Vitest
open Swate.Components.Page.FileExplorer
open Swate.Components.Page.FileExplorer.Types

let private file (id: string) : FileItem = {
    FileTree.createFile id None FileItemIcon.Document with
        Id = id
}

let private folder (id: string) (children: FileItem list option) : FileItem = {
    FileTree.createFolder id None FileItemIcon.Folder with
        Id = id
        Children = children
}

let private tree = [
    folder "src" (Some [ file "a.txt"; folder "sub" (Some [ file "deep.txt" ]) ])
    folder "Empty Folder" (Some [])
    folder "Lazy Folder" None
]

let private realisticTree = [
    folder
        "studies"
        (Some [
            folder
                "selected-study"
                (Some [
                    file "isa.study.xlsx"
                    folder "resources" (Some [ file "selected-report.txt"; file "sibling-report.txt" ])
                ])
            folder "neighbor-study" (Some [ file "neighbor-study.xlsx" ])
        ])
    folder
        "assays"
        (Some [
            folder "selected-assay" (Some [ file "isa.assay.xlsx" ])
            folder "neighbor-assay" (Some [ file "neighbor-assay.xlsx" ])
        ])
    file "README.md"
]

let private treeItem (id: string) =
    RTL.screen.getByRole ("treeitem", ByRoleOptions(name = Text id))

Vitest.afterEach (fun () -> RTL.cleanup ())

[<ReactComponent>]
let private ItemsHarness (initial: FileItem list, expose: (FileItem list -> unit) -> unit) =
    let items, setItems = React.useState initial
    expose setItems
    FileExplorer.FileExplorer(initialItems = items)

[<ReactComponent>]
let private ControlledTreeHarness () =
    let items = realisticTree

    let expandedIds, setExpandedIds =
        React.useState (set [ "studies"; "selected-study"; "resources"; "assays" ])

    let selectedId, setSelectedId = React.useState (Some "selected-report.txt")

    let materializedItems =
        React.useMemo (
            (fun () ->
                let rec materialize treeItems =
                    treeItems
                    |> List.map (fun item ->
                        match item.Children with
                        | Some children when expandedIds.Contains item.Id -> {
                            item with
                                Children = Some(materialize children)
                          }
                        | Some [] -> item
                        | Some _ -> { item with Children = None }
                        | None -> item
                    )

                materialize items
            ),
            [| box items; box expandedIds |]
        )

    FileExplorer.FileExplorer(
        initialItems = materializedItems,
        selectedItemId = selectedId,
        expandedIds = expandedIds,
        onExpandedIdsChange = setExpandedIds,
        onItemClick =
            (fun item ->
                if not item.IsDirectory || item.Id = "selected-assay" then
                    setSelectedId (Some item.Id)
            )
    )

Vitest.describe (
    "FileExplorer",
    fun () ->
        Vitest.test (
            "reports the tree position of expanded rows",
            fun () ->
                RTL.render (FileExplorer.FileExplorer(initialItems = tree, expandedIds = set [ "src"; "sub" ]))
                |> ignore

                Vitest.expect((RTL.screen.getByRole "tree").getAttribute "role").toBe "tree"
                let deep = treeItem "deep.txt"
                Vitest.expect(deep.getAttribute "aria-level").toBe "3"
                Vitest.expect(deep.getAttribute "aria-setsize").toBe "1"
                Vitest.expect(deep.getAttribute "aria-posinset").toBe "1"
                let sub = treeItem "sub"
                Vitest.expect(sub.getAttribute "aria-level").toBe "2"
                Vitest.expect(sub.getAttribute "aria-setsize").toBe "2"
                Vitest.expect(sub.getAttribute "aria-posinset").toBe "2"
                Vitest.expect(sub.getAttribute "aria-expanded").toBe "true"
        )

        Vitest.test (
            "sets aria-expanded only on directories that can expand",
            fun () ->
                RTL.render (FileExplorer.FileExplorer(initialItems = tree)) |> ignore

                Vitest.expect((treeItem "src").getAttribute "aria-expanded").toBe "false"
                Vitest.expect((treeItem "Lazy Folder").getAttribute "aria-expanded").toBe "false"
                Vitest.expect((treeItem "Empty Folder").hasAttribute "aria-expanded").toBe false
        )

        Vitest.test (
            "selects a file on click and routes it to onItemClick",
            fun () ->
                let clicked = ResizeArray<string>()

                RTL.render (
                    FileExplorer.FileExplorer(
                        initialItems = tree,
                        expandedIds = set [ "src" ],
                        onItemClick = (fun item -> clicked.Add item.Id)
                    )
                )
                |> ignore

                RTL.fireEvent.click (RTL.screen.getByText "a.txt")
                Vitest.expect((treeItem "a.txt").getAttribute "aria-selected").toBe "true"
                Vitest.expect(clicked.ToArray()).toEqual [| "a.txt" |]
        )

        Vitest.test (
            "keeps only the selected branch highlighted through collapse, expansion, and reselection",
            fun () ->
                RTL.render (ControlledTreeHarness()) |> ignore

                let isHighlighted (id: string) =
                    let row = treeItem id

                    match row.querySelector ("[data-file-item-id]") with
                    | null -> false
                    | element -> element.classList.contains "swt:bg-base-300"

                Vitest.expect(isHighlighted "studies").toBe true
                Vitest.expect(isHighlighted "selected-study").toBe true
                Vitest.expect(isHighlighted "resources").toBe true
                Vitest.expect(isHighlighted "selected-report.txt").toBe true
                Vitest.expect(isHighlighted "neighbor-study").toBe false
                Vitest.expect(isHighlighted "assays").toBe false

                RTL.fireEvent.click (RTL.screen.getByText "studies")

                Vitest.expect((RTL.screen.queryByText "selected-report.txt").IsNone).toBe true
                Vitest.expect(isHighlighted "studies").toBe true
                Vitest.expect(isHighlighted "assays").toBe false

                RTL.fireEvent.click (RTL.screen.getByText "studies")

                Vitest.expect((treeItem "selected-report.txt").getAttribute "aria-selected").toBe "true"
                Vitest.expect(isHighlighted "selected-study").toBe true
                Vitest.expect(isHighlighted "resources").toBe true
                Vitest.expect(isHighlighted "neighbor-study").toBe false

                RTL.fireEvent.click (RTL.screen.getByText "selected-assay")

                Vitest.expect(isHighlighted "assays").toBe true
                Vitest.expect(isHighlighted "selected-assay").toBe true
                Vitest.expect((treeItem "selected-assay").getAttribute "aria-selected").toBe "true"
                Vitest.expect(isHighlighted "isa.assay.xlsx").toBe false
                Vitest.expect(isHighlighted "studies").toBe false
                Vitest.expect(isHighlighted "neighbor-assay").toBe false
        )

        Vitest.test (
            "reports the new expanded ids when a directory row is clicked",
            fun () ->
                let changes = ResizeArray<Set<string>>()

                RTL.render (
                    FileExplorer.FileExplorer(
                        initialItems = tree,
                        expandedIds = set [ "src" ],
                        onExpandedIdsChange = (fun ids -> changes.Add ids)
                    )
                )
                |> ignore

                RTL.fireEvent.click (RTL.screen.getByText "sub")
                Vitest.expect(changes.Count).toBe 1
                Vitest.expect(changes.[0] |> Set.toArray).toEqual [| "src"; "sub" |]
        )

        Vitest.test (
            "offers Refresh folder for directories and calls the callback",
            fun () ->
                let refreshed = ResizeArray<string>()

                RTL.render (
                    FileExplorer.FileExplorer(
                        initialItems = tree,
                        onRefreshDirectory = (fun item -> refreshed.Add item.Id)
                    )
                )
                |> ignore

                RTL.fireEvent.custom (
                    "contextMenu",
                    RTL.screen.getByText "Empty Folder",
                    {| clientX = 10; clientY = 10 |}
                )

                RTL.fireEvent.click (RTL.screen.getByText "Refresh folder")
                Vitest.expect(refreshed.ToArray()).toEqual [| "Empty Folder" |]
        )

        Vitest.test (
            "puts aria-expanded on the treeitem only, not on the button inside it",
            fun () ->
                RTL.render (FileExplorer.FileExplorer(initialItems = tree, expandedIds = set [ "src" ]))
                |> ignore

                let marked = Browser.Dom.document.querySelectorAll "[aria-expanded]"
                Vitest.expect(marked.length > 0).toBe true

                for index in 0 .. marked.length - 1 do
                    Vitest.expect((marked.[index] :?> Browser.Types.HTMLElement).getAttribute "role").toBe "treeitem"
        )

        Vitest.test (
            "labels the tree with a default and a custom name",
            fun () ->
                RTL.render (FileExplorer.FileExplorer(initialItems = tree)) |> ignore

                RTL.screen.getByRole ("tree", ByRoleOptions(name = Text "File explorer"))
                |> ignore

                RTL.cleanup ()

                RTL.render (FileExplorer.FileExplorer(initialItems = tree, ariaLabel = "ARC files"))
                |> ignore

                RTL.screen.getByRole ("tree", ByRoleOptions(name = Text "ARC files")) |> ignore
        )

        Vitest.test (
            "moves the focus to the tree when the focused row leaves the rendered window",
            fun () -> promise {
                let files (names: string list) = names |> List.map file

                let initial =
                    files [
                        for index in 0..29 -> "row-" + (string index).PadLeft(2, '0')
                    ]

                let setItems = ref (fun (_: FileItem list) -> ())

                RTL.render (ItemsHarness(initial, (fun setter -> setItems.Value <- setter)))
                |> ignore

                RTL.fireEvent.custom ("focusIn", treeItem "row-02", {| |})

                // 40 rows come in above, so the focused row moves far below the first rendered rows.
                do!
                    RTL.act (fun () -> promise {
                        setItems.Value(
                            files [
                                for index in 0..39 -> "new-" + (string index).PadLeft(2, '0')
                            ]
                            @ initial
                        )
                    })

                Vitest.expect((RTL.screen.queryByText "row-02").IsNone).toBe true

                let tree = RTL.screen.getByRole ("tree", ByRoleOptions(name = Text "File explorer"))
                Vitest.expect(Browser.Dom.document.activeElement = (tree :> Browser.Types.Element)).toBe true
                Vitest.expect((RTL.screen.getAllByRole "treeitem").Count <= 13).toBe true
            }
        )

        Vitest.test (
            "moves the focus to the tree when a focusout without a target arrives before the row is removed",
            fun () -> promise {
                let files (names: string list) = names |> List.map file

                let initial =
                    files [
                        for index in 0..29 -> "row-" + (string index).PadLeft(2, '0')
                    ]

                let setItems = ref (fun (_: FileItem list) -> ())

                RTL.render (ItemsHarness(initial, (fun setter -> setItems.Value <- setter)))
                |> ignore

                let row = treeItem "row-02"
                RTL.fireEvent.custom ("focusIn", row, {| |})
                // Chromium may report the removal of the focused row as a focusout without a related target.
                RTL.fireEvent.custom ("focusOut", row, {| relatedTarget = null |})

                do!
                    RTL.act (fun () -> promise {
                        setItems.Value(
                            files [
                                for index in 0..39 -> "new-" + (string index).PadLeft(2, '0')
                            ]
                            @ initial
                        )
                    })

                let tree = RTL.screen.getByRole ("tree", ByRoleOptions(name = Text "File explorer"))
                Vitest.expect(Browser.Dom.document.activeElement = (tree :> Browser.Types.Element)).toBe true
            }
        )

        Vitest.test (
            "does not pull the focus into the tree after a click outside it",
            fun () -> promise {
                let files (names: string list) = names |> List.map file

                let initial =
                    files [
                        for index in 0..29 -> "row-" + (string index).PadLeft(2, '0')
                    ]

                let setItems = ref (fun (_: FileItem list) -> ())

                RTL.render (ItemsHarness(initial, (fun setter -> setItems.Value <- setter)))
                |> ignore

                let row = treeItem "row-02"
                RTL.fireEvent.custom ("focusIn", row, {| |})
                // A click on content outside the tree blurs without a target while the row stays mounted.
                RTL.fireEvent.custom ("focusOut", row, {| relatedTarget = null |})
                do! Promise.sleep 20

                do!
                    RTL.act (fun () -> promise {
                        setItems.Value(
                            files [
                                for index in 0..39 -> "new-" + (string index).PadLeft(2, '0')
                            ]
                            @ initial
                        )
                    })

                let tree = RTL.screen.getByRole ("tree", ByRoleOptions(name = Text "File explorer"))
                Vitest.expect(Browser.Dom.document.activeElement = (tree :> Browser.Types.Element)).toBe false
            }
        )
)
