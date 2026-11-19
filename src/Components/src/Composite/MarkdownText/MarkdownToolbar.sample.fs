namespace Swate.Components.Composite.MarkdownToolbarSample

open Fable.Core
open Feliz

open Swate.Components.Composite.TextEditor
open Swate.Components.Composite.TextEditor.Types
open Swate.Components.Composite.TermSearch.Types
open Swate.Components.Composite.MarkdownText
open Swate.Components.Composite.MarkdownText.Editing
open Swate.Components.Composite.MarkdownText.Editing.Types
open Swate.Components.Composite.MarkdownText.Links
open Swate.Components.Composite.MarkdownText.Links.Types

module private MarkdownToolbarSampleHelpers =

    /// Built once, so the editor keeps its keymap. The read-only sample shows no messages and runs no key.
    let readOnlyKeyBindings = MarkdownCommands.keyBindingsWith ignore ignore

    let noHistory: TextEditorHistory = {| canUndo = false; canRedo = false |}

    let private sampleFolders: Map<string, FolderEntry[]> =
        let folder (name: string) : FolderEntry = {| name = name; isDirectory = true |}
        let file (name: string) : FolderEntry = {| name = name; isDirectory = false |}

        Map [
            "",
            [|
                folder ".arc"
                folder "docs"
                folder "images"
                file "README.md"
            |]
            ".arc", [| file "validation_packages.yml" |]
            "docs", [| folder "sub"; file "guide.md"; file "other.md" |]
            "docs/sub", [| file "deep.md" |]
            "images", [| file "logo.png"; file "photo one.jpg"; file "scan.tif" |]
        ]

    /// Lists a folder of a small stubbed ARC like the app does: the names that start with the prefix, ignoring case.
    let listSampleFolder (request: ListFolderRequest) : JS.Promise<FolderContents option> = promise {
        let prefix = request.namePrefix.ToLowerInvariant()

        return
            sampleFolders
            |> Map.tryFind request.folder
            |> Option.map (fun entries -> {|
                entries =
                    entries
                    |> Array.filter (fun entry -> entry.name.ToLowerInvariant().StartsWith prefix)
                truncated = false
            |})
    }

open MarkdownToolbarSampleHelpers

[<Erase; Mangle(false)>]
type internal MarkdownToolbarSample =

    /// The toolbar over a Markdown editor. The keys of the toolbar commands work in the editor too, and Undo and Redo
    /// follow the editor's history.
    [<ReactComponent>]
    static member Editor() =
        let handleRef = React.useRef<TextEditorHandle option> None
        let surface = MarkdownSurface.ofTextEditor handleRef
        let message, setMessage = React.useState (None: string option)
        let referenceOpen, setReferenceOpen = React.useState false
        let history, setHistory = React.useState noHistory

        let keyBindings =
            React.useMemo (
                (fun () -> MarkdownCommands.keyBindingsWith setMessage (fun () -> setReferenceOpen true)),
                [||]
            )

        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-2 swt:p-4"
            prop.children [
                MarkdownToolbar.MarkdownToolbar(
                    surface,
                    history,
                    ?message = message,
                    setMessage = setMessage,
                    isOntologyReferenceOpen = referenceOpen,
                    setOntologyReferenceOpen = setReferenceOpen
                )
                Html.div [
                    prop.className "swt:h-48"
                    prop.children [
                        TextEditor.TextEditor(
                            "inmemory://samples/toolbar.md",
                            "format me",
                            fileName = "toolbar.md",
                            keyBindings = keyBindings,
                            onChange = (fun () -> setMessage None),
                            onHistoryChange = setHistory,
                            handleRef = handleRef
                        )
                    ]
                ]
            ]
        ]

    /// The Ontology reference button, or its key in the editor, inserts a link for a term of a stubbed search.
    /// The IRI comes from the term.
    [<ReactComponent>]
    static member WithOntologyReference() =
        let handleRef = React.useRef<TextEditorHandle option> None
        let surface = MarkdownSurface.ofTextEditor handleRef
        let message, setMessage = React.useState (None: string option)
        let referenceOpen, setReferenceOpen = React.useState false
        let history, setHistory = React.useState noHistory

        let keyBindings =
            React.useMemo (
                (fun () -> MarkdownCommands.keyBindingsWith setMessage (fun () -> setReferenceOpen true)),
                [||]
            )

        let termSearchQueries =
            ResizeArray<string * SearchCall>(
                [
                    "stub",
                    fun (_: string) -> promise {
                        return
                            ResizeArray [
                                Term(
                                    name = "instrument model",
                                    id = "MS:1000031",
                                    source = "MS",
                                    href = "http://purl.obolibrary.org/obo/MS_1000031"
                                )
                            ]
                    }
                ]
            )

        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-2 swt:p-4"
            prop.children [
                MarkdownToolbar.MarkdownToolbar(
                    surface,
                    history,
                    ?message = message,
                    setMessage = setMessage,
                    isOntologyReferenceOpen = referenceOpen,
                    setOntologyReferenceOpen = setReferenceOpen,
                    termSearchQueries = termSearchQueries,
                    disableDefaultSearch = true,
                    lookupIri = (fun _ -> promise { return None })
                )
                Html.div [
                    prop.className "swt:h-48"
                    prop.children [
                        TextEditor.TextEditor(
                            "inmemory://samples/toolbar-ontology.md",
                            "The sample was measured with an ",
                            fileName = "toolbar.md",
                            keyBindings = keyBindings,
                            onChange = (fun () -> setMessage None),
                            onHistoryChange = setHistory,
                            handleRef = handleRef
                        )
                    ]
                ]
            ]
        ]

    /// A read-only editor disables every button.
    [<ReactComponent>]
    static member ReadOnly() =
        let handleRef = React.useRef<TextEditorHandle option> None
        let surface = MarkdownSurface.ofTextEditor handleRef
        let history, setHistory = React.useState noHistory

        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-2 swt:p-4"
            prop.children [
                MarkdownToolbar.MarkdownToolbar(
                    surface,
                    history,
                    disabled = true,
                    disabledReason = "This file is write-protected."
                )
                Html.div [
                    prop.className "swt:h-48"
                    prop.children [
                        TextEditor.TextEditor(
                            "inmemory://samples/toolbar-readonly.md",
                            "format me",
                            fileName = "toolbar.md",
                            readOnly = true,
                            keyBindings = readOnlyKeyBindings,
                            onHistoryChange = setHistory,
                            handleRef = handleRef
                        )
                    ]
                ]
            ]
        ]

    /// Path completion in docs/notes.md of a small stubbed ARC. A link or image target that starts with "/", "./" or
    /// "../" lists the folder it names, and a chosen folder lists its own entries next.
    [<ReactComponent>]
    static member WithPathCompletion() =
        let completions =
            React.useMemo ((fun () -> PathCompletion.source "docs" listSampleFolder), [||])

        Html.div [
            prop.className "swt:h-64 swt:p-4"
            prop.children [
                TextEditor.TextEditor(
                    "inmemory://samples/docs/notes.md",
                    "Links: ",
                    fileName = "notes.md",
                    completions = completions
                )
            ]
        ]
