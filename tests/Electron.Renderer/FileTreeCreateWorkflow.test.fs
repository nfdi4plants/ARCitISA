module ElectronRenderer.FileTreeCreateWorkflowTests

open Fable.Core
open Fable.Core.JsInterop
open ARCtrl
open Renderer.Components.LeftSidebar.FileExplorer
open Swate.Components.Shared
open Swate.Components.Page.FileExplorer.Types
open Swate.Electron.Shared.FileIOTypes
open Vitest

// The workflow module reaches Api.js, whose IPC bridges are absent without the preload. No test calls them.
Vitest.vi.mock ("./src/Electron/src/Renderer/Api.js", box (fun () -> createObj [ "ipcArcVaultApi" ==> createObj [] ]))
|> ignore

let rec private waitUntil (predicate: unit -> bool, attempts: int) = promise {
    if predicate () then
        return ()
    elif attempts <= 0 then
        failwith "Timed out waiting for create workflow."
    else
        do! Promise.sleep 1
        return! waitUntil (predicate, attempts - 1)
}

type private CreateProbe = {
    mutable Busy: bool
    mutable Errors: string list
    mutable Revealed: string list
    mutable Opened: FileItem list
    mutable Closed: bool
}

let private createProbe () = {
    Busy = false
    Errors = []
    Revealed = []
    Opened = []
    Closed = false
}

let private createConfig (probe: CreateProbe) : FileTreeCreateWorkflow.CreateConfig = {
    setIsCreating = fun busy -> probe.Busy <- busy
    applyError = fun message -> probe.Errors <- probe.Errors @ [ message ]
    revealInTree = fun paths -> probe.Revealed <- probe.Revealed @ paths
    openCreated = fun item -> probe.Opened <- probe.Opened @ [ item ]
    closeCreateModal = fun () -> probe.Closed <- true
}

let private waitUntilIdle (probe: CreateProbe) =
    // The workflow sets busy to true synchronously, so a false value means it has finished.
    waitUntil ((fun () -> not probe.Busy), 50)

let private createDraft () =
    match
        Renderer.Components.LeftSidebar.FileExplorer.Helper.tryBuildArcCreateDraft ArcFilesDiscriminate.Study "S1" [||]
    with
    | Ok draft -> draft
    | Error message -> failwith message

Vitest.describe (
    "FileTreeCreateWorkflow",
    fun () ->
        Vitest.test (
            "createArcEntry reveals the new path, opens the entity folder and closes the modal",
            fun () -> promise {
                let probe = createProbe ()
                let draft = createDraft ()

                FileTreeCreateWorkflow.createArcEntry (createConfig probe) (fun _ -> promise { return Ok() }) draft

                do! waitUntilIdle probe

                Vitest.expect(List.toArray probe.Revealed).toEqual ([| draft.Path |])
                Vitest.expect(probe.Opened.Length).toBe (1)
                Vitest.expect(probe.Opened.Head.Path).toEqual (Some "studies/S1")
                Vitest.expect(probe.Opened.Head.IsDirectory).toBe (true)
                Vitest.expect(probe.Closed).toBe (true)
                Vitest.expect(probe.Errors.Length).toBe (0)
                Vitest.expect(probe.Busy).toBe (false)
            }
        )

        Vitest.test (
            "createArcEntry reports the error and keeps the modal open when the create fails",
            fun () -> promise {
                let probe = createProbe ()

                FileTreeCreateWorkflow.createArcEntry
                    (createConfig probe)
                    (fun _ -> promise { return Error(exn "disk full") })
                    (createDraft ())

                do! waitUntilIdle probe

                Vitest.expect(List.toArray probe.Errors).toEqual ([| "disk full" |])
                Vitest.expect(probe.Opened.Length).toBe (0)
                Vitest.expect(probe.Closed).toBe (false)
                Vitest.expect(probe.Revealed.Length).toBe (0)
                Vitest.expect(probe.Busy).toBe (false)
            }
        )

        Vitest.test (
            "createFileSystemItem reveals and opens the path the create call returns",
            fun () -> promise {
                let probe = createProbe ()

                let request: CreateFileSystemItemRequest = {
                    parentPath = "assays/A"
                    name = "notes.txt"
                    kind = FileSystemItemKind.File
                }

                FileTreeCreateWorkflow.createFileSystemItem
                    (createConfig probe)
                    (fun _ -> promise { return Ok "assays/A/notes.txt" })
                    request

                do! waitUntilIdle probe

                Vitest.expect(List.toArray probe.Revealed).toEqual ([| "assays/A/notes.txt" |])
                Vitest.expect(probe.Opened.Length).toBe (1)
                Vitest.expect(probe.Opened.Head.Path).toEqual (Some "assays/A/notes.txt")
                Vitest.expect(probe.Opened.Head.IsDirectory).toBe (false)
                Vitest.expect(probe.Closed).toBe (true)
                Vitest.expect(probe.Errors.Length).toBe (0)
            }
        )

        Vitest.test (
            "createFileSystemItem opens a folder item for a created folder",
            fun () -> promise {
                let probe = createProbe ()

                let request: CreateFileSystemItemRequest = {
                    parentPath = "assays/A"
                    name = "data"
                    kind = FileSystemItemKind.Folder
                }

                FileTreeCreateWorkflow.createFileSystemItem
                    (createConfig probe)
                    (fun _ -> promise { return Ok "assays/A/data" })
                    request

                do! waitUntilIdle probe

                Vitest.expect(probe.Opened.Length).toBe (1)
                Vitest.expect(probe.Opened.Head.Path).toEqual (Some "assays/A/data")
                Vitest.expect(probe.Opened.Head.IsDirectory).toBe (true)
            }
        )

        Vitest.test (
            "createFileSystemItem reports one error when the create call rejects",
            fun () -> promise {
                let probe = createProbe ()

                let request: CreateFileSystemItemRequest = {
                    parentPath = "assays/A"
                    name = "notes.txt"
                    kind = FileSystemItemKind.File
                }

                FileTreeCreateWorkflow.createFileSystemItem
                    (createConfig probe)
                    (fun _ -> promise {
                        failwith "boom"
                        return Ok "assays/A/notes.txt"
                    })
                    request

                do! waitUntilIdle probe

                Vitest.expect(List.toArray probe.Errors).toEqual ([| "boom" |])
                Vitest.expect(probe.Opened.Length).toBe (0)
                Vitest.expect(probe.Closed).toBe (false)
                Vitest.expect(probe.Revealed.Length).toBe (0)
                Vitest.expect(probe.Busy).toBe (false)
            }
        )

        Vitest.test (
            "createDataMap reports the error and reveals nothing when the create fails",
            fun () -> promise {
                let probe = createProbe ()
                let parentInfo = DatamapParentInfo.create "A" DataMapParent.Assay

                FileTreeCreateWorkflow.createDataMap
                    (createConfig probe)
                    (fun _ -> promise { return Error(exn "denied") })
                    parentInfo

                do! waitUntilIdle probe

                Vitest.expect(List.toArray probe.Errors).toEqual ([| "denied" |])
                Vitest.expect(probe.Opened.Length).toBe (0)
                Vitest.expect(probe.Revealed.Length).toBe (0)
            }
        )

        Vitest.test (
            "createDataMap reveals and opens the new DataMap file",
            fun () -> promise {
                let probe = createProbe ()
                let parentInfo = DatamapParentInfo.create "A" DataMapParent.Assay

                FileTreeCreateWorkflow.createDataMap (createConfig probe) (fun _ -> promise { return Ok() }) parentInfo
                do! waitUntilIdle probe

                Vitest.expect(List.toArray probe.Revealed).toEqual ([| DatamapParentInfo.toPath parentInfo |])
                Vitest.expect(probe.Opened.Length).toBe (1)
                Vitest.expect(probe.Opened.Head.Path).toEqual (Some(DatamapParentInfo.toPath parentInfo))
                Vitest.expect(probe.Opened.Head.IsDirectory).toBe (false)
                Vitest.expect(probe.Errors.Length).toBe (0)
            }
        )
)
