namespace Arcitisa.Components.Page

open Browser.Types
open Fable.Core
open Feliz
open Arcitisa.Components.Page.GitSidebarTypes
open Arcitisa.Components.Page.GitSidebarHelper

[<Erase; Mangle(false)>]
type GitSidebarParts =

    [<ReactComponent>]
    static member SectionHeader(title: string, countText: string option) =
        Html.div [
            prop.className
                "swt:flex swt:min-w-0 swt:items-center swt:justify-between swt:gap-2 swt:px-3 swt:pt-3 swt:text-xs swt:font-semibold swt:uppercase swt:tracking-[0.2em] swt:text-base-content/60 swt:@max-xs:px-2 swt:@max-xs:tracking-[0.12em]"
            prop.children [
                Html.span [
                    prop.className "swt:min-w-0 swt:truncate"
                    prop.text title
                ]
                match countText with
                | Some value ->
                    Html.span [
                        prop.className "swt:shrink-0 swt:text-[0.65rem] swt:text-base-content/50"
                        prop.text value
                    ]
                | None -> Html.none
            ]
        ]

    [<ReactComponent>]
    static member ChangeStatusIcon(change: GitSidebarChange, testId: string) =
        let presentation = changePresentation change
        let stopRowActivation (event: MouseEvent) = event.stopPropagation ()

        Html.span [
            prop.testId testId
            prop.className [
                "swt:shrink-0 swt:text-center"
                presentation.ToneClassName
            ]
            prop.text presentation.Shorthand
            prop.title presentation.Label
            prop.ariaLabel presentation.Label
            prop.onMouseDown stopRowActivation
            prop.onClick stopRowActivation
        ]

    [<ReactComponent>]
    static member ChangeLabel
        (change: GitSidebarChange, nameTestId: string, directoryTestId: string, renamedFromTestId: string)
        =
        let fileName, directory = splitPath change.Path

        Html.div [
            prop.className "swt:min-w-0 swt:flex-1"
            prop.children [
                Html.div [
                    prop.className "swt:min-w-0"
                    prop.children [
                        // One truncating line: the ellipsis cuts the directory first and the file name only when it does not fit alone.
                        Html.span [
                            prop.className "swt:block swt:min-w-0 swt:truncate swt:text-sm swt:@max-xs:text-xs"
                            prop.children [
                                Html.span [ prop.testId nameTestId; prop.text fileName ]
                                match directory with
                                | Some directory ->
                                    // The space keeps assistive tech from reading name and directory as one word.
                                    React.Fragment [
                                        Html.text " "
                                        Html.span [
                                            prop.testId directoryTestId
                                            prop.className "swt:ml-1 swt:text-xs swt:text-base-content/60"
                                            prop.text directory
                                        ]
                                    ]
                                | None -> Html.none
                            ]
                        ]
                        match change.OriginalPath with
                        | Some originalPath ->
                            Html.div [
                                prop.testId renamedFromTestId
                                prop.className "swt:mt-0.5 swt:truncate swt:text-xs swt:text-base-content/60"
                                prop.title originalPath
                                prop.text $"Renamed from {originalPath}"
                            ]
                        | None -> Html.none
                    ]
                ]
            ]
        ]
