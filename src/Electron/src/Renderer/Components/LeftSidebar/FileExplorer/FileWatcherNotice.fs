module Renderer.Components.LeftSidebar.FileExplorer.FileWatcherNotice

open Feliz
open Swate.Electron.Shared.IPCTypes

/// A short notice above the file tree while the file watcher cannot run. The tree then changes on window focus and
/// through Refresh folder. A watcher that works, or that is restarting by itself, shows nothing.
[<ReactComponent>]
let FileWatcherNotice (watcher: FileWatcherStateDto) =
    if watcher.state <> "unavailable" then
        Html.none
    else
        // The error text of the system names the limit. Linux ends the watcher with ENOSPC when the inotify
        // watches run out.
        let hint =
            if watcher.reason.Contains "ENOSPC" then
                " On Linux, raise fs.inotify.max_user_watches."
            else
                ""

        Html.div [
            prop.testId "file-watcher-unavailable"
            prop.role "status"
            prop.className "swt:alert swt:alert-warning swt:mx-1 swt:px-2 swt:py-1 swt:text-xs"
            prop.children [
                Html.i [
                    prop.className "swt:iconify swt:fluent--warning-24-regular swt:size-4 swt:shrink-0"
                ]
                Html.span [
                    prop.text (
                        "File watching is unavailable. Changes appear when this window gets focus or with Refresh folder. "
                        + watcher.reason
                        + hint
                    )
                ]
            ]
        ]
