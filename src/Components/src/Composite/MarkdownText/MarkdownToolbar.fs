namespace Swate.Components.Composite.MarkdownText

open Fable.Core
open Feliz
open Swate.Components.Primitive.Dropdown
open Swate.Components.Composite.TextEditor
open Swate.Components.Composite.TermSearch.Types
open Swate.Components.Composite.MarkdownText.Editing.Types
open Swate.Components.Composite.MarkdownText.Editing.MarkdownCommands

[<Erase; Mangle(false)>]
type MarkdownToolbar =

    /// Formatting buttons for Markdown text on a surface. disabledReason is the tooltip of every button while disabled.
    /// lookupIri, termSearchQueries and disableDefaultSearch go to the ontology reference dialog.
    /// The toolbar shows the reason of a refused command. It keeps that message itself unless setMessage is given.
    /// Then the host owns the message (message and setMessage), so refusals of the keys from
    /// MarkdownCommands.keyBindingsWith show in the toolbar too. In the same way the toolbar keeps the open state of
    /// the ontology reference dialog unless setOntologyReferenceOpen is given. Then the host owns it
    /// (isOntologyReferenceOpen and setOntologyReferenceOpen), so MarkdownCommands.ontologyReferenceKey opens it.
    [<ReactComponent>]
    static member MarkdownToolbar
        (
            surface: MarkdownSurface,
            ?disabled: bool,
            ?disabledReason: string,
            ?message: string,
            ?setMessage: string option -> unit,
            ?isOntologyReferenceOpen: bool,
            ?setOntologyReferenceOpen: bool -> unit,
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

        let icon (iconClass: string) =
            Html.span [
                prop.className ("swt:iconify " + iconClass + " swt:size-5")
                prop.ariaHidden true
            ]

        // The border color is a utility, so it wins over the transparent border of btn-ghost.
        let buttonClass = "swt:btn swt:btn-ghost swt:btn-sm swt:border-base-content/20"

        let withKey (label: string) (key: string option) =
            match key with
            | Some key -> Shortcuts.title isMac label key
            | None -> label

        let title (label: string) (key: string option) =
            match disabled, disabledReason with
            | true, Some reason -> reason
            | _ -> withKey label key

        let commandButton (cmd: MarkdownCommand) =
            Html.button [
                prop.key cmd.Id
                prop.type'.button
                prop.className (buttonClass + " swt:btn-square swt:join-item")
                prop.ariaLabel cmd.Label
                prop.title (title cmd.Label cmd.Key)
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

        let headingTitle =
            let keys = headings |> Array.choose _.Key |> Array.map (Shortcuts.label isMac)

            match disabled, disabledReason with
            | true, Some reason -> reason
            | _ when keys.Length = 0 -> "Heading"
            | _ -> "Heading (" + String.concat ", " keys + ")"

        // The heading menu stands alone. Inside a join its dropdown wrapper became a join item, which drew a border
        // of its own around the button.
        let headingMenu =
            if disabled then
                Html.button [
                    prop.key "heading"
                    prop.type'.button
                    prop.className buttonClass
                    prop.ariaLabel "Heading"
                    prop.title headingTitle
                    prop.disabled true
                    prop.children headingButtonContent
                ]
            else
                Dropdown.Main(
                    isHeadingOpen,
                    setHeadingOpen,
                    Html.button [
                        prop.type'.button
                        prop.className buttonClass
                        prop.ariaLabel "Heading"
                        prop.title headingTitle
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
                                        prop.title (withKey cmd.Label cmd.Key)
                                        prop.onClick (fun _ -> run cmd)
                                        prop.children [
                                            icon cmd.Icon
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

        Html.div [
            prop.role "toolbar"
            prop.ariaLabel "Markdown formatting"
            prop.className ("swt:flex swt:flex-wrap swt:items-center swt:gap-2 " + defaultArg className "")
            prop.children [
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
                Html.button [
                    prop.key "ontology"
                    prop.type'.button
                    prop.className (buttonClass + " swt:btn-square")
                    prop.ariaLabel "Ontology reference"
                    prop.title (title "Ontology reference" (Some ontologyReferenceKey))
                    prop.disabled disabled
                    prop.onClick (fun _ -> setDialogOpen true)
                    prop.children [ icon "swt:fluent--book-search-24-regular" ]
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
