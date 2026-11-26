module ElectronRenderer.FileTreeFileTargetWorkflowTests

open Renderer.Components.LeftSidebar.FileExplorer.FileTreeFileTargetWorkflow
open Arcitisa.Electron.Shared.FileIOHelper
open Arcitisa.Electron.Shared.FileIOTypes
open Vitest

let private createRequest path =
    FileContentDTO.create FileContentType.PlainText "content" path

let private createConfig
    pathExists
    (writeTarget: FileContentDTO -> Fable.Core.JS.Promise<unit>)
    (requestOverwrite: FileContentDTO -> unit)
    setBusy
    =
    {
        IsBusy = false
        PathExists = pathExists
        WriteTarget = writeTarget
        RequestOverwrite = requestOverwrite
        SetBusy = setBusy
    }

Vitest.describe (
    "FileTreeFileTargetWorkflow",
    fun () ->
        Vitest.test (
            "writes the file target when the target path is empty",
            fun () -> promise {
                let request = createRequest "notes\\draft.md"
                let checkedPaths = ResizeArray<string>()
                let writtenPaths = ResizeArray<string>()
                let busyStates = ResizeArray<bool>()

                let pathExists path = promise {
                    checkedPaths.Add path
                    return Ok false
                }

                let writeTarget (request: FileContentDTO) = promise {
                    writtenPaths.Add request.path
                    return ()
                }

                let config = createConfig pathExists writeTarget ignore busyStates.Add
                let! result = addFileTarget config request

                match result with
                | Ok() -> ()
                | Error exn -> failwith $"Expected file target write to succeed, got {exn.Message}."

                Vitest.expect(checkedPaths.ToArray()).toEqual ([| "notes/draft.md" |])
                Vitest.expect(writtenPaths.ToArray()).toEqual ([| "notes\\draft.md" |])
                Vitest.expect(busyStates.ToArray()).toEqual ([| true; false |])
            }
        )

        Vitest.test (
            "requests overwrite instead of writing when the target exists",
            fun () -> promise {
                let request = createRequest "notes/draft.md"
                let overwrittenPaths = ResizeArray<string>()
                let mutable didWrite = false

                let pathExists _ = promise { return Ok true }
                let writeTarget _ = promise { didWrite <- true }
                let requestOverwrite (request: FileContentDTO) = overwrittenPaths.Add request.path
                let config = createConfig pathExists writeTarget requestOverwrite ignore

                let! result = addFileTarget config request

                match result with
                | Ok() -> ()
                | Error exn -> failwith $"Expected overwrite request to succeed, got {exn.Message}."

                Vitest.expect(didWrite).toBe (false)
                Vitest.expect(overwrittenPaths.ToArray()).toEqual ([| "notes/draft.md" |])
            }
        )

        Vitest.test (
            "returns the path check error without writing",
            fun () -> promise {
                let expectedError = exn "path check failed"
                let mutable didWrite = false
                let mutable didRequestOverwrite = false

                let pathExists _ = promise { return Error expectedError }
                let writeTarget _ = promise { didWrite <- true }
                let requestOverwrite _ = didRequestOverwrite <- true
                let config = createConfig pathExists writeTarget requestOverwrite ignore

                let! result = addFileTarget config (createRequest "notes/draft.md")

                match result with
                | Error exn -> Vitest.expect(exn.Message).toBe "path check failed"
                | Ok() -> failwith "Expected path check error."

                Vitest.expect(didWrite).toBe (false)
                Vitest.expect(didRequestOverwrite).toBe (false)
            }
        )
)
