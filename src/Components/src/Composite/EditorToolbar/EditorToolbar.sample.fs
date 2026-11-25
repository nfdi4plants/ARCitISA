namespace Swate.Components.Composite.EditorToolbarSample

open Fable.Core
open Feliz

open Swate.Components.Composite.TextEditor
open Swate.Components.Composite.TextEditor.Types
open Swate.Components.Composite.EditorToolbar

module private EditorToolbarSampleHelpers =

    let noHistory: TextEditorHistory = {| canUndo = false; canRedo = false |}

    /// Takes one step through the history of the editor and gives the focus back to it.
    let stepHistory (handleRef: IRefValue<TextEditorHandle option>) (step: TextEditorHandle -> bool) () =
        handleRef.current
        |> Option.iter (fun handle ->
            step handle |> ignore
            handle.focus ()
        )

open EditorToolbarSampleHelpers

[<Erase; Mangle(false)>]
type internal EditorToolbarSample =

    /// Undo and Redo over a YAML editor follow its history.
    [<ReactComponent>]
    static member Editor() =
        let handleRef = React.useRef<TextEditorHandle option> None
        let history, setHistory = React.useState noHistory

        Html.div [
            prop.className "swt:flex swt:flex-col swt:p-4"
            prop.children [
                EditorToolbar.EditorToolbar(
                    children =
                        EditorToolbar.HistoryButtons(
                            history,
                            stepHistory handleRef (fun handle -> handle.undo ()),
                            stepHistory handleRef (fun handle -> handle.redo ())
                        )
                )
                Html.div [
                    prop.className "swt:h-48"
                    prop.children [
                        TextEditor.TextEditor(
                            "inmemory://samples/toolbar.yml",
                            "title: Dilution series",
                            fileName = "toolbar.yml",
                            onHistoryChange = setHistory,
                            handleRef = handleRef
                        )
                    ]
                ]
            ]
        ]

    /// Over a read-only JSON editor Undo and Redo are disabled and give the reason.
    [<ReactComponent>]
    static member ReadOnly() =
        let handleRef = React.useRef<TextEditorHandle option> None
        let history, setHistory = React.useState noHistory

        Html.div [
            prop.className "swt:flex swt:flex-col swt:p-4"
            prop.children [
                EditorToolbar.EditorToolbar(
                    children =
                        EditorToolbar.HistoryButtons(
                            history,
                            stepHistory handleRef (fun handle -> handle.undo ()),
                            stepHistory handleRef (fun handle -> handle.redo ()),
                            disabled = true,
                            disabledReason = "This file is write-protected."
                        )
                )
                Html.div [
                    prop.className "swt:h-48"
                    prop.children [
                        TextEditor.TextEditor(
                            "inmemory://samples/toolbar-readonly.json",
                            """{ "title": "Dilution series" }""",
                            fileName = "toolbar.json",
                            readOnly = true,
                            onHistoryChange = setHistory,
                            handleRef = handleRef
                        )
                    ]
                ]
            ]
        ]
