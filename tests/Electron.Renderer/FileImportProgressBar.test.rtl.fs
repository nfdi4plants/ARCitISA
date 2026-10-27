module ElectronRenderer.FileImportProgressBarTests

open Browser.Types
open Feliz
open Renderer.Components.LeftSidebar.FileImportStatus
open Swate.Electron.Shared.FileIOTypes
open Vitest

Vitest.afterEach (fun () -> RTL.cleanup ())

let private importProgress step completed : FileImportProgress = {
    step = step
    completedItems = completed
    totalItems = 4
}

[<ReactComponent>]
let private Harness (initial: FileImportProgress, expose: (FileImportProgress -> unit) -> unit) =
    let state, setState = React.useState initial
    expose setState
    FileImportProgressBar state

let private expectProgress fraction =
    let progress =
        RTL.screen.getByRole ("progressbar", ByRoleOptions(name = Text "Import progress"))
        |> unbox<HTMLProgressElement>

    Vitest.expect(progress.value / progress.max).toBe fraction

Vitest.test (
    "the import progress bar keeps its overall progress when copying finishes and publishing starts",
    fun () -> promise {
        let setState = ref (fun (_: FileImportProgress) -> ())

        RTL.render (Harness(importProgress FileImportStep.Copying 3, (fun setter -> setState.Value <- setter)))
        |> ignore

        expectProgress 0.375

        for step, completed, expected in
            [
                FileImportStep.Copying, 4, 0.5
                FileImportStep.Publishing, 0, 0.5
                FileImportStep.Publishing, 1, 0.625
                FileImportStep.Publishing, 4, 1.0
            ] do
            do! RTL.act (fun () -> promise { setState.Value(importProgress step completed) })

            expectProgress expected

    }
)
