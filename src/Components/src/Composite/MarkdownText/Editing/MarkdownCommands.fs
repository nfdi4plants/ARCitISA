module Arcitisa.Components.Composite.MarkdownText.Editing.MarkdownCommands

open Arcitisa.Components.Composite.TextEditor.Types
open Arcitisa.Components.Composite.MarkdownText.Editing.Types
open Arcitisa.Components.Composite.MarkdownText.Editing.MarkdownEdits

let private command id label icon key run = {
    Id = id
    Label = label
    Icon = icon
    Key = key
    Run = run
}

// The keys avoid CodeMirror's default keys, the menu keys of Electron and Ctrl-Alt, which is AltGr on Windows.
// A shifted digit also has to stay clear of a key CodeMirror binds: on a German keyboard Shift-7 types "/", so
// CodeMirror reads Mod-Shift-7 as Mod-/ (toggle comment). That is why the numbered list does not use Mod-Shift-7.

let heading1 =
    command "heading-1" "Heading 1" "swt:fluent--text-header-1-24-regular" (Some "Mod-1") (setHeading 1)

let heading2 =
    command "heading-2" "Heading 2" "swt:fluent--text-header-2-24-regular" (Some "Mod-2") (setHeading 2)

let heading3 =
    command "heading-3" "Heading 3" "swt:fluent--text-header-3-24-regular" (Some "Mod-3") (setHeading 3)

let bold =
    command "bold" "Bold" "swt:fluent--text-bold-24-regular" (Some "Mod-b") toggleBold

let italic =
    command "italic" "Italic" "swt:fluent--text-italic-24-regular" (Some "Mod-i") toggleItalic

let strikethrough =
    command
        "strikethrough"
        "Strikethrough"
        "swt:fluent--text-strikethrough-24-regular"
        (Some "Mod-Shift-x")
        toggleStrikethrough

let inlineCode =
    command "inline-code" "Inline code" "swt:fluent--code-24-regular" (Some "Mod-e") toggleInlineCode

let codeBlock =
    command "code-block" "Code block" "swt:fluent--code-block-24-regular" (Some "Mod-Shift-e") (insertCodeBlock >> Ok)

let link =
    command "link" "Link" "swt:fluent--link-24-regular" (Some "Mod-k") insertLink

let image =
    command "image" "Image" "swt:fluent--image-24-regular" (Some "Mod-Shift-p") insertImage

let quote =
    command "quote" "Quote" "swt:fluent--text-quote-24-regular" (Some "Mod-Shift-.") toggleQuote

let bulletList =
    command
        "bulleted-list"
        "Bulleted list"
        "swt:fluent--text-bullet-list-ltr-24-regular"
        (Some "Mod-Shift-8")
        toggleBulletList

let numberedList =
    command
        "numbered-list"
        "Numbered list"
        "swt:fluent--text-number-list-ltr-24-regular"
        (Some "Mod-Shift-n")
        toggleNumberedList

let taskList =
    command "task-list" "Task list" "swt:fluent--task-list-ltr-24-regular" (Some "Mod-Shift-c") toggleTaskList

let table =
    command "table" "Table" "swt:fluent--table-24-regular" (Some "Mod-Shift-t") (insertTable >> Ok)

/// The key that opens the ontology reference dialog of the toolbar.
let ontologyReferenceKey = "Mod-Shift-o"

let headings: MarkdownCommand[] = [| heading1; heading2; heading3 |]

/// The button groups of the toolbar. The toolbar draws the heading menu before the first group.
let groups: MarkdownCommand[][] = [|
    [| bold; italic; strikethrough |]
    [| inlineCode; codeBlock; link; image; quote |]
    [| bulletList; numberedList; taskList |]
    [| table |]
|]

/// Every command of the toolbar, the headings first.
let all: MarkdownCommand[] = Array.append headings (Array.concat groups)

/// The key bindings of the commands that have a key, and ontologyReferenceKey, which calls openOntologyReference.
/// A refused command reports its reason through report and consumes the key without a change, because returning
/// None would hand Mod-i to selectParentSyntax. A successful one reports None. Build the array once per host with
/// stable functions (for example React state setters inside React.useMemo with no dependencies) and pass the same
/// instance to the editor, so refusals by key show in the toolbar and the key opens the toolbar's dialog.
let keyBindingsWith (report: string option -> unit) (openOntologyReference: unit -> unit) : TextEditorKeyBinding[] =
    let unchanged (state: TextEditState) : TextEdit = {|
        changes = [||]
        selection = state.selection
    |}

    [|
        for cmd in all do
            match cmd.Key with
            | Some key -> {|
                key = key
                run =
                    fun state ->
                        match cmd.Run state with
                        | Ok edit ->
                            report None
                            Some edit
                        | Error reason ->
                            report (Some reason)
                            Some(unchanged state)
              |}
            | None -> ()

        {|
            key = ontologyReferenceKey
            run =
                fun state ->
                    openOntologyReference ()
                    Some(unchanged state)
        |}
    |]
