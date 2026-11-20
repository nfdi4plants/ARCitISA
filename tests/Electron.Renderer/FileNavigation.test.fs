module ElectronRenderer.FileNavigationTests

open Fable.Core
open Fable.Core.JsInterop
open Renderer.Types
open Renderer.Components.Helper.FileNavigation
open Vitest

// The module reaches Api.js through ArcViewHelper, whose IPC bridges are absent without the preload. No test calls them.
Vitest.vi.mock ("./src/Electron/src/Renderer/Api.js", box (fun () -> createObj []))
|> ignore

/// Fake deps that record each call as text, in order.
let private depsWith (calls: ResizeArray<string>) (documentPath: string option) (confirm: bool) : FileNavigationDeps = {
    DocumentPath = fun () -> documentPath
    ConfirmLeave =
        fun () ->
            calls.Add "ConfirmLeave"
            Promise.lift confirm
    SetSelection = fun p -> calls.Add $"""SetSelection {defaultArg p "none"}"""
    OpenView =
        fun p ->
            calls.Add $"OpenView {p}"
            Promise.lift (Ok PageState.UnknownPage)
    SetPage =
        fun page ->
            match page with
            | Some _ -> calls.Add "SetPage"
            | None -> calls.Add "SetPage none"
}

Vitest.describe (
    "FileNavigation",
    fun () ->
        Vitest.test (
            "closing asks the leave guard, then clears the selection and the page",
            fun () -> promise {
                let calls = ResizeArray<string>()
                let! closed = closeFileWith (depsWith calls (Some "a.md") true)

                Vitest.expect(closed).toBe true
                Vitest.expect(calls.ToArray()).toEqual [| "ConfirmLeave"; "SetSelection none"; "SetPage none" |]
            }
        )

        Vitest.test (
            "a cancelled close keeps the selection and the page",
            fun () -> promise {
                let calls = ResizeArray<string>()
                let! closed = closeFileWith (depsWith calls (Some "a.md") false)

                Vitest.expect(closed).toBe false
                Vitest.expect(calls.ToArray()).toEqual [| "ConfirmLeave" |]
            }
        )

        Vitest.test (
            "a file that needs an LFS download is selected with an empty page and not opened",
            fun () -> promise {
                let calls = ResizeArray<string>()
                let! opened = openFileWith (depsWith calls None true) "data/big.bin" true

                Vitest.expect(opened).toBe true

                Vitest.expect(calls.ToArray()).toEqual [|
                    "ConfirmLeave"
                    "SetSelection data/big.bin"
                    "SetPage none"
                |]
            }
        )
)
