module internal Swate.Components.Composite.TextEditor.LanguageDetectionTests

open Vitest
open Swate.Components.Composite.TextEditor.LanguageDetection

let private entry (id: string) (extensions: string[]) (fileNames: string[]) : LanguageEntry = {|
    id = id
    extensions = extensions
    matchesFileName = fun name -> Array.contains name fileNames
    prose = None
|}

let private registry: LanguageEntry[] = [|
    entry "python" [| "py"; "pyw" |] [||]
    entry "dockerfile" [||] [| "Dockerfile" |]
    entry "shell" [| "sh" |] [||]
    entry "r" [| "r"; "R" |] [||]
    entry "yaml" [| "yaml"; "yml" |] [||]
    entry "f#" [| "fs" |] [||]
|]

let private detect (name: string) = detectLanguage registry name ""

Vitest.describe (
    "detectLanguage",
    fun () ->
        Vitest.test ("maps .md to markdown", fun () -> Vitest.expect(detect "notes.md").toBe "markdown")

        Vitest.test (
            "maps README without extension to markdown",
            fun () -> Vitest.expect(detect "README").toBe "markdown"
        )

        Vitest.test ("maps LICENSE to plaintext", fun () -> Vitest.expect(detect "LICENSE").toBe "plaintext")
        Vitest.test ("maps .cwl to yaml", fun () -> Vitest.expect(detect "tool.cwl").toBe "yaml")
        Vitest.test ("maps .yml to yaml", fun () -> Vitest.expect(detect "config.yml").toBe "yaml")
        Vitest.test ("maps .fsx to f#", fun () -> Vitest.expect(detect "build.fsx").toBe "f#")
        Vitest.test ("ignores the case of the extension", fun () -> Vitest.expect(detect "DATA.JSON").toBe "json")
        Vitest.test ("uses the extensions of the registry", fun () -> Vitest.expect(detect "run.py").toBe "python")

        Vitest.test (
            "matches an upper-case extension of the registry",
            fun () -> Vitest.expect(detect "analysis.R").toBe "r"
        )

        Vitest.test (
            "uses the file names of the registry",
            fun () -> Vitest.expect(detect "Dockerfile").toBe "dockerfile"
        )

        Vitest.test (
            "reads a python shebang of an extensionless file",
            fun () -> Vitest.expect(detectLanguage registry "run" "#!/usr/bin/env python3 -u").toBe "python"
        )

        Vitest.test (
            "reads a bash shebang",
            fun () -> Vitest.expect(detectLanguage registry "run" "#!/bin/bash").toBe "shell"
        )

        Vitest.test (
            "reads an Rscript shebang",
            fun () -> Vitest.expect(detectLanguage registry "run" "#!/usr/bin/env Rscript").toBe "r"
        )

        Vitest.test (
            "falls back to plaintext for an unknown extension",
            fun () -> Vitest.expect(detect "a.unknownext").toBe "plaintext"
        )

        Vitest.test (
            "uses the last segment of a path",
            fun () -> Vitest.expect(detect "assays/a1/dataset/run.py").toBe "python"
        )
)

Vitest.describe (
    "proseOf",
    fun () ->
        Vitest.test (
            "treats plain text as prose without nodes to skip",
            fun () ->
                Vitest
                    .expect(proseOf LanguageDetection.registry (detect "LICENSE"))
                    .toEqual (
                        Some {|
                            skippedNodes = [||]
                            skippedTagsIn = [||]
                        |}
                    )
        )

        Vitest.test (
            "treats code languages and unknown ids as code",
            fun () ->
                Vitest.expect(proseOf LanguageDetection.registry "json").toEqual None
                Vitest.expect(proseOf LanguageDetection.registry "no such language").toEqual None
        )
)

Vitest.describe (
    "detectLineSeparator",
    fun () ->
        Vitest.test ("finds CRLF", fun () -> Vitest.expect(detectLineSeparator "a\r\nb\nc").toBe "\r\n")
        Vitest.test ("finds LF", fun () -> Vitest.expect(detectLineSeparator "a\nb\r\nc").toBe "\n")
        Vitest.test ("finds a lone CR", fun () -> Vitest.expect(detectLineSeparator "a\rb").toBe "\r")
        Vitest.test ("uses LF for a single line", fun () -> Vitest.expect(detectLineSeparator "abc").toBe "\n")

        Vitest.test (
            "reads a CR at the end of the text as CR",
            fun () -> Vitest.expect(detectLineSeparator "abc\r").toBe "\r"
        )
)
