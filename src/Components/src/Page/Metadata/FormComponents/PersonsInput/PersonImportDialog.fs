namespace Swate.Components.Page.Metadata.FormComponents.PersonsInput

open ARCtrl
open Fable.Core
open Feliz
open Swate.Components
open Swate.Components.Primitive.Dialog

module private PersonImportDialogTypes =
    [<RequireQualifiedAccess>]
    type LoadState =
        | Loading
        | Loaded of Person[]
        | Failed of string

open PersonImportDialogTypes

[<Erase; Mangle(false)>]
type PersonImportDialog =

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

                            Html.label [
                                prop.key index
                                prop.className
                                    "swt:flex swt:items-start swt:gap-3 swt:rounded swt:border swt:border-base-300 swt:p-3"
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
                                        prop.className "swt:flex swt:flex-col swt:wrap-break-word"
                                        prop.children [
                                            Html.span [ prop.className "swt:font-semibold"; prop.text name ]
                                            for detail in
                                                [ person.ORCID; person.EMail; person.Affiliation ] |> List.choose id do
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
