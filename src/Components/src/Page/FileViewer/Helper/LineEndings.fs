module Arcitisa.Components.Page.FileViewer.Helper.LineEndings

open Arcitisa.Components.Composite.TextEditor

/// A notice for a file that mixes "\r\n", "\r" and "\n". The editor writes every line ending like the first one on
/// save (LanguageDetection.detectLineSeparator), and this says so before the first save changes every line.
let notice (text: string) : string option =
    let mutable crlf = false
    let mutable cr = false
    let mutable lf = false
    let mutable i = 0

    while i < text.Length do
        match text.[i] with
        | '\r' when i + 1 < text.Length && text.[i + 1] = '\n' ->
            crlf <- true
            i <- i + 2
        | '\r' ->
            cr <- true
            i <- i + 1
        | '\n' ->
            lf <- true
            i <- i + 1
        | _ -> i <- i + 1

    let kinds = [ crlf; cr; lf ] |> List.filter id |> List.length

    if kinds < 2 then
        None
    else
        let name =
            match LanguageDetection.detectLineSeparator text with
            | "\r\n" -> "CRLF (Windows)"
            | "\r" -> "CR (classic Mac)"
            | _ -> "LF (Linux and macOS)"

        Some
            $"This file mixes line endings. A save writes all of them as {name}, so lines that end differently change too."
