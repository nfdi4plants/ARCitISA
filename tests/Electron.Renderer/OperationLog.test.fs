module ElectronRenderer.OperationLogTests

open Renderer.Context.OperationLog
open Swate.Electron.Shared.VersionControlTypes
open Vitest

let private entry (commandId: string) (kind: string) (text: string) (isPartial: bool) : VersionControlOutputEntryDto = {
    CommandId = commandId
    Kind = kind
    Text = text
    IsPartial = isPartial
}

let private stdout' (text: string) = entry "cmd-1" "stdout" text false

let private texts (log: OperationLog) = log.Lines |> Array.map _.Text

Vitest.describe (
    "OperationLog.append",
    fun () ->
        Vitest.test (
            "keeps the order of the entries across batches",
            fun () ->
                let log = empty |> append [| stdout' "a"; stdout' "b" |] |> append [| stdout' "c" |]

                Vitest.expect(texts log).toEqual [| "a"; "b"; "c" |]
                Vitest.expect(log.Omitted).toBe 0
        )

        Vitest.test (
            "an empty batch returns the same log",
            fun () -> Vitest.expect(obj.ReferenceEquals(append [||] empty, empty)).toBe true
        )

        Vitest.test (
            "a partial line is replaced by the next entry of the same command and kind",
            fun () ->
                let log =
                    empty
                    |> append [| entry "cmd-1" "stderr" "Receiving objects: 10%" true |]
                    |> append [| entry "cmd-1" "stderr" "Receiving objects: 40%" true |]

                Vitest.expect(texts log).toEqual [| "Receiving objects: 40%" |]
                Vitest.expect(log.Lines.[0].IsPartial).toBe true

                let completed =
                    log
                    |> append [|
                        entry "cmd-1" "stderr" "Receiving objects: 100%, done." false
                    |]

                Vitest.expect(texts completed).toEqual [| "Receiving objects: 100%, done." |]
                Vitest.expect(completed.Lines.[0].IsPartial).toBe false
        )

        Vitest.test (
            "a partial line is replaced even when lines of other streams came after it",
            fun () ->
                let log =
                    empty
                    |> append [|
                        entry "cmd-1" "stderr" "Receiving objects: 10%" true
                        entry "cmd-1" "stdout" "an output line" false
                        entry "cmd-1" "stderr" "Receiving objects: 90%" true
                    |]

                Vitest.expect(texts log).toEqual [| "Receiving objects: 90%"; "an output line" |]
        )

        Vitest.test (
            "a partial line is kept when the next entry belongs to another command or kind",
            fun () ->
                let log =
                    empty
                    |> append [|
                        entry "cmd-1" "stderr" "Receiving objects: 10%" true
                        entry "cmd-2" "stderr" "Filtering content: 5%" true
                        entry "cmd-1" "stdout" "an output line" false
                    |]

                Vitest.expect(texts log).toEqual [|
                    "Receiving objects: 10%"
                    "Filtering content: 5%"
                    "an output line"
                |]
        )

        Vitest.test (
            "a complete line is not replaced by the next entry of its stream",
            fun () ->
                let log = empty |> append [| stdout' "a"; stdout' "b" |]

                Vitest.expect(texts log).toEqual [| "a"; "b" |]
        )

        Vitest.test (
            "the line cap drops the oldest lines and counts them",
            fun () ->
                let entries = Array.init (MaxLines + 5) (fun index -> stdout' $"line {index}")

                let log = empty |> append entries

                Vitest.expect(log.Lines.Length).toBe MaxLines
                Vitest.expect(log.Lines.[0].Text).toBe "line 5"
                Vitest.expect(log.Omitted).toBe 5

                let next = log |> append [| stdout' "newest" |]

                Vitest.expect(next.Lines.Length).toBe MaxLines
                Vitest.expect(next.Lines.[0].Text).toBe "line 6"
                Vitest.expect(next.Lines.[MaxLines - 1].Text).toBe "newest"
                Vitest.expect(next.Omitted).toBe 6
        )

        Vitest.test (
            "the character cap drops the oldest lines and counts them",
            fun () ->
                let block = MaxCharacters / 4

                let line (marker: string) =
                    stdout' (marker + String.replicate (block - 1) "x")

                let log = empty |> append [| line "a"; line "b"; line "c"; line "d" |]

                Vitest.expect(log.Lines.Length).toBe 4
                Vitest.expect(log.Omitted).toBe 0

                let capped = log |> append [| line "e" |]

                Vitest.expect(capped.Lines |> Array.map (fun kept -> kept.Text.Substring(0, 1))).toEqual [|
                    "b"
                    "c"
                    "d"
                    "e"
                |]

                Vitest.expect(capped.Omitted).toBe 1
        )
)

Vitest.describe (
    "OperationLog.tryText",
    fun () ->
        Vitest.test ("returns None for a log without lines", fun () -> Vitest.expect(tryText empty).toEqual None)

        Vitest.test (
            "starts with the omitted count, marks the command line and joins the lines with line feeds",
            fun () ->
                let command = "git push"
                let output = "Everything up-to-date"

                let appended =
                    empty |> append [| entry "cmd-1" "command" command false; stdout' output |]

                let log = { appended with Omitted = 2 }

                let expected =
                    String.concat "\n" [ "2 earlier lines were left out"; "$ " + command; output ]

                Vitest.expect(tryText log).toEqual (Some expected)
        )
)

Vitest.describe (
    "OperationLog.tryDetails",
    fun () ->
        Vitest.test ("returns None for a log without lines", fun () -> Vitest.expect(tryDetails empty).toEqual None)

        Vitest.test (
            "puts a heading line in front of the text of the log",
            fun () ->
                let log =
                    empty
                    |> append [|
                        entry "cmd-1" "command" "git push" false
                        stdout' "Everything up-to-date"
                    |]

                let details = (tryDetails log).Value
                let headingEnd = details.IndexOf('\n')

                Vitest.expect(headingEnd > 0).toBe true
                Vitest.expect(details.Substring(headingEnd + 1)).toBe (tryText log).Value
        )
)

let private finishedLog (arcName: string option) (lines: string[]) : FinishedOperationLog = {
    Label = "Saving changes"
    ArcName = arcName
    FinishedAt = 1700000000000.0
    Log = append (lines |> Array.map stdout') empty
}

Vitest.describe (
    "OperationLog.toSidebarLastLog",
    fun () ->
        Vitest.test (
            "a finished log with lines gets a title with the label and the ARC name and keeps its lines",
            fun () ->
                let mapped =
                    toSidebarLastLog (finishedLog (Some "my-arc") [| "first line"; "second line" |])

                Vitest.expect(mapped.IsSome).toBe true
                Vitest.expect(mapped.Value.Title.StartsWith "Saving changes").toBe true
                Vitest.expect(mapped.Value.Title.Contains "my-arc").toBe true
                Vitest.expect(mapped.Value.Log.Lines |> Array.map _.Text).toEqual [| "first line"; "second line" |]
        )

        Vitest.test (
            "a finished log without an ARC name has no ARC part in its title",
            fun () ->
                let mapped = toSidebarLastLog (finishedLog None [| "first line" |])

                Vitest.expect(mapped.Value.Title.Split([| " · " |], System.StringSplitOptions.None).Length).toBe 2
                Vitest.expect(mapped.Value.Title.StartsWith "Saving changes").toBe true
        )

        Vitest.test (
            "the title ends with the local time of the end as hours and minutes",
            fun () ->
                let mapped = toSidebarLastLog (finishedLog (Some "my-arc") [| "first line" |])

                Vitest
                    .expect(System.Text.RegularExpressions.Regex.IsMatch(mapped.Value.Title, @"^.* · \d{2}:\d{2}$"))
                    .toBe
                    true
        )

        Vitest.test (
            "a finished log without lines maps to None",
            fun () -> Vitest.expect(toSidebarLastLog (finishedLog (Some "my-arc") [||])).toEqual None
        )
)

Vitest.describe (
    "OperationLog.newest",
    fun () ->
        let older = finishedLog (Some "my-arc") [| "older line" |]

        let newer = {
            finishedLog (Some "my-arc") [| "newer line" |] with
                FinishedAt = older.FinishedAt + 1000.0
        }

        Vitest.test (
            "the log that ended later wins whichever side it comes from",
            fun () ->
                Vitest.expect(newest (Some older) (Some newer)).toEqual (Some newer)
                Vitest.expect(newest (Some newer) (Some older)).toEqual (Some newer)
        )

        Vitest.test (
            "the only log that exists is returned",
            fun () ->
                Vitest.expect(newest (Some older) None).toEqual (Some older)
                Vitest.expect(newest None (Some older)).toEqual (Some older)
        )

        Vitest.test ("two missing logs give None", fun () -> Vitest.expect(newest None None).toEqual None)
)
