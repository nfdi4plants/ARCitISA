module ElectronRenderer.TextDocumentStateTests

open Vitest
open Swate.Electron.Shared.FileIOTypes
open Renderer.Components.MainContent.TextDocument.TextDocumentState

let private info (hash: string) (encoding: TextFileEncoding) (readOnly: TextReadOnlyReason option) : TextDocumentInfo = {|
    encoding = encoding
    contentHash = hash
    sizeBytes = 10.0
    readOnly = readOnly
|}

let private utf8 hash = info hash TextFileEncoding.Utf8 None

Vitest.describe (
    "TextDocumentState",
    fun () ->
        Vitest.test (
            "a reload adopts the encoding and the read-only reason of the new version",
            fun () ->
                let edited = init "äö" (Some(utf8 "a")) |> dirtyChanged true

                let reloaded =
                    adoptDiskVersion
                        "äö"
                        (Some(info "b" TextFileEncoding.Windows1252 (Some TextReadOnlyReason.NotUtf8)))
                        edited

                Vitest.expect(readOnlyText reloaded).toEqual (Some(readOnlyMessage TextReadOnlyReason.NotUtf8))
                Vitest.expect((saveRequest "a.txt" "äö" reloaded).encoding).toEqual TextFileEncoding.Windows1252
                Vitest.expect((saveRequest "a.txt" "äö" reloaded).expectedHash).toEqual (Some "b")
        )

        Vitest.test (
            "the echo of the page's own version changes nothing",
            fun () ->
                let state = init "x" (Some(utf8 "a"))
                let next, adopted = diskChanged "x" (utf8 "a") state

                Vitest.expect(obj.ReferenceEquals(next, state)).toBe true
                Vitest.expect(adopted).toBe false
        )

        Vitest.test (
            "a change on disk keeps unsaved edits and says so",
            fun () ->
                let state = init "x" (Some(utf8 "a")) |> dirtyChanged true
                let next, adopted = diskChanged "y" (utf8 "b") state

                Vitest.expect(adopted).toBe false
                Vitest.expect(next.Notice).toEqual (Some ChangedOnDisk)
                Vitest.expect(next.SavedText).toBe "x"
                Vitest.expect(next.Info).toEqual (Some(utf8 "a"))
        )

        Vitest.test (
            "a change on disk without unsaved edits is adopted",
            fun () ->
                let next, adopted = diskChanged "y" (utf8 "b") (init "x" (Some(utf8 "a")))

                Vitest.expect(adopted).toBe true
                Vitest.expect(next.SavedText).toBe "y"
                Vitest.expect(next.Info).toEqual (Some(utf8 "b"))
        )

        Vitest.test (
            "a save of a missing file sends no expected hash, and success ends the missing state",
            fun () ->
                let missing = init "x" (Some(utf8 "a")) |> removedFromDisk

                Vitest.expect((saveRequest "a.txt" "x" missing).expectedHash).toEqual None
                Vitest.expect(missing.Notice).toEqual (Some RemovedFromDisk)

                let saved = missing |> saveStarted |> saveSucceeded "x2" (utf8 "c")

                Vitest.expect(saved.MissingOnDisk).toBe false
                Vitest.expect(saved.Notice).toEqual None
                Vitest.expect(saved.SavedText).toBe "x2"
                Vitest.expect(saved.SavesRunning).toBe 0
                Vitest.expect((saveRequest "a.txt" "x2" saved).expectedHash).toEqual (Some "c")
        )

        Vitest.test (
            "an overwrite goes with the next save request only",
            fun () ->
                let requested = init "x" (Some(utf8 "a")) |> overwriteRequested

                Vitest.expect((saveRequest "a.txt" "x" requested).overwrite).toBe true
                Vitest.expect((saveRequest "a.txt" "x" (saveStarted requested)).overwrite).toBe false
                Vitest.expect((overwriteFinished requested).OverwriteNext).toBe false
        )

        Vitest.test (
            "conflicts and failures end the running save and say why",
            fun () ->
                let running = init "x" (Some(utf8 "a")) |> saveStarted

                Vitest.expect((saveConflicted true running).Notice).toEqual (Some SaveConflict)
                Vitest.expect((saveConflicted false running).Notice).toEqual (Some SaveConflictMissing)
                Vitest.expect((saveFailed "disk full" running).Error).toEqual (Some "Saving failed: disk full")
                Vitest.expect((saveFailed "disk full" running).SavesRunning).toBe 0
        )

        Vitest.test (
            "the status puts read-only first, then a running save, an error and unsaved edits",
            fun () ->
                let editable = init "x" (Some(utf8 "a"))

                Vitest.expect(status (init "x" None)).toEqual ("Read-only", false)
                Vitest.expect(status (saveStarted editable)).toEqual ("Saving...", false)
                Vitest.expect(status (saveFailed "boom" (saveStarted editable))).toEqual ("Saving failed: boom", true)
                Vitest.expect(status (dirtyChanged true editable)).toEqual ("Unsaved changes", false)
                Vitest.expect(status editable).toEqual ("Saved", false)
        )
)
