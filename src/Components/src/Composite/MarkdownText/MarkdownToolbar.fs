namespace Swate.Components.Composite.MarkdownText

open Fable.Core
open Feliz
open Swate.Components.Primitive.Dropdown
open Swate.Components.Composite.TextEditor
open Swate.Components.Util
open Swate.Components.Composite.TextEditor.Types
open Swate.Components.Composite.TermSearch.Types
open Swate.Components.Composite.EditorToolbar
open Swate.Components.Composite.MarkdownText.Editing.Types
open Swate.Components.Composite.MarkdownText.Editing.MarkdownCommands

[<Erase; Mangle(false)>]
type MarkdownToolbar =

    /// An EditorToolbar with Undo, Redo and formatting buttons for Markdown text on a surface. Undo and Redo step the
    /// surface, each enabled only while history holds a step to take. The host keeps history current, for example
    /// from TextEditor's onHistoryChange. disabledReason is the tooltip of every button while disabled.
    /// lookupIri, termSearchQueries and disableDefaultSearch go to the ontology reference dialog.
    /// The toolbar shows the reason of a refused command. It keeps that message itself unless setMessage is given.
    /// Then the host owns the message (message and setMessage), so refusals of the keys from
    /// MarkdownCommands.keyBindingsWith show in the toolbar too. In the same way the toolbar keeps the open state of
    /// the ontology reference dialog unless setOntologyReferenceOpen is given. Then the host owns it
    /// (isOntologyReferenceOpen and setOntologyReferenceOpen), so MarkdownCommands.ontologyReferenceKey opens it.
    /// trailing goes to the end of the bar.
    [<ReactComponent>]
    static member MarkdownToolbar
        (
            surface: MarkdownSurface,
            history: TextEditorHistory,
            ?disabled: bool,
            ?disabledReason: string,
            ?message: string,
            ?setMessage: string option -> unit,
            ?isOntologyReferenceOpen: bool,
            ?setOntologyReferenceOpen: bool -> unit,
            ?className: string,
            ?lookupIri: string -> JS.Promise<string option>,
            ?termSearchQueries: ResizeArray<string * SearchCall>,
            ?disableDefaultSearch: bool,
            ?trailing: ReactElement
        ) =
        let disabled = defaultArg disabled false
        let ownMessage, setOwnMessage = React.useState (None: string option)

        let message, setMessage =
            match setMessage with
            | Some set -> message, set
            | None -> ownMessage, setOwnMessage

        let ownDialogOpen, setOwnDialogOpen = React.useState false

        let isDialogOpen, setDialogOpen =
            match setOntologyReferenceOpen with
            | Some set -> defaultArg isOntologyReferenceOpen false, set
            | None -> ownDialogOpen, setOwnDialogOpen

        let isHeadingOpen, setHeadingOpen = React.useState false
        let isMac = Shortcuts.isMac ()

        let run (cmd: MarkdownCommand) =
            match cmd.Run(surface.getState ()) with
            | Ok edit ->
                if surface.apply edit then
                    setMessage None
                else
                    setMessage (Some "The editor did not accept the change.")
            | Error reason -> setMessage (Some reason)

            surface.focus ()

        let stepHistory (step: unit -> bool) () =
            step () |> ignore
            setMessage None
            surface.focus ()

        let commandButton (cmd: MarkdownCommand) =
            EditorToolbar.ToolbarButton(
                cmd.Label,
                cmd.Icon,
                (fun () -> run cmd),
                shortcuts = Option.toArray cmd.Key,
                disabled = disabled,
                ?disabledReason = disabledReason,
                joined = true,
                key = cmd.Id
            )

        let headingIcon = "swt:fluent--text-font-size-24-regular"
        let headingKeys = headings |> Array.choose _.Key

        // The heading menu stands alone. Inside a join its dropdown wrapper became a join item, which drew a border
        // of its own around the button.
        let headingMenu =
            if disabled then
                EditorToolbar.ToolbarButton(
                    "Heading",
                    headingIcon,
                    ignore,
                    shortcuts = headingKeys,
                    disabled = true,
                    ?disabledReason = disabledReason,
                    menuOpen = false,
                    key = "heading"
                )
            else
                Dropdown.Main(
                    isHeadingOpen,
                    setHeadingOpen,
                    EditorToolbar.ToolbarButton(
                        "Heading",
                        headingIcon,
                        (fun () -> setHeadingOpen (not isHeadingOpen)),
                        shortcuts = headingKeys,
                        menuOpen = isHeadingOpen
                    ),
                    React.Fragment [
                        for cmd in headings do
                            Html.li [
                                prop.key cmd.Id
                                prop.children [
                                    Html.button [
                                        prop.type'.button
                                        prop.title (
                                            match cmd.Key with
                                            | Some key -> Shortcuts.title isMac cmd.Label key
                                            | None -> cmd.Label
                                        )
                                        prop.onClick (fun _ -> run cmd)
                                        prop.children [
                                            Html.span [
                                                prop.className ("swt:iconify " + cmd.Icon + " swt:size-5")
                                                prop.ariaHidden true
                                            ]
                                            Html.span [ prop.text cmd.Label ]
                                            match cmd.Key with
                                            | Some key ->
                                                Html.span [
                                                    prop.className "swt:ml-auto swt:text-xs swt:opacity-60"
                                                    prop.ariaHidden true
                                                    prop.text (Shortcuts.label isMac key)
                                                ]
                                            | None -> Html.none
                                        ]
                                    ]
                                ]
                            ]
                    ],
                    contentClassName =
                        "swt:menu swt:bg-base-100 swt:rounded-box swt:shadow-sm swt:z-10 swt:p-1 swt:w-48 swt:border swt:border-base-content/20",
                    closeOnClick = true
                )

        EditorToolbar.EditorToolbar(
            ariaLabel = "Markdown formatting",
            ?className = className,
            ?trailing = trailing,
            children =
                React.Fragment [
                    EditorToolbar.HistoryButtons(
                        history,
                        stepHistory surface.undo,
                        stepHistory surface.redo,
                        disabled = disabled,
                        ?disabledReason = disabledReason,
                        key = "history"
                    )
                    headingMenu
                    for i in 0 .. groups.Length - 1 do
                        Html.div [
                            prop.key i
                            prop.className "swt:join"
                            prop.children [
                                for cmd in groups.[i] do
                                    commandButton cmd
                            ]
                        ]
                    EditorToolbar.ToolbarButton(
                        "Ontology reference",
                        "swt:fluent--book-search-24-regular",
                        (fun () -> setDialogOpen true),
                        shortcuts = [| ontologyReferenceKey |],
                        disabled = disabled,
                        ?disabledReason = disabledReason,
                        key = "ontology"
                    )
                    match message with
                    | Some m ->
                        Html.span [
                            prop.role "status"
                            prop.className "swt:text-xs swt:text-error"
                            prop.text m
                        ]
                    | None -> Html.none
                    OntologyReferenceDialog.OntologyReferenceDialog(
                        isDialogOpen,
                        setDialogOpen,
                        surface,
                        ?lookupIri = lookupIri,
                        ?termSearchQueries = termSearchQueries,
                        ?disableDefaultSearch = disableDefaultSearch
                    )
                ]
        )
