module ElectronRenderer.LargeObjectStoreRtlTests

open Fable.Core
open Feliz
open Renderer.Context.LargeObjectStore
open Swate.Electron.Shared.IPCTypes
open Swate.Electron.Shared.VersionControlTypes
open Vitest

Vitest.afterEach (fun () -> RTL.cleanup ())

let private pointer: ObjectStateDto = {
    Path = "data/a.bin"
    IsMaterialized = false
    IsLocallyAvailable = false
    SizeBytes = Some 10.0
    ObjectId = None
}

let private push (version: int) (state: string) (entries: ObjectStateDto[]) : LargeObjectIndexSnapshot = {|
    version = version
    state = state
    caseInsensitive = false
    entries = entries
|}

[<ReactComponent>]
let private Row (path: string) =
    let largeObject = useLargeObject path

    Html.div [
        prop.text (
            match largeObject with
            | None -> "plain"
            | Some state when state.IsMaterialized -> "materialized"
            | Some _ -> "pointer"
        )
    ]

Vitest.test (
    "a row shows the LFS state after a largeObjectIndexUpdate push without any file tree update",
    fun () -> promise {
        let pulled = push 0 "notLoaded" [||]

        RTL.render (
            LargeObjectStoreProviderWith(Some "/arc", (fun () -> promise { return Ok pulled }), Row "data/a.bin")
        )
        |> ignore

        do! RTL.act (fun () -> promise { return () })
        Vitest.expect(RTL.screen.queryByText "plain").toBeTruthy ()

        do! RTL.act (fun () -> promise { forwardToActiveStore (push 1 "ready" [| pointer |]) })
        Vitest.expect(RTL.screen.queryByText "pointer").toBeTruthy ()

        do!
            RTL.act (fun () -> promise {
                forwardToActiveStore (push 2 "ready" [| { pointer with IsMaterialized = true } |])
            })

        Vitest.expect(RTL.screen.queryByText "materialized").toBeTruthy ()
    }
)
