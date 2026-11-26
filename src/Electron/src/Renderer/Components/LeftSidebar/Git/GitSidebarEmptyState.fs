namespace Renderer.Components.LeftSidebar.Git

open Fable.Core
open Feliz
open Arcitisa.Components.Primitive.Blankslate
open Arcitisa.Components.Primitive.Blankslate.Types
open Renderer.Components.LeftSidebar.Git.Types

[<Erase; Mangle(false)>]
type GitSidebarEmptyState =

    [<ReactComponent(true)>]
    static member Main
        (
            title: string,
            description: string,
            primaryAction: EmptyStateAction,
            ?secondaryAction: EmptyStateAction,
            ?iconClassName: string,
            ?infoText: string,
            ?extraContent: ReactElement
        ) =
        let iconClassName = defaultArg iconClassName "swt:fluent--info-24-regular"

        let secondaryActions =
            match secondaryAction with
            | Some action -> [
                BlankslateSecondaryAction.create (
                    action.Label,
                    action.OnClick,
                    iconClassName = action.IconClassName,
                    disabled = action.Disabled
                )
              ]
            | None -> []

        let primaryActions = [
            BlankslatePrimaryAction.create (
                primaryAction.Label,
                primaryAction.OnClick,
                iconClassName = primaryAction.IconClassName,
                disabled = primaryAction.Disabled
            )
        ]

        let trailingElement =
            match infoText with
            | Some info ->
                Some(
                    Html.div [
                        prop.className "swt:alert swt:alert-warning swt:px-3 swt:py-2 swt:text-sm"
                        prop.children [
                            Html.span [
                                prop.className "swt:iconify swt:fluent--warning-shield-20-regular swt:size-4"
                            ]
                            Html.span info
                        ]
                    ]
                )
            | None -> None

        Blankslate.Blankslate(
            title = title,
            description = description,
            iconClassName = iconClassName,
            primaryActions = primaryActions,
            secondaryActions = secondaryActions,
            ?leadingElement = extraContent,
            ?trailingElement = trailingElement,
            fullHeight = true,
            testId = "GitSidebarEmptyState"
        )
