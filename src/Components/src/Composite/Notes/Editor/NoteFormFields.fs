namespace Arcitisa.Components.Composite.Notes.Editor

open System
open ARCtrl
open Feliz
open Arcitisa.Components
open Arcitisa.Components.Composite.MarkdownText
open Arcitisa.Components.Composite.TermSearch
open Arcitisa.Components.Composite.TermSearch.Types
open Arcitisa.Components.Primitive.LayoutComponents

[<RequireQualifiedAccess>]
type NoteFormFields =

    [<ReactComponent>]
    static member Main(draft: NotesDraft, setDraft: NotesDraft -> unit) =
        let dateInputValue =
            draft.DateCreated
            |> Option.map (fun dateValue -> dateValue.ToString("yyyy-MM-dd"))
            |> Option.defaultValue ""

        let currentTagTerm, setCurrentTagTerm = React.useState (None: Term option)
        let currentTagTermRef = React.useRef currentTagTerm

        let setCurrentTagTermAndRef (nextTerm: Term option) =
            currentTagTermRef.current <- nextTerm
            setCurrentTagTerm nextTerm

        let tryCreateTagFromTerm (termOption: Term option) =
            termOption
            |> Option.bind (fun term ->
                term.name
                |> Option.bind (fun name ->
                    if String.IsNullOrWhiteSpace name then
                        None
                    else
                        Some(OntologyAnnotation.from term)
                )
            )

        let addTagFromTerm (termOption: Term option) =
            match tryCreateTagFromTerm termOption with
            | Some tag ->
                let alreadyExists =
                    draft.Tags
                    |> Seq.exists (fun existing ->
                        existing.Name = tag.Name
                        && existing.TermSourceREF = tag.TermSourceREF
                        && existing.TermAccessionNumber = tag.TermAccessionNumber
                    )

                if not alreadyExists then
                    let nextTags = ResizeArray draft.Tags
                    nextTags.Add tag
                    setDraft { draft with Tags = nextTags }

                setCurrentTagTermAndRef None
            | _ -> ()

        let addTagFromKeyboardEvent (keyboardEvent: Browser.Types.KeyboardEvent) =
            let termFromInput =
                match keyboardEvent.target with
                | :? Browser.Types.HTMLInputElement as inputElement when
                    not (String.IsNullOrWhiteSpace inputElement.value)
                    ->
                    Some(Term(inputElement.value.Trim()))
                | _ -> None

            let termToAdd =
                match currentTagTermRef.current with
                | Some term when term.name |> Option.exists (fun value -> not (String.IsNullOrWhiteSpace value)) ->
                    Some term
                | _ -> termFromInput

            addTagFromTerm termToAdd

        let removeTagAtIndex (indexToRemove: int) =
            if indexToRemove >= 0 && indexToRemove < draft.Tags.Count then
                let nextTags = ResizeArray draft.Tags
                nextTags.RemoveAt indexToRemove
                setDraft { draft with Tags = nextTags }

        let tagLabel (tag: OntologyAnnotation) =
            tag.Name
            |> Option.filter (fun name -> not (String.IsNullOrWhiteSpace name))
            |> Option.defaultValue "Tag"

        let tagKey (tag: OntologyAnnotation) =
            let name = tag.Name |> Option.defaultValue ""
            let source = tag.TermSourceREF |> Option.defaultValue ""
            let accession = tag.TermAccessionNumber |> Option.defaultValue ""
            $"{source}|{accession}|{name}"

        React.Fragment [
            Html.div [
                prop.testId "notes-title-field"
                prop.className "swt:fieldset swt:grow"
                prop.children [
                    LayoutComponents.FieldTitle "Title (Required)"
                    Html.input [
                        prop.className "swt:input swt:input-bordered swt:w-full"
                        prop.type'.text
                        prop.valueOrDefault draft.Title
                        prop.placeholder "Note title"
                        prop.onChange (fun value -> setDraft { draft with Title = value })
                    ]
                ]
            ]
            Html.div [
                prop.testId "notes-date-field"
                prop.className "swt:fieldset swt:w-full swt:max-w-36"
                prop.children [
                    LayoutComponents.FieldTitle "Date Created (Required)"
                    Html.input [
                        prop.className "swt:input swt:input-bordered swt:w-full"
                        prop.type'.date
                        prop.valueOrDefault dateInputValue
                        prop.onChange (fun value ->
                            let parsedDate = Validation.tryParseDateCreated value
                            setDraft { draft with DateCreated = parsedDate }
                        )
                    ]
                ]
            ]
            Html.div [
                prop.testId "notes-tags-field"
                prop.className "swt:fieldset swt:space-y-2"
                prop.children [
                    LayoutComponents.FieldTitle "Tags (Optional)"
                    Html.div [
                        prop.className "swt:w-full swt:relative"
                        prop.children [
                            TermSearch.TermSearch(
                                currentTagTerm,
                                setCurrentTagTermAndRef,
                                onTermSelect = (fun selectedTerm -> addTagFromTerm (Some selectedTerm)),
                                onKeyDown =
                                    (fun keyboardEvent ->
                                        if keyboardEvent.code = kbdEventCode.enter then
                                            keyboardEvent.preventDefault ()
                                            keyboardEvent.stopPropagation ()
                                            addTagFromKeyboardEvent keyboardEvent
                                    ),
                                classNames = TermSearchStyle(Fable.Core.U2.Case1 "swt:w-full")
                            )
                        ]
                    ]
                    if draft.Tags.Count = 0 then
                        Html.p [
                            prop.className "swt:text-xs swt:opacity-70"
                            prop.text "No tags added."
                        ]
                    else
                        Html.ul [
                            prop.className "swt:flex swt:flex-col swt:gap-1"
                            prop.children [
                                for index, tag in draft.Tags |> Seq.indexed do
                                    Html.li [
                                        prop.key $"note-tag-{tagKey tag}"
                                        prop.className
                                            "swt:flex swt:items-center swt:gap-2 swt:rounded-box swt:border swt:border-base-300 swt:px-2 swt:py-1"
                                        prop.children [
                                            Html.span [
                                                prop.className "swt:grow swt:text-sm swt:break-all"
                                                prop.text (tagLabel tag)
                                            ]
                                            Html.button [
                                                prop.type'.button
                                                prop.className "swt:btn swt:btn-xs swt:btn-ghost"
                                                prop.ariaLabel $"Remove tag {tagLabel tag}"
                                                prop.text "x"
                                                prop.onClick (fun _ -> removeTagAtIndex index)
                                            ]
                                        ]
                                    ]
                            ]
                        ]
                ]
            ]
            Html.div [
                prop.testId "notes-main-text-field"
                prop.children [
                    MarkdownTextEditor.MarkdownTextEditor(
                        draft.MainText,
                        (fun value -> setDraft { draft with MainText = value }),
                        height = 360,
                        label = "Main Text",
                        placeholder = "Write note markdown..."
                    )
                ]
            ]
        ]
