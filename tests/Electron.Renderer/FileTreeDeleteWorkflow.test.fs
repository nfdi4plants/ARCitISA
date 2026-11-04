module ElectronRenderer.FileTreeDeleteWorkflowTests

open Fable.Core
open Renderer.Components.LeftSidebar.FileExplorer
open Swate.Components.Page.FileExplorer.Types
open Swate.Components.Primitive.ErrorModal.Types
open Vitest

let rec private waitUntil (predicate: unit -> bool, attempts: int) = promise {
    if predicate () then
        return ()
    elif attempts <= 0 then
        failwith "Timed out waiting for delete workflow."
    else
        do! Promise.sleep 1
        return! waitUntil (predicate, attempts - 1)
}

let private createFileItem (name: string) (path: string) = {
    FileTree.createFile name (Some path) FileItemIcon.Document with
        Id = path
}

type private DeleteWorkflowProbe = {
    mutable ActivePaths: string list
    mutable DeletedPaths: string list
    mutable Errors: ErrorModalRequest list
    mutable Closed: bool
}

let private createProbe () = {
    ActivePaths = []
    DeletedPaths = []
    Errors = []
    Closed = false
}

let private createConfig (item: FileItem) (probe: DeleteWorkflowProbe) : FileTreeDeleteWorkflow.ConfirmDeleteConfig = {
    pendingDeleteItem = Some item
    closeDeleteModal = fun () -> probe.Closed <- true
    setIsDeleting = ignore
    enqueueError = fun request -> probe.Errors <- probe.Errors @ [ request ]
    getLfsActivePaths = fun () -> Renderer.Context.LfsActivityContext.LfsActivePathIndex.create probe.ActivePaths
    deletePath =
        fun path -> promise {
            probe.DeletedPaths <- probe.DeletedPaths @ [ path ]
            return Ok()
        }
}

Vitest.describe (
    "FileTreeDeleteWorkflow",
    fun () ->
        Vitest.test (
            "confirmDeleteItem skips the delete and reports an error when an LFS action started after the modal opened",
            fun () ->
                let workbook = createFileItem "isa.assay.xlsx" "assays/A/isa.assay.xlsx"
                let probe = createProbe ()
                let config = createConfig workbook probe

                // The modal opened while nothing ran. A Download starts before the user confirms.
                probe.ActivePaths <- [ "assays/A/dataset/big.bin" ]

                FileTreeDeleteWorkflow.confirmDeleteItem config

                Vitest.expect(probe.DeletedPaths.Length).toBe (0)
                Vitest.expect(probe.Errors.Length).toBe (1)
                Vitest.expect(probe.Closed).toBe (true)
        )

        Vitest.test (
            "confirmDeleteItem deletes the item when no LFS action runs inside its delete scope",
            fun () -> promise {
                let workbook = createFileItem "isa.assay.xlsx" "assays/A/isa.assay.xlsx"
                let probe = createProbe ()
                probe.ActivePaths <- [ "assays/B/dataset/big.bin" ]

                FileTreeDeleteWorkflow.confirmDeleteItem (createConfig workbook probe)
                do! waitUntil ((fun () -> probe.Closed), 50)

                Vitest.expect(List.toArray probe.DeletedPaths).toEqual ([| "assays/A/isa.assay.xlsx" |])
                Vitest.expect(probe.Errors.Length).toBe (0)
            }
        )
)
