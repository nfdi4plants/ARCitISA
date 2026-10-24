module ElectronRenderer.FileWatcherNoticeRtlTests

open Feliz
open Renderer.Components.LeftSidebar.FileExplorer.FileWatcherNotice
open Swate.Electron.Shared.IPCTypes
open Vitest

Vitest.afterEach (fun () -> RTL.cleanup ())

[<ReactComponent>]
let private Harness (initial: FileWatcherStateDto, expose: (FileWatcherStateDto -> unit) -> unit) =
    let state, setState = React.useState initial
    expose setState
    FileWatcherNotice state

Vitest.test (
    "the notice shows while watching is unavailable and disappears when it runs again",
    fun () -> promise {
        let setState = ref (fun (_: FileWatcherStateDto) -> ())

        RTL.render (
            Harness(
                {|
                    state = FileWatcherStatus.Unavailable
                    reason = "ENOSPC: System limit for number of file watchers reached"
                |},
                (fun setter -> setState.Value <- setter)
            )
        )
        |> ignore

        let notice = RTL.screen.getByTestId "file-watcher-unavailable"
        Vitest.expect(notice.textContent.Contains "File watching is unavailable").toBe true
        Vitest.expect(notice.textContent.Contains "Refresh folder").toBe true
        Vitest.expect(notice.textContent.Contains "fs.inotify.max_user_watches").toBe true

        do!
            RTL.act (fun () -> promise {
                setState.Value {|
                    state = FileWatcherStatus.Running
                    reason = ""
                |}
            })

        Vitest.expect((RTL.screen.queryByTestId "file-watcher-unavailable").IsNone).toBe true
    }
)

Vitest.test (
    "a restarting watcher shows no notice and other reasons add no Linux hint",
    fun () -> promise {
        let setState = ref (fun (_: FileWatcherStateDto) -> ())

        RTL.render (
            Harness(
                {|
                    state = FileWatcherStatus.Degraded
                    reason = "boom"
                |},
                (fun setter -> setState.Value <- setter)
            )
        )
        |> ignore

        Vitest.expect((RTL.screen.queryByTestId "file-watcher-unavailable").IsNone).toBe true

        do!
            RTL.act (fun () -> promise {
                setState.Value {|
                    state = FileWatcherStatus.Unavailable
                    reason = "EMFILE: too many open files"
                |}
            })

        Vitest.expect((RTL.screen.getByTestId "file-watcher-unavailable").textContent.Contains "inotify").toBe false
    }
)
