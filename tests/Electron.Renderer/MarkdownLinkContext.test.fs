module ElectronRenderer.MarkdownLinkContextTests

open Fable.Core
open Fable.Core.JsInterop
open Renderer.Types
open Renderer.Components.Helper.MarkdownLinkContext
open Swate.Components.Composite.MarkdownText.Links
open Swate.Components.Composite.MarkdownText.Links.Types
open Swate.Electron.Shared.DTOs.MarkdownResolverDto
open Vitest

// The module reaches Api.js, whose IPC bridges are absent without the preload. No test calls them.
Vitest.vi.mock (
    "./src/Electron/src/Renderer/Api.js",
    box (fun () -> createObj [ "ipcMarkdownResolverApi" ==> createObj [] ])
)
|> ignore

Vitest.describe (
    "MarkdownLinkContext.assetRefusalMessage",
    fun () ->
        Vitest.test (
            "an ARC that is no longer open is named",
            fun () ->
                Vitest.expect(assetRefusalMessage "a.png" AssetRefusal.ArcNotOpen).toBe
                    "This image link is from an ARC that is no longer open."
        )

        Vitest.test (
            "a path outside the ARC is named",
            fun () ->
                Vitest.expect(assetRefusalMessage "a.png" AssetRefusal.OutsideArc).toBe
                    "This image lies outside the ARC."
        )

        Vitest.test (
            "a missing image shows its path",
            fun () ->
                Vitest.expect(assetRefusalMessage "img/a.png" AssetRefusal.NotFound).toBe "Image not found: img/a.png"
        )

        Vitest.test (
            "an LFS pointer says the object is not downloaded",
            fun () ->
                Vitest.expect(assetRefusalMessage "a.png" AssetRefusal.LfsPointer).toBe
                    "This image is stored in Git LFS and is not downloaded."
        )

        Vitest.test (
            "the size limit shows in whole megabytes",
            fun () ->
                Vitest.expect(assetRefusalMessage "a.png" (AssetRefusal.TooLarge(60000000.0, 52428800.0))).toBe
                    "This image is larger than 50 MB."
        )

        Vitest.test (
            "an image without an extension cannot be shown",
            fun () ->
                Vitest.expect(assetRefusalMessage "a" (AssetRefusal.UnsupportedType "")).toBe
                    "Images without a file extension cannot be shown."
        )

        Vitest.test (
            "an unsupported extension is named",
            fun () ->
                Vitest.expect(assetRefusalMessage "a.tif" (AssetRefusal.UnsupportedType ".tif")).toBe
                    ".tif images cannot be shown."
        )

        Vitest.test (
            "an unreadable image shows the reason",
            fun () ->
                Vitest.expect(assetRefusalMessage "a.png" (AssetRefusal.Unreadable "EACCES")).toBe
                    "The image could not be read: EACCES"
        )
)

Vitest.describe (
    "MarkdownLinkContext.linkActionOf",
    fun () ->
        Vitest.test (
            "a file opens at its path",
            fun () -> Vitest.expect(linkActionOf "a/b.md" PathCheck.File).toEqual (OpenFile "a/b.md")
        )

        Vitest.test (
            "a file with another case on disk opens at the path on disk",
            fun () ->
                Vitest
                    .expect(linkActionOf "a/B.md" (PathCheck.CaseDiffers("a/b.md", false)))
                    .toEqual (OpenFile "a/b.md")
        )

        Vitest.test (
            "a folder is revealed at its path",
            fun () -> Vitest.expect(linkActionOf "a" PathCheck.Directory).toEqual (RevealFolder "a")
        )

        Vitest.test (
            "a folder with another case on disk is revealed at the path on disk",
            fun () -> Vitest.expect(linkActionOf "A" (PathCheck.CaseDiffers("a", true))).toEqual (RevealFolder "a")
        )

        Vitest.test (
            "a missing target fails with its path",
            fun () ->
                Vitest
                    .expect(linkActionOf "x.md" PathCheck.Missing)
                    .toEqual (Fail "The link target does not exist: x.md")
        )

        Vitest.test (
            "a target outside the ARC fails with its path",
            fun () ->
                Vitest
                    .expect(linkActionOf "x.md" PathCheck.OutsideArc)
                    .toEqual (Fail "The link target lies outside the ARC: x.md")
        )

        Vitest.test (
            "an unreadable target fails with the reason",
            fun () ->
                Vitest
                    .expect(linkActionOf "x.md" (PathCheck.Unreadable "EACCES"))
                    .toEqual (Fail "The link target could not be read: EACCES")
        )

        Vitest.test (
            "an LFS pointer fails with the download notice",
            fun () ->
                Vitest
                    .expect(linkActionOf "x.bin" PathCheck.LfsPointer)
                    .toEqual (Fail "This file is stored in Git LFS and is not downloaded.")
        )

        Vitest.test (
            "the ARC root fails with its own message",
            fun () ->
                Vitest
                    .expect(linkActionOf "" PathCheck.Directory)
                    .toEqual (Fail "This link points to the ARC root folder.")
        )
)

Vitest.describe (
    "MarkdownLinkContext.targetStateOf",
    fun () ->
        Vitest.test (
            "a file and a folder both exist",
            fun () ->
                Vitest.expect(targetStateOf PathCheck.File).toEqual LinkTargetState.Exists
                Vitest.expect(targetStateOf PathCheck.Directory).toEqual LinkTargetState.Exists
        )

        Vitest.test (
            "a case difference keeps the path on disk",
            fun () ->
                Vitest
                    .expect(targetStateOf (PathCheck.CaseDiffers("a/b.md", true)))
                    .toEqual (LinkTargetState.CaseDiffers "a/b.md")
        )

        Vitest.test (
            "an LFS pointer exists",
            fun () -> Vitest.expect(targetStateOf PathCheck.LfsPointer).toEqual LinkTargetState.Exists
        )

        Vitest.test (
            "missing, outside and unreadable map one to one",
            fun () ->
                Vitest.expect(targetStateOf PathCheck.Missing).toEqual LinkTargetState.Missing
                Vitest.expect(targetStateOf PathCheck.OutsideArc).toEqual LinkTargetState.OutsideArc

                Vitest
                    .expect(targetStateOf (PathCheck.Unreadable "EACCES"))
                    .toEqual (LinkTargetState.Unreadable "EACCES")
        )
)

/// Fake deps that record each call as text, in order.
let private depsWith
    (calls: ResizeArray<string>)
    (check: Result<PathCheck[], exn>)
    (documentPath: string option)
    (confirm: bool)
    (opened: Result<PageState, string>)
    : LinkOpenDeps =
    {
        CheckPath =
            fun p ->
                calls.Add $"CheckPath {p}"
                Promise.lift check
        Navigation = {
            DocumentPath = fun () -> documentPath
            ConfirmLeave =
                fun () ->
                    calls.Add "ConfirmLeave"
                    Promise.lift confirm
            SetSelection = fun p -> calls.Add $"""SetSelection {defaultArg p "none"}"""
            OpenView =
                fun p ->
                    calls.Add $"OpenView {p}"
                    Promise.lift opened
            SetPage =
                fun state ->
                    match state with
                    | Some(PageState.ErrorPage message) -> calls.Add $"SetPage {message}"
                    | Some _ -> calls.Add "SetPage"
                    | None -> calls.Add "SetPage none"
        }
        RevealFolder = fun p -> calls.Add $"RevealFolder {p}"
        OnError = fun message -> calls.Add $"OnError {message}"
    }

let private runClick (check: PathCheck) (path: string) (documentPath: string option) (confirm: bool) opened = promise {
    let calls = ResizeArray<string>()
    do! openArcPathWith (depsWith calls (Ok [| check |]) documentPath confirm opened) path
    return calls |> Seq.toArray
}

Vitest.describe (
    "MarkdownLinkContext.openArcPathWith",
    fun () ->
        Vitest.test (
            "a file opens after the leave guard, in order",
            fun () -> promise {
                let! calls = runClick PathCheck.File "a.md" None true (Ok PageState.UnknownPage)

                Vitest.expect(calls).toEqual [|
                    "CheckPath a.md"
                    "ConfirmLeave"
                    "SetSelection a.md"
                    "OpenView a.md"
                    "SetPage"
                |]
            }
        )

        Vitest.test (
            "a refused leave neither selects nor opens the file",
            fun () -> promise {
                let! calls = runClick PathCheck.File "a.md" None false (Ok PageState.UnknownPage)
                Vitest.expect(calls).toEqual [| "CheckPath a.md"; "ConfirmLeave" |]
            }
        )

        Vitest.test (
            "the open document skips the leave guard",
            fun () -> promise {
                let! calls = runClick PathCheck.File "a.md" (Some "a.md") false (Ok PageState.UnknownPage)

                Vitest.expect(calls).toEqual [|
                    "CheckPath a.md"
                    "SetSelection a.md"
                    "OpenView a.md"
                    "SetPage"
                |]
            }
        )

        Vitest.test (
            "a file with another case on disk opens at the path on disk",
            fun () -> promise {
                let! calls =
                    runClick (PathCheck.CaseDiffers("a/b.md", false)) "a/B.md" None true (Ok PageState.UnknownPage)

                Vitest.expect(calls).toEqual [|
                    "CheckPath a/B.md"
                    "ConfirmLeave"
                    "SetSelection a/b.md"
                    "OpenView a/b.md"
                    "SetPage"
                |]
            }
        )

        Vitest.test (
            "a folder is revealed",
            fun () -> promise {
                let! calls = runClick PathCheck.Directory "docs" None true (Ok PageState.UnknownPage)
                Vitest.expect(calls).toEqual [| "CheckPath docs"; "RevealFolder docs" |]
            }
        )

        Vitest.test (
            "the ARC root reports an error",
            fun () -> promise {
                let! calls = runClick PathCheck.Directory "" None true (Ok PageState.UnknownPage)

                Vitest.expect(calls).toEqual [|
                    "CheckPath "
                    "OnError This link points to the ARC root folder."
                |]
            }
        )

        Vitest.test (
            "a missing target and an LFS pointer report their messages",
            fun () -> promise {
                let! missing = runClick PathCheck.Missing "x.md" None true (Ok PageState.UnknownPage)
                let! pointer = runClick PathCheck.LfsPointer "x.bin" None true (Ok PageState.UnknownPage)

                Vitest.expect(missing).toEqual [|
                    "CheckPath x.md"
                    "OnError The link target does not exist: x.md"
                |]

                Vitest.expect(pointer).toEqual [|
                    "CheckPath x.bin"
                    "OnError This file is stored in Git LFS and is not downloaded."
                |]
            }
        )

        Vitest.test (
            "a failed check reports its message",
            fun () -> promise {
                let calls = ResizeArray<string>()
                let deps = depsWith calls (Error(exn "boom")) None true (Ok PageState.UnknownPage)
                do! openArcPathWith deps "a.md"
                Vitest.expect(calls |> Seq.toArray).toEqual [| "CheckPath a.md"; "OnError boom" |]
            }
        )

        Vitest.test (
            "a result of the wrong length reports an error",
            fun () -> promise {
                let calls = ResizeArray<string>()
                let deps = depsWith calls (Ok [||]) None true (Ok PageState.UnknownPage)
                do! openArcPathWith deps "a.md"

                Vitest.expect(calls |> Seq.toArray).toEqual [|
                    "CheckPath a.md"
                    "OnError The link target could not be checked."
                |]
            }
        )

        Vitest.test (
            "a view that fails to open shows an error page",
            fun () -> promise {
                let! calls = runClick PathCheck.File "a.md" None true (Error "no access")

                Vitest.expect(calls).toEqual [|
                    "CheckPath a.md"
                    "ConfirmLeave"
                    "SetSelection a.md"
                    "OpenView a.md"
                    "SetPage Could not open 'a.md': no access"
                |]
            }
        )
)
