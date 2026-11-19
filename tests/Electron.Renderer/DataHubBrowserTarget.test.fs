module ElectronRenderer.DataHubBrowserTargetTests

open Fable.Core.JsInterop
open Renderer.Components.MainContent.DataHubBrowserTarget
open Renderer.Context.GitWorkflow
open Swate.Components.Api.GitLabApi
open Vitest

// Importing the target pulls in the IPC bridges, which only exist inside Electron.
Vitest.vi.mock (
    "./src/Electron/src/Renderer/Api.js",
    box (fun () -> createObj [ "ipcVersionControlApi" ==> createObj [] ])
)
|> ignore

let private project: ExploreProjectDto = {
    id = 123
    name = "my-arc"
    path_with_namespace = "carol/my-arc"
    name_with_namespace = "carol / my-arc"
    description = None
    web_url = "https://gitlab.example/carol/my-arc"
    http_url_to_repo = "https://gitlab.example/carol/my-arc.git"
    ssh_url_to_repo = None
    avatar_url = None
    visibility = Some "private"
    star_count = 0
    created_at = None
    last_activity_at = None
    tag_list = [||]
    ``namespace`` = {
        id = 5
        name = "carol"
        kind = "user"
        full_path = "carol"
    }
}

Vitest.describe (
    "DataHubBrowserTarget clone request",
    fun () ->
        Vitest.test (
            "uses the saved download default when another ARC is open with a different session value",
            fun () ->
                let openArcState = {
                    GitState.Empty with
                        CurrentArcPath = Some "C:/arc"
                        DownloadLargeFiles = true
                        UserDefaults = {
                            builtInUserDefaults with
                                DownloadLargeFiles = false
                        }
                }

                let request =
                    DataHubBrowserHelper.toCloneRequest openArcState "clone-op" project "C:/clone-target"

                Vitest.expect(request.MaterializeAllObjects).toBe (false)
                Vitest.expect(request.ProviderLocation).toBe (project.http_url_to_repo)
                Vitest.expect(request.TargetPath).toBe ("C:/clone-target")
        )
)
