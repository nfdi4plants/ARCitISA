namespace Swate.Components.Page.Metadata.FormComponents.PersonsInput

open ARCtrl
open Fable.Core
open Feliz
open Swate.Components
open Swate.Components.Primitive.Dialog
open Swate.Components.Primitive.Popover

module private PersonImportDialogTypes =
    [<RequireQualifiedAccess>]
    type LoadState =
        | Loading
        | Loaded of Person[]
        | Failed of string

open PersonImportDialogTypes

module private PersonImportDialogHelper =
    let displayText value =
        value
        |> Option.filter (System.String.IsNullOrWhiteSpace >> not)
        |> Option.defaultValue "—"

[<Erase; Mangle(false)>]
type PersonImportDialog =

    [<ReactComponent>]
    static member private Comments(comments: ResizeArray<Comment>) =
        if comments.Count = 0 then
            Html.text "—"
        else
            Html.dl [
                prop.className "swt:space-y-1"
                prop.children [
                    for index, comment in Seq.indexed comments do
                        Html.div [
                            prop.key index
                            prop.children [
                                Html.dt [
                                    prop.className "swt:font-medium"
                                    prop.text (PersonImportDialogHelper.displayText comment.Name)
                                ]
                                Html.dd [
                                    prop.text (PersonImportDialogHelper.displayText comment.Value)
                                ]
                            ]
                        ]
                ]
            ]

    [<ReactComponent>]
    static member private InfoCard(person: Person) =
        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-2"
            prop.children [
                Popover.Heading(Html.text "Person details", className = "swt:text-lg swt:font-bold")
                Html.dl [
                    prop.className
                        "swt:grid swt:grid-cols-[auto_minmax(0,1fr)] swt:gap-x-3 swt:gap-y-2 swt:text-sm swt:whitespace-pre-wrap swt:wrap-break-word"
                    prop.children [
                        for label, value in
                            [
                                "First name", person.FirstName
                                "Last name", person.LastName
                                "Mid initials", person.MidInitials
                                "ORCID", person.ORCID
                                "Address", person.Address
                                "Affiliation", person.Affiliation
                                "Email", person.EMail
                                "Phone", person.Phone
                                "Fax", person.Fax
                            ] do
                            Html.dt [ prop.className "swt:font-semibold"; prop.text label ]
                            Html.dd [ prop.text (PersonImportDialogHelper.displayText value) ]
                        Html.dt [ prop.className "swt:font-semibold"; prop.text "Roles" ]
                        Html.dd [
                            if person.Roles.Count = 0 then
                                Html.text "—"
                            else
                                Html.ul [
                                    prop.className "swt:space-y-3"
                                    prop.children [
                                        for index, role in Seq.indexed person.Roles do
                                            Html.li [
                                                prop.key index
                                                prop.children [
                                                    Html.span [
                                                        prop.className "swt:font-medium"
                                                        prop.text (PersonImportDialogHelper.displayText role.Name)
                                                    ]
                                                    Html.dl [
                                                        prop.className "swt:space-y-1"
                                                        prop.children [
                                                            Html.dt [
                                                                prop.className "swt:font-medium"
                                                                prop.text "Term source"
                                                            ]
                                                            Html.dd [
                                                                prop.text (
                                                                    PersonImportDialogHelper.displayText
                                                                        role.TermSourceREF
                                                                )
                                                            ]
                                                            Html.dt [
                                                                prop.className "swt:font-medium"
                                                                prop.text "Accession"
                                                            ]
                                                            Html.dd [
                                                                prop.text (
                                                                    PersonImportDialogHelper.displayText
                                                                        role.TermAccessionNumber
                                                                )
                                                            ]
                                                            Html.dt [
                                                                prop.className "swt:font-medium"
                                                                prop.text "Comments"
                                                            ]
                                                            Html.dd [ PersonImportDialog.Comments role.Comments ]
                                                        ]
                                                    ]
                                                ]
                                            ]
                                    ]
                                ]
                        ]
                        Html.dt [ prop.className "swt:font-semibold"; prop.text "Comments" ]
                        Html.dd [ PersonImportDialog.Comments person.Comments ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member PersonImportDialog
        (persons: ResizeArray<Person>, load: unit -> JS.Promise<Person[]>, apply: Person[] -> unit, close: unit -> unit)
        =
        let state, setState = React.useState LoadState.Loading
        let selected, setSelected = React.useState Set.empty<int>
        let requestVersion = React.useRef 0

        let invalidate () =
            requestVersion.current <- requestVersion.current + 1

        let dismiss () =
            invalidate ()
            close ()

        let fetchPersons () =
            invalidate ()
            let version = requestVersion.current
            setState LoadState.Loading
            setSelected Set.empty

            promise {
                try
                    let! loaded = load ()

                    if requestVersion.current = version then
                        loaded
                        |> Array.distinct
                        |> Array.sortBy (fun person -> person.LastName, person.FirstName)
                        |> LoadState.Loaded
                        |> setState
                with error ->
                    if requestVersion.current = version then
                        setState (LoadState.Failed error.Message)
            }
            |> Promise.start

        React.useEffectOnce (fun () ->
            fetchPersons ()
            FsReact.createDisposable invalidate
        )

        let importSelected () =
            match state with
            | LoadState.Loaded loaded when not selected.IsEmpty ->
                loaded
                |> Array.indexed
                |> Array.choose (fun (index, person) ->
                    if selected.Contains index && not (persons.Contains person) then
                        Some(person.Copy())
                    else
                        None
                )
                |> apply

                dismiss ()
            | _ -> ()

        Dialog.Dialog(
            true,
            (fun isOpen ->
                if not isOpen then
                    dismiss ()
            ),
            Html.text "Import persons",
            Html.div [
                prop.className "swt:space-y-3"
                prop.children [
                    match state with
                    | LoadState.Loading -> Html.p [ prop.role "status"; prop.text "Loading persons…" ]
                    | LoadState.Failed message ->
                        Html.p [
                            prop.role "alert"
                            prop.className "swt:text-error"
                            prop.text message
                        ]

                        Html.button [
                            prop.type'.button
                            prop.className "swt:btn swt:btn-outline"
                            prop.onClick (fun _ -> fetchPersons ())
                            prop.text "Retry"
                        ]
                    | LoadState.Loaded loaded when loaded.Length = 0 ->
                        Html.p [ prop.text "No persons available to import." ]
                    | LoadState.Loaded loaded ->
                        for index, person in Array.indexed loaded do
                            let alreadyAdded = persons.Contains person

                            let name =
                                [ person.FirstName; person.MidInitials; person.LastName ]
                                |> List.choose id
                                |> String.concat " "
                                |> fun name -> if name.Trim() = "" then "Unnamed person" else name

                            Html.div [
                                prop.key index
                                prop.className
                                    "swt:flex swt:items-start swt:gap-3 swt:rounded swt:border swt:border-base-300 swt:p-3"
                                prop.children [
                                    Html.label [
                                        prop.className "swt:flex swt:min-w-0 swt:grow swt:cursor-pointer swt:gap-3"
                                        prop.children [
                                            Html.input [
                                                prop.type'.checkbox
                                                prop.className "swt:checkbox"
                                                prop.ariaLabel $"Import {name}"
                                                prop.disabled alreadyAdded
                                                prop.isChecked (selected.Contains index)
                                                prop.onChange (fun (isSelected: bool) ->
                                                    setSelected (
                                                        if isSelected then
                                                            selected.Add index
                                                        else
                                                            selected.Remove index
                                                    )
                                                )
                                            ]
                                            Html.span [
                                                prop.className "swt:flex swt:min-w-0 swt:flex-col swt:wrap-break-word"
                                                prop.children [
                                                    Html.span [ prop.className "swt:font-semibold"; prop.text name ]
                                                    for detail in
                                                        [ person.ORCID; person.EMail; person.Affiliation ]
                                                        |> List.choose id do
                                                        Html.span [ prop.className "swt:text-sm"; prop.text detail ]
                                                    if alreadyAdded then
                                                        Html.span [
                                                            prop.className "swt:text-sm swt:opacity-60"
                                                            prop.text "Already added"
                                                        ]
                                                ]
                                            ]
                                        ]
                                    ]
                                    Popover.Simple(
                                        trigger =
                                            Html.button [
                                                prop.type'.button
                                                prop.className "swt:btn swt:btn-square"
                                                prop.ariaLabel $"More info about {name}"
                                                prop.title $"More info about {name}"
                                                prop.children [
                                                    Html.span [
                                                        prop.ariaHidden true
                                                        prop.className "swt:iconify swt:fluent--info-20-regular"
                                                    ]
                                                ]
                                            ],
                                        triggerClassName = "swt:shrink-0",
                                        content = PersonImportDialog.InfoCard person,
                                        contentClassName =
                                            "swt:max-h-[min(36rem,calc(100dvh-2rem))] swt:overflow-y-auto",
                                        placement = FloatingUI.Placement.Left
                                    )
                                ]
                            ]
                ]
            ],
            description = Html.text "Select the persons to add.",
            footer =
                Html.div [
                    prop.className "swt:flex swt:justify-end swt:gap-2 swt:w-full"
                    prop.children [
                        Html.button [
                            prop.type'.button
                            prop.className "swt:btn swt:btn-ghost"
                            prop.text "Cancel"
                            prop.onClick (fun _ -> dismiss ())
                        ]
                        Html.button [
                            prop.type'.button
                            prop.className "swt:btn swt:btn-primary"
                            prop.text "Import selected"
                            prop.disabled selected.IsEmpty
                            prop.onClick (fun _ -> importSelected ())
                        ]
                    ]
                ]
        )
