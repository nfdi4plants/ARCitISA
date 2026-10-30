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
                    folder
                        "protocols"
                        (Some [
                            folder
                                "sample-preparation"
                                (Some [
                                    file "sample-preparation.md"
                                    file "sample-preparation.cwl"
                                ])
                        ])
                    folder
                        "resources"
                        (Some [
                            folder
                                "raw-data"
                                (Some [
                                    folder
                                        "batch-01"
                                        (Some [
                                            folder
                                                "replicate-01"
                                                (Some [ file "selected-report.txt"; file "sibling-report.txt" ])
                                        ])
                                    folder "batch-02" (Some [ file "other-report.txt" ])
                                ])
                            folder "derived-data" (Some [ file "summary.csv" ])
                        ])
                    folder
                        "study-assays"
                        (Some [
                            folder "rna-seq" (Some [ file "isa.assay.xlsx"; file "reads.fastq.gz" ])
                        ])
                ])
            folder
                "neighbor-study"
                (Some [
                    file "isa.study.xlsx - neighbor"
                    file "neighbor-study.xlsx"
                ])
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

let private flushRender () =
    RTL.act (fun () -> promise { return () })

[<ReactComponent>]
let private ItemsHarness (initial: FileItem list, expose: (FileItem list -> unit) -> unit) =
    let items, setItems = React.useState initial
    expose setItems
    FileExplorer.FileExplorer(initialItems = items)

[<ReactComponent>]
let private ControlledTreeHarness () =
    let items = realisticTree

    let expandedIds, setExpandedIds =
        React.useState (
            set [
                "studies"
                "selected-study"
                "protocols"
                "resources"
                "raw-data"
                "batch-01"
                "replicate-01"
                "study-assays"
                "rna-seq"
                "assays"
            ]
        )

    let selectedId, setSelectedId = React.useState (Some "selected-report.txt")

    let materializedIds =
        selectedId
        |> Option.bind (fun id -> FileTree.getPath id items [])
        |> Option.defaultValue []
        |> List.filter _.IsDirectory
        |> List.map _.Id
        |> Set.ofList
        |> Set.union expandedIds

    let materializedItems =
        React.useMemo (
            (fun () ->
                let rec materialize treeItems =
                    treeItems
                    |> List.map (fun item ->
                        match item.Children with
                        | Some children when materializedIds.Contains item.Id -> {
                            item with
                                Children = Some(materialize children)
                          }
                        | Some [] -> item
                        | Some _ -> { item with Children = None }
                        | None -> item
                    )

                materialize items
            ),
            [| box items; box materializedIds |]
        )

    FileExplorer.FileExplorer(
        initialItems = materializedItems,
        selectedItemId = selectedId,
        expandedIds = expandedIds,
        onExpandedIdsChange = setExpandedIds,
        onItemClick =
            (fun item ->
                if
                    not item.IsDirectory
                    || item.Id = "studies"
                    || item.Id = "selected-assay"
                    || item.Id = "neighbor-study"
                then
                    setSelectedId (Some item.Id)
            )
    )

let private isHighlighted (id: string) =
    let row = treeItem id

    match row.querySelector ("[data-file-item-id]") with
    | null -> false
    | element -> element.classList.contains "swt:bg-base-300"

let private expectVisibleState (selectedId: string option) (highlightedIds: Set<string>) (visibleIds: string list) =
    for id in visibleIds do
        Vitest.expect((treeItem id).getAttribute "aria-selected").toBe (string (selectedId = Some id))
        Vitest.expect(isHighlighted id).toBe (highlightedIds.Contains id)

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
            "routes directory clicks while expanding and collapsing",
            fun () -> promise {
                let clicked = ResizeArray<string>()

                RTL.render (
                    FileExplorer.FileExplorer(initialItems = tree, onItemClick = (fun item -> clicked.Add item.Id))
                )
                |> ignore

                RTL.fireEvent.click (RTL.screen.getByText "src")
                do! flushRender ()
                Vitest.expect((treeItem "src").getAttribute "aria-selected").toBe "true"
                Vitest.expect((treeItem "src").getAttribute "aria-expanded").toBe "true"

                RTL.fireEvent.click (RTL.screen.getByText "src")
                do! flushRender ()
                Vitest.expect((treeItem "src").getAttribute "aria-selected").toBe "true"
                Vitest.expect((treeItem "src").getAttribute "aria-expanded").toBe "false"
                Vitest.expect(clicked.ToArray()).toEqual [| "src"; "src" |]
            }
        )

        Vitest.test (
            "keeps only the selected branch highlighted through collapse, expansion, and reselection",
            fun () -> promise {
                RTL.render (ControlledTreeHarness()) |> ignore

                let visibleSelectedBranch = [
                    "studies"
                    "selected-study"
                    "isa.study.xlsx"
                    "protocols"
                    "sample-preparation"
                    "resources"
                    "raw-data"
                    "batch-01"
                    "replicate-01"
                    "selected-report.txt"
                    "sibling-report.txt"
                    "batch-02"
                ]

                expectVisibleState
                    (Some "selected-report.txt")
                    (set [
                        "studies"
                        "selected-study"
                        "resources"
                        "raw-data"
                        "batch-01"
                        "replicate-01"
                        "selected-report.txt"
                    ])
                    visibleSelectedBranch

                RTL.fireEvent.click (RTL.screen.getByText "batch-01")
                do! flushRender ()

                Vitest.expect((RTL.screen.queryByText "selected-report.txt").IsNone).toBe true
                Vitest.expect((RTL.screen.queryByText "replicate-01").IsNone).toBe true

                Vitest.expect((treeItem "batch-01").getAttribute "aria-expanded").toBe "false"

                expectVisibleState
                    None
                    (set [
                        "studies"
                        "selected-study"
                        "resources"
                        "raw-data"
                        "batch-01"
                    ])
                    [
                        "studies"
                        "selected-study"
                        "isa.study.xlsx"
                        "protocols"
                        "sample-preparation"
                        "resources"
                        "raw-data"
                        "batch-01"
                        "batch-02"
                    ]

                RTL.fireEvent.click (RTL.screen.getByText "batch-01")
                do! flushRender ()

                Vitest.expect((treeItem "batch-01").getAttribute "aria-expanded").toBe "true"
                Vitest.expect((RTL.screen.queryByText "selected-report.txt").IsSome).toBe true

                expectVisibleState
                    (Some "selected-report.txt")
                    (set [
                        "studies"
                        "selected-study"
                        "resources"
                        "raw-data"
                        "batch-01"
                        "replicate-01"
                        "selected-report.txt"
                    ])
                    visibleSelectedBranch

                RTL.fireEvent.click (RTL.screen.getByText "isa.study.xlsx")
                do! flushRender ()

                expectVisibleState
                    (Some "isa.study.xlsx")
                    (set [ "studies"; "selected-study"; "isa.study.xlsx" ])
                    visibleSelectedBranch

                RTL.fireEvent.click (RTL.screen.getByText "selected-report.txt")
                do! flushRender ()

                expectVisibleState
                    (Some "selected-report.txt")
                    (set [
                        "studies"
                        "selected-study"
                        "resources"
                        "raw-data"
                        "batch-01"
                        "replicate-01"
                        "selected-report.txt"
                    ])
                    visibleSelectedBranch

                RTL.fireEvent.click (RTL.screen.getByText "studies")
                do! flushRender ()

                Vitest.expect((RTL.screen.queryByText "selected-report.txt").IsNone).toBe true
                Vitest.expect((RTL.screen.queryByText "selected-study").IsNone).toBe true

                expectVisibleState (Some "studies") (set [ "studies" ]) [
                    "studies"
                    "assays"
                    "selected-assay"
                    "neighbor-assay"
                    "README.md"
                ]

                RTL.fireEvent.click (RTL.screen.getByText "studies")
                do! flushRender ()

                expectVisibleState (Some "studies") (set [ "studies" ]) visibleSelectedBranch
            }
        )

        Vitest.test (
            "selects a deeply nested ISA study file on its own path",
            fun () -> promise {
                RTL.render (ControlledTreeHarness()) |> ignore

                RTL.fireEvent.click (RTL.screen.getByText "isa.study.xlsx")
                do! flushRender ()

                expectVisibleState (Some "isa.study.xlsx") (set [ "studies"; "selected-study"; "isa.study.xlsx" ]) [
                    "studies"
                    "selected-study"
                    "isa.study.xlsx"
                    "protocols"
                    "sample-preparation"
                    "resources"
                    "raw-data"
                    "batch-01"
                    "replicate-01"
                    "selected-report.txt"
                    "sibling-report.txt"
                    "batch-02"
                ]

            }
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
