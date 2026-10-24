module ElectronRenderer.FileExplorerStateReconciliationRtlTests

open Feliz
open Renderer.Components.Helper
open Renderer.Types
open Swate.Electron.Shared.FileIOTypes
open Swate.Electron.Shared.VersionControlTypes
open Vitest

Vitest.afterEach (fun () -> RTL.cleanup ())

type private HarnessProps = {
    Lookup: string -> ObjectStateDto option
    FileTree: FileEntry[]
    Selection: string
}

let private selectedPath = "data/note.md"
let private fileTree = [| FileEntry.create ("note.md", selectedPath, false) |]

let private lfsState (isMaterialized: bool) : ObjectStateDto = {
    Path = selectedPath
    IsMaterialized = isMaterialized
    IsLocallyAvailable = isMaterialized
    SizeBytes = Some 10.0
    ObjectId = None
}

[<ReactComponent>]
let private Harness
    (initial: HarnessProps, reloads: ResizeArray<string>, clears: int ref, expose: (HarnessProps -> unit) -> unit)
    =
    let props, setProps = React.useState initial
    expose setProps

    FileExplorerStateReconciliation.useReconcileSelectedPreview
        props.Lookup
        props.FileTree
        (Some props.Selection)
        (Some PageState.UnknownPage)
        (fun path _ -> reloads.Add path)
        (fun () -> clears.Value <- clears.Value + 1)

    Html.none

Vitest.test (
    "the selected preview reloads only when the tree or the LFS state of the selected file changes",
    fun () -> promise {
        let reloads = ResizeArray<string>()
        let clears = ref 0
        let setProps = ref (fun (_: HarnessProps) -> ())

        RTL.render (
            Harness(
                {
                    Lookup = fun _ -> None
                    FileTree = fileTree
                    Selection = selectedPath
                },
                reloads,
                clears,
                (fun setter -> setProps.Value <- setter)
            )
        )
        |> ignore

        // A new lookup that answers the same for the selected file, like the one after an unrelated push.
        do!
            RTL.act (fun () -> promise {
                setProps.Value {
                    Lookup = fun _ -> None
                    FileTree = fileTree
                    Selection = selectedPath
                }
            })

        Vitest.expect(reloads.Count).toBe 0
        Vitest.expect(clears.Value).toBe 0

        do!
            RTL.act (fun () -> promise {
                setProps.Value {
                    Lookup = fun path -> if path = selectedPath then Some(lfsState false) else None
                    FileTree = fileTree
                    Selection = selectedPath
                }
            })

        Vitest.expect(clears.Value).toBe 1
        Vitest.expect(reloads.Count).toBe 0

        do!
            RTL.act (fun () -> promise {
                setProps.Value {
                    Lookup = fun path -> if path = selectedPath then Some(lfsState true) else None
                    FileTree = fileTree
                    Selection = selectedPath
                }
            })

        Vitest.expect(reloads.Count).toBe 1
        Vitest.expect(reloads.[0]).toBe selectedPath

        // Selecting another file changes the key, but the click opens that file itself.
        do!
            RTL.act (fun () -> promise {
                setProps.Value {
                    Lookup = fun _ -> None
                    FileTree = fileTree
                    Selection = "data/other.bin"
                }
            })

        Vitest.expect(reloads.Count).toBe 1
        Vitest.expect(clears.Value).toBe 1
    }
)
