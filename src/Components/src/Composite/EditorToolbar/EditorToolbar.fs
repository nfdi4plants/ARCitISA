namespace Swate.Components.Composite.EditorToolbar

open Fable.Core
open Feliz
open Swate.Components.Composite.TextEditor
open Swate.Components.Util

module private EditorToolbarHelpers =

    /// "Bold" with the key "Mod-b" becomes "Bold (Ctrl+B)". Several keys share the brackets, separated by commas.
    let titleWithKeys (isMac: bool) (label: string) (keys: string[]) =
        match keys with
        | [||] -> label
        | keys ->
            label
            + " ("
            + (keys |> Array.map (Shortcuts.label isMac) |> String.concat ", ")
            + ")"

open EditorToolbarHelpers

/// The bar above a file editor, the same for text, Markdown and ARC files. EditorToolbar draws the bar,
/// ToolbarButton the buttons in it and HistoryButtons the Undo and Redo of an editor with an undo history.
[<Erase; Mangle(false)>]
type EditorToolbar =

    /// An icon button for an editor toolbar. label is its accessible name and the start of its tooltip, which adds
    /// the keys of shortcuts (CodeMirror notation), for example "Bold (Ctrl+B)". A disabled button shows
    /// disabledReason as its tooltip instead, when given. joined makes it an item of a DaisyUI join, so a group of
    /// buttons shares one border. With menuOpen the button opens a menu: a chevron follows the icon and the button
    /// reports whether the menu is open. With pressed it is a toggle, for example for a panel, and shows when it is on.
    [<ReactComponent>]
    static member ToolbarButton
        (
            label: string,
            iconClass: string,
            onClick: unit -> unit,
            ?shortcuts: string[],
            ?disabled: bool,
            ?disabledReason: string,
            ?joined: bool,
            ?menuOpen: bool,
            ?pressed: bool,
            ?key: string
        ) =
        let disabled = defaultArg disabled false

        let title =
            match disabled, disabledReason with
            | true, Some reason -> reason
            | _ -> titleWithKeys (Shortcuts.isMac ()) label (defaultArg shortcuts [||])

        Html.button [
            prop.type'.button
            prop.className [
                // The border color is a utility, so it wins over the transparent border of btn-ghost.
                "swt:btn swt:btn-ghost swt:btn-sm swt:border-base-content/20"
                if menuOpen.IsNone then
                    "swt:btn-square"
                if defaultArg joined false then
                    "swt:join-item"
                if defaultArg pressed false then
                    "swt:btn-active swt:text-primary!"
            ]
            prop.ariaLabel label
            prop.title title
            prop.disabled disabled
            match menuOpen with
            | Some isOpen -> prop.ariaExpanded isOpen
            | None -> ()
            match pressed with
            | Some isPressed -> prop.ariaPressed isPressed
            | None -> ()
            prop.onClick (fun _ -> onClick ())
            prop.children [
                Html.span [
                    prop.className ("swt:iconify " + iconClass + " swt:size-5")
                    prop.ariaHidden true
                ]
                if menuOpen.IsSome then
                    Html.span [
                        prop.className "swt:iconify swt:fluent--chevron-down-24-regular swt:size-3"
                        prop.ariaHidden true
                    ]
            ]
        ]

    /// Undo and Redo as a joined pair. Each is enabled only while history holds a step to take. The host keeps
    /// history current, for example from TextEditor's onHistoryChange, and onUndo and onRedo take the step. The click
    /// moves the focus to the button, so they should give it back to the editor. disabled disables both, for example
    /// over a read-only editor, and disabledReason is their tooltip then.
    [<ReactComponent>]
    static member HistoryButtons
        (
            history: {| canUndo: bool; canRedo: bool |},
            onUndo: unit -> unit,
            onRedo: unit -> unit,
            ?disabled: bool,
            ?disabledReason: string,
            ?key: string
        ) =
        let disabled = defaultArg disabled false
        let isMac = Shortcuts.isMac ()
        // Without a step to take a button is disabled too, but its tooltip still names it and its key.
        let reason = if disabled then disabledReason else None

        Html.div [
            prop.className "swt:join"
            prop.children [
                EditorToolbar.ToolbarButton(
                    "Undo",
                    "swt:fluent--arrow-undo-24-regular",
                    onUndo,
                    shortcuts = [| Shortcuts.undoKey |],
                    disabled = (disabled || not history.canUndo),
                    ?disabledReason = reason,
                    joined = true,
                    key = "undo"
                )
                EditorToolbar.ToolbarButton(
                    "Redo",
                    "swt:fluent--arrow-redo-24-regular",
                    onRedo,
                    shortcuts = [| Shortcuts.redoKey isMac |],
                    disabled = (disabled || not history.canRedo),
                    ?disabledReason = reason,
                    joined = true,
                    key = "redo"
                )
            ]
        ]

    /// The bar itself: a toolbar row along the top of an editor, wrapping when the editor is narrow. children start
    /// at the left. trailing sits at the right end, and with onClose a "Close file" button comes last. The host
    /// decides what closing means, for example asking about unsaved changes first. ariaLabel names the toolbar for
    /// screen readers.
    [<ReactComponent>]
    static member EditorToolbar
        (
            ?ariaLabel: string,
            ?className: string,
            ?children: ReactElement,
            ?trailing: ReactElement,
            ?onClose: unit -> unit
        ) =
        Html.div [
            prop.role "toolbar"
            prop.ariaLabel (defaultArg ariaLabel "Editing")
            prop.className [
                "swt:flex swt:flex-wrap swt:items-center swt:gap-2 swt:px-3 swt:py-1 swt:bg-base-100 swt:border-b swt:border-base-300"
                defaultArg className ""
            ]
            prop.children [
                defaultArg children Html.none
                if trailing.IsSome || onClose.IsSome then
                    Html.div [
                        prop.key "end"
                        prop.className "swt:ml-auto swt:flex swt:items-center swt:gap-2"
                        prop.children [
                            defaultArg trailing Html.none
                            match onClose with
                            | Some close ->
                                EditorToolbar.ToolbarButton(
                                    "Close file",
                                    "swt:fluent--dismiss-24-regular",
                                    close,
                                    key = "close"
                                )
                            | None -> Html.none
                        ]
                    ]
            ]
        ]
