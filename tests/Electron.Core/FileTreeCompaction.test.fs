module ElectronCore.FileTreeCompactionTests

open System.Collections.Generic
module FileTreeCreator = Main.FileTreeCreator

open Swate.Electron.Shared.FileIOHelper
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.VersionControlTypes
open Swate.Components.Shared
open Vitest

let private fileNode (name: string) (path: string) =
    FileTreeNode.create (name, false, path, Dictionary())

let private directoryNode (name: string) (path: string) (children: FileTreeNode list) =
    let childrenByName = Dictionary<string, FileTreeNode>()

    children |> List.iter (fun child -> childrenByName.[child.name] <- child)

    FileTreeNode.create (name, true, path, childrenByName)

let private onlyChild (node: FileTreeNode) =
    if node.children.Count <> 1 then
        failwith $"Expected exactly one child on '{node.path}', but found {node.children.Count}."

    node.children.Values |> Seq.exactlyOne

Vitest.describe (
    "FileIOHelper.collapseSingleChildSameNameDirectories",
    fun () ->
        Vitest.test (
            "collapses A/A single-child same-name directories",
            fun () ->
                let leaf = fileNode "leaf.txt" "arc/A/A/leaf.txt"
                let innerA = directoryNode "A" "arc/A/A" [ leaf ]
                let outerA = directoryNode "A" "arc/A" [ innerA ]
                let root = directoryNode "arc" "arc" [ outerA ]

                let collapsed = collapseSingleChildSameName root
                let mergedA = onlyChild collapsed
                let mergedLeaf = onlyChild mergedA

                Vitest.expect(mergedA.path).toBe ("arc/A/A")
                Vitest.expect(mergedA.name).toBe ("A")
                Vitest.expect(mergedLeaf.path).toBe ("arc/A/A/leaf.txt")
        )

        Vitest.test (
            "collapses repeated A/A/A chains recursively",
            fun () ->
                let leaf = fileNode "leaf.txt" "arc/A/A/A/leaf.txt"
                let level3 = directoryNode "A" "arc/A/A/A" [ leaf ]
                let level2 = directoryNode "A" "arc/A/A" [ level3 ]
                let level1 = directoryNode "A" "arc/A" [ level2 ]
                let root = directoryNode "arc" "arc" [ level1 ]

                let collapsed = collapseSingleChildSameName root
                let mergedA = onlyChild collapsed

                Vitest.expect(mergedA.path).toBe ("arc/A/A/A")
                Vitest.expect(mergedA.children.Count).toBe (1)
        )

        Vitest.test (
            "does not collapse when same-name child folder has siblings/files",
            fun () ->
                let sameNameChild = directoryNode "A" "arc/A/A" []
                let siblingFile = fileNode "notes.txt" "arc/A/notes.txt"
                let outerA = directoryNode "A" "arc/A" [ sameNameChild; siblingFile ]
                let root = directoryNode "arc" "arc" [ outerA ]

                let collapsed = collapseSingleChildSameName root
                let topA = onlyChild collapsed
                let childPaths = topA.children.Values |> Seq.map _.path |> Seq.sort |> Seq.toList

                Vitest.expect(topA.path).toBe ("arc/A")
                Vitest.expect(childPaths).toEqual ([ "arc/A/A"; "arc/A/notes.txt" ])
        )

        Vitest.test (
            "does not collapse when only child has a different name",
            fun () ->
                let innerB = directoryNode "B" "arc/A/B" []
                let outerA = directoryNode "A" "arc/A" [ innerB ]
                let root = directoryNode "arc" "arc" [ outerA ]

                let collapsed = collapseSingleChildSameName root
                let topA = onlyChild collapsed
                let childB = onlyChild topA

                Vitest.expect(topA.path).toBe ("arc/A")
                Vitest.expect(topA.name).toBe ("A")
                Vitest.expect(childB.path).toBe ("arc/A/B")
                Vitest.expect(childB.name).toBe ("B")
        )

        Vitest.test (
            "preserves deepest path/id for interactions and compares names case-insensitively",
            fun () ->
                let leaf = fileNode "leaf.txt" "arc/Data/data/leaf.txt"
                let innerData = directoryNode "data" "arc/Data/data" [ leaf ]
                let outerData = directoryNode "Data" "arc/Data" [ innerData ]
                let root = directoryNode "arc" "arc" [ outerData ]

                let collapsed = collapseSingleChildSameName root
                let mergedData = onlyChild collapsed

                Vitest.expect(mergedData.path).toBe ("arc/Data/data")
                Vitest.expect(mergedData.path = "arc/Data").toBe (false)
        )
)

Vitest.describe (
    "FileIOHelper.toFileTreeNode LFS metadata",
    fun () ->
        Vitest.test (
            "preserves Git LFS ls-files metadata from FileEntry to root FileTreeNode",
            fun () ->
                let largeObject: ObjectStateDto = {
                    Path = "arc/sample.bin"
                    SizeBytes = Some 2048.0
                    IsMaterialized = false
                    IsLocallyAvailable = false
                    ObjectId = Some "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                }

                let rootEntry: FileEntry = {
                    name = "arc"
                    isDirectory = true
                    path = "C:/arc"
                    largeObject = Some largeObject
                }

                let rootNode = toFileTreeNode [| rootEntry |]

                Vitest.expect(rootNode.largeObject).toEqual (Some largeObject)
        )

        Vitest.test (
            "keeps root-level generic files and folders as root children",
            fun () ->
                let entries = [|
                    FileEntry.create ("arc", "", true)
                    FileEntry.create ("studies", "studies", true)
                    FileEntry.create ("docs", "docs", true)
                    FileEntry.create ("notes.txt", "notes.txt", false)
                |]

                let rootNode = toFileTreeNode entries

                Vitest.expect(rootNode.children.ContainsKey("studies")).toBe (true)
                Vitest.expect(rootNode.children.ContainsKey("docs")).toBe (true)
                Vitest.expect(rootNode.children.ContainsKey("notes.txt")).toBe (true)
                Vitest.expect(rootNode.children.["docs"].path).toBe ("docs")
                Vitest.expect(rootNode.children.["notes.txt"].path).toBe ("notes.txt")
        )
)

Vitest.describe (
    "FileIOHelper.createAvailableArcEntityTargets",
    fun () ->
        Vitest.test (
            "lists only solid study and assay entities with canonical ARC files",
            fun () ->
                let targets =
                    createAvailableArcEntityTargets [
                        (ARCtrl.ArcPathHelper.StudiesFolderName,
                         ARCtrl.ArcPathHelper.StudyFileName,
                         fun name -> {
                             Name = name
                             Kind = NotesTargetKind.Study
                         })
                        (ARCtrl.ArcPathHelper.AssaysFolderName,
                         ARCtrl.ArcPathHelper.AssayFileName,
                         fun name -> {
                             Name = name
                             Kind = NotesTargetKind.Assay
                         })
                    ] [
                        FileEntry.create ("arc", "", true)
                        FileEntry.create ("studies", "studies", true)
                        FileEntry.create (".gitkeep", "studies/.gitkeep", false)
                        FileEntry.create ("loose.txt", "studies/loose.txt", false)
                        FileEntry.create ("StudyA", "studies/StudyA", true)
                        FileEntry.create ("isa.study.xlsx", "studies/StudyA/isa.study.xlsx", false)
                        FileEntry.create ("protocol.md", "studies/StudyA/protocols/protocol.md", false)
                        FileEntry.create ("DraftOnly", "studies/DraftOnly", true)
                        FileEntry.create ("AssayA", "assays/AssayA", true)
                        FileEntry.create ("isa.assay.xlsx", "assays/AssayA/isa.assay.xlsx", false)
                        FileEntry.create ("NoCanonicalAssay", "assays/NoCanonicalAssay", true)
                    ]
                    |> Seq.map (fun target -> target.Kind, target.Name)
                    |> Seq.toArray

                Vitest
                    .expect(targets)
                    .toEqual (
                        [|
                            NotesTargetKind.Study, "StudyA"
                            NotesTargetKind.Assay, "AssayA"
                        |]
                    )
        )

        Vitest.test (
            "lists entities from caller supplied canonical file rules",
            fun () ->
                let targets =
                    createAvailableArcEntityTargets [
                        ("workflows", "isa.workflow.xlsx", fun name -> "workflow", name)
                        ("runs", "isa.run.xlsx", fun name -> "run", name)
                    ] [
                        FileEntry.create ("arc", "", true)
                        FileEntry.create ("WorkflowA", "workflows/WorkflowA", true)
                        FileEntry.create ("isa.workflow.xlsx", "workflows/WorkflowA/isa.workflow.xlsx", false)
                        FileEntry.create ("draft.md", "workflows/DraftOnly/draft.md", false)
                        FileEntry.create ("RunA", "runs/RunA", true)
                        FileEntry.create ("isa.run.xlsx", "runs/RunA/isa.run.xlsx", false)
                    ]
                    |> Seq.toArray

                Vitest.expect(targets).toEqual ([| "workflow", "WorkflowA"; "run", "RunA" |])
        )
)
