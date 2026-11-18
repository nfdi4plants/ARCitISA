namespace Swate.Components.Composite.MarkdownText

open Fable.Core
open Feliz
open Swate.Components.Primitive.Dropdown
open Swate.Components.Util.UserOS
open Swate.Components.Composite.TermSearch.Types
open Swate.Components.Composite.MarkdownText.Editing.Types
open Swate.Components.Composite.MarkdownText.Editing.MarkdownCommands

[<Erase; Mangle(false)>]
type MarkdownToolbar =

    /// Formatting buttons for Markdown text on a surface. disabledReason is the tooltip of every button while disabled.
    /// lookupIri, termSearchQueries and disableDefaultSearch go to the ontology reference dialog.
    /// The toolbar shows the reason of a refused command. It keeps that message itself unless setMessage is given.
    /// Then the host owns the message (message and setMessage), so refusals of the keys from
    /// MarkdownCommands.keyBindingsWith show in the toolbar too.
    [<ReactComponent>]
    static member MarkdownToolbar
        (
            surface: MarkdownSurface,
            ?disabled: bool,
            ?disabledReason: string,
            ?message: string,
            ?setMessage: string option -> unit,
            ?className: string,
            ?lookupIri: string -> JS.Promise<string option>,
            ?termSearchQueries: ResizeArray<string * SearchCall>,
            ?disableDefaultSearch: bool
        ) =
        let disabled = defaultArg disabled false
        let ownMessage, setOwnMessage = React.useState (None: string option)

        let message, setMessage =
            match setMessage with
            | Some set -> message, set
            | None -> ownMessage, setOwnMessage

        let isDialogOpen, setDialogOpen = React.useState false
        let isHeadingOpen, setHeadingOpen = React.useState false

        let isMac =
            match getUserOS () with
            | MacOS
            | IOS -> true
            | _ -> false

        let run (cmd: MarkdownCommand) =
            match cmd.Run(surface.getState ()) with
            | Ok edit ->
                if surface.apply edit then
                    setMessage None
                else
                    setMessage (Some "The editor did not accept the change.")
            | Error reason -> setMessage (Some reason)

            surface.focus ()

        let icon (iconClass: string) =
            Html.span [
                prop.className ("swt:iconify " + iconClass + " swt:size-5")
                prop.ariaHidden true
            ]

        let title (cmd: MarkdownCommand) =
            match disabled, disabledReason, cmd.Key with
            | true, Some reason, _ -> reason
            | _, _, Some key -> cmd.Label + " (" + shortcutLabel isMac key + ")"
            | _ -> cmd.Label

        let commandButton (cmd: MarkdownCommand) =
            Html.button [
                prop.key cmd.Id
                prop.type'.button
                prop.className "swt:btn swt:btn-ghost swt:btn-sm swt:btn-square swt:join-item"
                prop.ariaLabel cmd.Label
                prop.title (title cmd)
                prop.disabled disabled
                prop.onClick (fun _ -> run cmd)
                prop.children [ icon cmd.Icon ]
            ]

        let headingButtonContent = [
            icon "swt:fluent--text-font-size-24-regular"
            Html.span [
                prop.className "swt:iconify swt:fluent--chevron-down-24-regular swt:size-3"
                prop.ariaHidden true
            ]
        ]

        let headingMenu =
            if disabled then
                Html.button [
                    prop.key "heading"
                    prop.type'.button
                    prop.className "swt:btn swt:btn-ghost swt:btn-sm swt:join-item"
                    prop.ariaLabel "Heading"
                    prop.title (defaultArg disabledReason "Heading")
                    prop.disabled true
                    prop.children headingButtonContent
                ]
            else
                Dropdown.Main(
                    isHeadingOpen,
                    setHeadingOpen,
                    Html.button [
                        prop.type'.button
                        prop.className "swt:btn swt:btn-ghost swt:btn-sm swt:join-item"
                        prop.ariaLabel "Heading"
                        prop.title "Heading"
                        prop.ariaExpanded isHeadingOpen
                        prop.onClick (fun _ -> setHeadingOpen (not isHeadingOpen))
                        prop.children headingButtonContent
                    ],
                    React.Fragment [
                        for cmd in headings do
                            Html.li [
                                prop.key cmd.Id
                                prop.children [
                                    Html.button [
                                        prop.type'.button
                                        prop.onClick (fun _ -> run cmd)
                                        prop.children [ icon cmd.Icon; Html.span [ prop.text cmd.Label ] ]
                                    ]
                                ]
                            ]
                    ],
                    dropdownClassName = "swt:join-item",
                    contentClassName =
                        "swt:menu swt:bg-base-100 swt:rounded-box swt:shadow-sm swt:z-10 swt:p-1 swt:w-40",
                    closeOnClick = true
                )

        Html.div [
            prop.role "toolbar"
            prop.ariaLabel "Markdown formatting"
            prop.className ("swt:flex swt:flex-wrap swt:items-center swt:gap-2 " + defaultArg className "")
            prop.children [
                for i in 0 .. groups.Length - 1 do
                    Html.div [
                        prop.key i
                        prop.className "swt:join"
                        prop.children [
                            if i = 0 then
                                headingMenu
                            for cmd in groups.[i] do
                                commandButton cmd
                        ]
                    ]
                Html.div [
                    prop.key "ontology"
                    prop.className "swt:join"
                    prop.children [
                        Html.button [
                            prop.type'.button
                            prop.className "swt:btn swt:btn-ghost swt:btn-sm swt:btn-square swt:join-item"
                            prop.ariaLabel "Ontology reference"
                            prop.title (
                                if disabled then
                                    defaultArg disabledReason "Ontology reference"
                                else
                                    "Ontology reference"
                            )
                            prop.disabled disabled
                            prop.onClick (fun _ -> setDialogOpen true)
                            prop.children [ icon "swt:fluent--book-search-24-regular" ]
                        ]
                    ]
                ]
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
        ]
