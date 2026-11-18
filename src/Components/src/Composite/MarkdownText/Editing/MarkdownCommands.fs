module Swate.Components.Composite.MarkdownText.Editing.MarkdownCommands

open Swate.Components.Composite.TextEditor.Types
open Swate.Components.Composite.MarkdownText.Editing.Types
open Swate.Components.Composite.MarkdownText.Editing.MarkdownEdits

let private command id label icon key run = {
    Id = id
    Label = label
    Icon = icon
    Key = key
    Run = run
}

let heading1 =
    command "heading-1" "Heading 1" "swt:fluent--text-header-1-24-regular" None (setHeading 1)

let heading2 =
    command "heading-2" "Heading 2" "swt:fluent--text-header-2-24-regular" None (setHeading 2)

let heading3 =
    command "heading-3" "Heading 3" "swt:fluent--text-header-3-24-regular" None (setHeading 3)

let bold =
    command "bold" "Bold" "swt:fluent--text-bold-24-regular" (Some "Mod-b") toggleBold

let italic =
    command "italic" "Italic" "swt:fluent--text-italic-24-regular" (Some "Mod-i") toggleItalic

let strikethrough =
    command "strikethrough" "Strikethrough" "swt:fluent--text-strikethrough-24-regular" None toggleStrikethrough

let inlineCode =
    command "inline-code" "Inline code" "swt:fluent--code-24-regular" (Some "Mod-e") toggleInlineCode

let codeBlock =
    command "code-block" "Code block" "swt:fluent--code-block-24-regular" None (insertCodeBlock >> Ok)

let link =
    command "link" "Link" "swt:fluent--link-24-regular" (Some "Mod-k") insertLink

let quote =
    command "quote" "Quote" "swt:fluent--text-quote-24-regular" None toggleQuote

let bulletList =
    command "bulleted-list" "Bulleted list" "swt:fluent--text-bullet-list-ltr-24-regular" None toggleBulletList

let numberedList =
    command "numbered-list" "Numbered list" "swt:fluent--text-number-list-ltr-24-regular" None toggleNumberedList

let taskList =
    command "task-list" "Task list" "swt:fluent--task-list-ltr-24-regular" None toggleTaskList

let table =
    command "table" "Table" "swt:fluent--table-24-regular" None (insertTable >> Ok)

let headings: MarkdownCommand[] = [| heading1; heading2; heading3 |]

/// The button groups of the toolbar. The toolbar draws the heading menu at the start of the first group.
let groups: MarkdownCommand[][] = [|
    [| bold; italic; strikethrough |]
    [| inlineCode; codeBlock; link; quote |]
    [| bulletList; numberedList; taskList |]
    [| table |]
|]

/// The key bindings of the commands that have a key. A refused command reports its reason through report and
/// consumes the key without a change, because returning None would hand Mod-i to selectParentSyntax. A successful
/// one reports None. Build the array once per host with a stable report function (for example a React state setter
/// inside React.useMemo with no dependencies) and pass the same instance to the editor, so refusals by key show in
/// the toolbar.
let keyBindingsWith (report: string option -> unit) : TextEditorKeyBinding[] =
    groups
    |> Array.concat
    |> Array.choose (fun cmd ->
        cmd.Key
        |> Option.map (fun key -> {|
            key = key
            run =
                fun state ->
                    match cmd.Run state with
                    | Ok edit ->
                        report None
                        Some edit
                    | Error reason ->
                        report (Some reason)

                        Some {|
                            changes = [||]
                            selection = state.selection
                        |}
        |})
    )

/// "Mod-b" becomes "Ctrl+B", or "⌘+B" on a Mac.
let shortcutLabel (isMac: bool) (key: string) : string =
    let parts = key.Split '-'

    parts
    |> Array.mapi (fun i part ->
        if i = parts.Length - 1 then part.ToUpperInvariant()
        elif part = "Mod" then (if isMac then "⌘" else "Ctrl")
        else part
    )
    |> String.concat "+"
