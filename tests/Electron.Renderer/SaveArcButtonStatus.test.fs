module ElectronRenderer.SaveArcButtonStatusTests

open Fable.Core.JsInterop
open Renderer.Components
open Vitest

// The status lives in the navbar module, which reaches the IPC bridges through the Api module. The preload is absent
// in tests, so the bridges are replaced by empty objects that no test calls.
Vitest.vi.mock (
    "./src/Electron/src/Renderer/Api.js",
    box (fun () ->
        createObj [
            "ipcGitLabApi" ==> createObj []
            "ipcVersionControlApi" ==> createObj []
            "ipcArcVaultApi" ==> createObj []
            "ipcAuthApi" ==> createObj []
            "ipcAppVersionApi" ==> createObj []
            "ipcUiSettingsApi" ==> createObj []
            "ipcUserDefaultsApi" ==> createObj []
            "ipcTemplateApi" ==> createObj []
            "ipcValidationPackageApi" ==> createObj []
        ]
    )
)
|> ignore

Vitest.test (
    "unsaved changes can be saved while nothing runs",
    fun () ->
        let status = SaveArcButtonStatus.create false false true

        Vitest.expect(status).toEqual SaveArcButtonStatus.Ready
        Vitest.expect(SaveArcButtonStatus.canSave status).toBe true
        Vitest.expect(SaveArcButtonStatus.isBusy status).toBe false
        Vitest.expect(SaveArcButtonStatus.title status).toBe "Save ARC"
)

Vitest.test (
    "without unsaved changes there is nothing to save",
    fun () ->
        let status = SaveArcButtonStatus.create false false false

        Vitest.expect(status).toEqual SaveArcButtonStatus.NothingToSave
        Vitest.expect(SaveArcButtonStatus.canSave status).toBe false
        Vitest.expect(SaveArcButtonStatus.isBusy status).toBe false
)

Vitest.test (
    "a load of external changes blocks saving unsaved changes and says why",
    fun () ->
        let status = SaveArcButtonStatus.create false true true

        Vitest.expect(status).toEqual SaveArcButtonStatus.LoadingExternalChanges
        Vitest.expect(SaveArcButtonStatus.canSave status).toBe false
        Vitest.expect(SaveArcButtonStatus.isBusy status).toBe true
        Vitest.expect(SaveArcButtonStatus.title status).toBe "Loading external changes…"
)

Vitest.test (
    "a load shows even without unsaved changes",
    fun () ->
        Vitest.expect(SaveArcButtonStatus.create false true false).toEqual SaveArcButtonStatus.LoadingExternalChanges
)

Vitest.test (
    "a running save blocks a second save and wins over a load that starts meanwhile",
    fun () ->
        let status = SaveArcButtonStatus.create true true true

        Vitest.expect(status).toEqual SaveArcButtonStatus.Saving
        Vitest.expect(SaveArcButtonStatus.canSave status).toBe false
        Vitest.expect(SaveArcButtonStatus.isBusy status).toBe true
        Vitest.expect(SaveArcButtonStatus.title status).toBe "Saving ARC…"
)
