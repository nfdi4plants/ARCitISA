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

let private push (version: int) (state: LargeObjectIndexStatus) (entries: ObjectStateDto[]) : LargeObjectIndexSnapshot = {|
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
        let pulled = push 0 LargeObjectIndexStatus.NotLoaded [||]

        RTL.render (
            LargeObjectStoreProviderWith(Some "/arc", (fun () -> promise { return Ok pulled }), Row "data/a.bin")
        )
        |> ignore

        do! RTL.act (fun () -> promise { return () })
        Vitest.expect(RTL.screen.queryByText "plain").toBeTruthy ()

        do! RTL.act (fun () -> promise { forwardToActiveStore (push 1 LargeObjectIndexStatus.Ready [| pointer |]) })
        Vitest.expect(RTL.screen.queryByText "pointer").toBeTruthy ()

        do!
            RTL.act (fun () -> promise {
                forwardToActiveStore (push 2 LargeObjectIndexStatus.Ready [| { pointer with IsMaterialized = true } |])
            })

        Vitest.expect(RTL.screen.queryByText "materialized").toBeTruthy ()
    }
)

[<ReactComponent>]
let private RootSwitcher
    (
        initialRoot: string option,
        pullFor: string -> JS.Promise<Result<LargeObjectIndexSnapshot, exn>>,
        expose: (string option -> unit) -> unit
    ) =
    let root, setRoot = React.useState initialRoot
    expose setRoot

    LargeObjectStoreProviderWith(root, (fun () -> pullFor (defaultArg root "")), Row "data/a.bin")

Vitest.test (
    "a root change clears the store and drops the pull result of the old root",
    fun () -> promise {
        let resolvers =
            System.Collections.Generic.Dictionary<string, Result<LargeObjectIndexSnapshot, exn> -> unit>()

        let pullFor (root: string) =
            Promise.create (fun resolve _ -> resolvers.[root] <- resolve)

        let setRoot = ref (fun (_: string option) -> ())

        RTL.render (RootSwitcher(Some "/a", pullFor, (fun setter -> setRoot.Value <- setter)))
        |> ignore

        do! RTL.act (fun () -> promise { return () })

        do! RTL.act (fun () -> promise { setRoot.Value(Some "/b") })
        do! RTL.act (fun () -> promise { resolvers.["/b"] (Ok(push 1 LargeObjectIndexStatus.Ready [| pointer |])) })
        Vitest.expect(RTL.screen.queryByText "pointer").toBeTruthy ()

        let materialized = { pointer with IsMaterialized = true }

        do!
            RTL.act (fun () -> promise {
                resolvers.["/a"] (Ok(push 9 LargeObjectIndexStatus.Ready [| materialized |]))
            })

        Vitest.expect(RTL.screen.queryByText "pointer").toBeTruthy ()

        do! RTL.act (fun () -> promise { setRoot.Value(Some "/c") })
        Vitest.expect(RTL.screen.queryByText "plain").toBeTruthy ()
    }
)
