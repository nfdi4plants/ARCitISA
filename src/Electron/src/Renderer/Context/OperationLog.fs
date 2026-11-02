/// A bounded buffer for the raw output of one operation. The Git workflow keeps one for the sidebar and the
/// file tree keeps one per running LFS action. The buffer holds plain data, so React state can store it.
module Renderer.Context.OperationLog

open System.Collections.Generic
open Swate.Electron.Shared.VersionControlTypes

type OperationLogLine = {
    CommandId: string
    Kind: string
    Text: string
    IsPartial: bool
}

type OperationLog = {
    Lines: OperationLogLine[]
    /// How many of the oldest lines the caps dropped so far.
    Omitted: int
}

[<Literal>]
let MaxLines = 2000

[<Literal>]
let MaxCharacters = 1000000

let empty: OperationLog = { Lines = [||]; Omitted = 0 }

/// The stream of a line is its command and its kind. A command writes stdout and stderr at the same time, so
/// the last line of a stream is not always the last line of the log.
let private streamKey (commandId: string) (kind: string) = commandId + "\u0000" + kind

/// Adds a batch of entries at the end. An entry replaces the last line of its stream when that line is partial,
/// because the library sends a partial line again while the command rewrites it. When the log then holds more
/// than MaxLines lines or MaxCharacters characters, the oldest lines go and Omitted counts them.
///
/// One batch costs one pass over the kept lines, at most MaxLines. The pass finds the last line of each stream
/// and sums the characters. Every entry after that is constant work.
let append (entries: VersionControlOutputEntryDto[]) (log: OperationLog) : OperationLog =
    if entries.Length = 0 then
        log
    else
        let lines = ResizeArray<OperationLogLine>(log.Lines)
        let lastOfStream = Dictionary<string, int>()
        let mutable characters = 0

        for index in 0 .. lines.Count - 1 do
            lastOfStream.[streamKey lines.[index].CommandId lines.[index].Kind] <- index
            characters <- characters + lines.[index].Text.Length

        for entry in entries do
            let line: OperationLogLine = {
                CommandId = entry.CommandId
                Kind = entry.Kind
                Text = entry.Text
                IsPartial = entry.IsPartial
            }

            let key = streamKey line.CommandId line.Kind

            match lastOfStream.TryGetValue key with
            | true, index when lines.[index].IsPartial ->
                characters <- characters - lines.[index].Text.Length + line.Text.Length
                lines.[index] <- line
            | _ ->
                lines.Add line
                lastOfStream.[key] <- lines.Count - 1
                characters <- characters + line.Text.Length

        let mutable dropped = max 0 (lines.Count - MaxLines)

        for index in 0 .. dropped - 1 do
            characters <- characters - lines.[index].Text.Length

        while characters > MaxCharacters && dropped < lines.Count do
            characters <- characters - lines.[dropped].Text.Length
            dropped <- dropped + 1

        if dropped > 0 then
            lines.RemoveRange(0, dropped)

        {
            Lines = lines.ToArray()
            Omitted = log.Omitted + dropped
        }
