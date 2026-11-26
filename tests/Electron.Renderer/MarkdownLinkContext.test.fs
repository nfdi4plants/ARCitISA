module ElectronRenderer.MarkdownLinkContextTests

open Fable.Core
open Fable.Core.JsInterop
open Renderer.Types
open Renderer.Components.Helper.MarkdownLinkContext
open Arcitisa.Components.Composite.MarkdownText.Links
open Arcitisa.Components.Composite.MarkdownText.Links.Types
open Arcitisa.Electron.Shared.DTOs.MarkdownResolverDto
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
            "a missing image shows its path",
            fun () -> Vitest.expect(assetRefusalMessage "img/a.png" AssetRefusal.NotFound).toContain "img/a.png"
        )

        Vitest.test (
            "the size limit shows in whole megabytes",
            fun () ->
                Vitest.expect(assetRefusalMessage "a.png" (AssetRefusal.TooLarge(60000000.0, 52428800.0))).toContain
                    "50 MB"
        )

        Vitest.test (
            "an unsupported extension is named",
            fun () -> Vitest.expect(assetRefusalMessage "a.tif" (AssetRefusal.UnsupportedType ".tif")).toContain ".tif"
        )

        Vitest.test (
            "an unreadable image shows the reason",
            fun () -> Vitest.expect(assetRefusalMessage "a.png" (AssetRefusal.Unreadable "EACCES")).toContain "EACCES"
        )
)

/// The message of a failed link action. Any other action fails the test.
let private failMessage (action: LinkAction) =
    match action with
    | Fail message -> message
    | other -> failwith $"Expected Fail, got {other}."

let private isFail (action: LinkAction) =
    match action with
    | Fail _ -> true
    | _ -> false

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
            fun () -> Vitest.expect(failMessage (linkActionOf "x.md" PathCheck.Missing)).toContain "x.md"
        )

        Vitest.test (
            "a target outside the ARC fails with its path",
            fun () -> Vitest.expect(failMessage (linkActionOf "x.md" PathCheck.OutsideArc)).toContain "x.md"
        )

        Vitest.test (
            "an unreadable target fails with the reason",
            fun () ->
                Vitest.expect(failMessage (linkActionOf "x.md" (PathCheck.Unreadable "EACCES"))).toContain "EACCES"
        )

        Vitest.test (
            "an LFS pointer fails instead of opening",
            fun () -> Vitest.expect(isFail (linkActionOf "x.bin" PathCheck.LfsPointer)).toBe true
        )

        Vitest.test (
            "the ARC root fails instead of being revealed",
            fun () -> Vitest.expect(isFail (linkActionOf "" PathCheck.Directory)).toBe true
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

/// Fake deps that record each call as text, in order. The messages for the user go to messages, so a test checks the
/// calls apart from the wording.
let private depsWith
    (calls: ResizeArray<string>)
    (messages: ResizeArray<string>)
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
                    | Some(PageState.ErrorPage message) ->
                        calls.Add "SetPage error"
                        messages.Add message
                    | Some _ -> calls.Add "SetPage"
                    | None -> calls.Add "SetPage none"
        }
        RevealFolder = fun p -> calls.Add $"RevealFolder {p}"
        OnError =
            fun message ->
                calls.Add "OnError"
                messages.Add message
    }

/// Runs a link click on the given check result and returns the calls and the messages, each in order.
let private runCheck
    (check: Result<PathCheck[], exn>)
    (path: string)
    (documentPath: string option)
    (confirm: bool)
    (opened: Result<PageState, string>)
    =
    promise {
        let calls = ResizeArray<string>()
        let messages = ResizeArray<string>()
        do! openArcPathWith (depsWith calls messages check documentPath confirm opened) path
        return calls.ToArray(), messages.ToArray()
    }

let private runClick (check: PathCheck) path documentPath confirm opened =
    runCheck (Ok [| check |]) path documentPath confirm opened

Vitest.describe (
    "MarkdownLinkContext.openArcPathWith",
    fun () ->
        Vitest.test (
            "a file opens after the leave guard, in order",
            fun () -> promise {
                let! calls, _ = runClick PathCheck.File "a.md" None true (Ok PageState.UnknownPage)

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
                let! calls, _ = runClick PathCheck.File "a.md" None false (Ok PageState.UnknownPage)
                Vitest.expect(calls).toEqual [| "CheckPath a.md"; "ConfirmLeave" |]
            }
        )

        Vitest.test (
            "the open document skips the leave guard",
            fun () -> promise {
                let! calls, _ = runClick PathCheck.File "a.md" (Some "a.md") false (Ok PageState.UnknownPage)

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
                let! calls, _ =
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
                let! calls, _ = runClick PathCheck.Directory "docs" None true (Ok PageState.UnknownPage)
                Vitest.expect(calls).toEqual [| "CheckPath docs"; "RevealFolder docs" |]
            }
        )

        Vitest.test (
            "the ARC root reports an error",
            fun () -> promise {
                let! calls, _ = runClick PathCheck.Directory "" None true (Ok PageState.UnknownPage)
                Vitest.expect(calls).toEqual [| "CheckPath "; "OnError" |]
            }
        )

        Vitest.test (
            "a missing target and an LFS pointer report an error, the missing target with its path",
            fun () -> promise {
                let! missing, missingMessages = runClick PathCheck.Missing "x.md" None true (Ok PageState.UnknownPage)
                let! pointer, _ = runClick PathCheck.LfsPointer "x.bin" None true (Ok PageState.UnknownPage)

                Vitest.expect(missing).toEqual [| "CheckPath x.md"; "OnError" |]
                Vitest.expect(missingMessages.[0]).toContain "x.md"
                Vitest.expect(pointer).toEqual [| "CheckPath x.bin"; "OnError" |]
            }
        )

        Vitest.test (
            "a failed check reports its message",
            fun () -> promise {
                let! calls, messages = runCheck (Error(exn "boom")) "a.md" None true (Ok PageState.UnknownPage)

                Vitest.expect(calls).toEqual [| "CheckPath a.md"; "OnError" |]
                Vitest.expect(messages.[0]).toContain "boom"
            }
        )

        Vitest.test (
            "a result of the wrong length reports an error",
            fun () -> promise {
                let! calls, _ = runCheck (Ok [||]) "a.md" None true (Ok PageState.UnknownPage)
                Vitest.expect(calls).toEqual [| "CheckPath a.md"; "OnError" |]
            }
        )

        Vitest.test (
            "a view that fails to open shows an error page with the path and the reason",
            fun () -> promise {
                let! calls, messages = runClick PathCheck.File "a.md" None true (Error "no access")

                Vitest.expect(calls).toEqual [|
                    "CheckPath a.md"
                    "ConfirmLeave"
                    "SetSelection a.md"
                    "OpenView a.md"
                    "SetPage error"
                |]

                Vitest.expect(messages.[0]).toContain "a.md"
                Vitest.expect(messages.[0]).toContain "no access"
            }
        )
)

Vitest.describe (
    "MarkdownLinkContext.folderContentsOf",
    fun () ->
        Vitest.test (
            "hands a listing to the path completion as it is",
            fun () ->
                let entries: FolderEntryDto[] = [|
                    {| name = "docs"; isDirectory = true |}
                    {| name = "a.md"; isDirectory = false |}
                |]

                match folderContentsOf (FolderListing.Listed(entries, true)) with
                | Some contents ->
                    Vitest.expect(contents.entries).toEqual entries
                    Vitest.expect(contents.truncated).toBe true
                | None -> failwith "Expected folder contents"
        )

        Vitest.test (
            "offers nothing for a file, a missing folder or a path outside the ARC",
            fun () ->
                Vitest.expect(folderContentsOf FolderListing.NotAFolder |> Option.isNone).toBe true
                Vitest.expect(folderContentsOf FolderListing.OutsideArc |> Option.isNone).toBe true
        )
)
