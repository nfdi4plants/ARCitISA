namespace Swate.Components.Primitive.Blankslate

open Fable.Core
open Feliz
open Swate.Components.Primitive
open Swate.Components.Primitive.Blankslate.Types

module private BlankslateHelper =

    let resolveTextClasses (textSize: BlankslateTextSize) =
        match textSize with
        | BlankslateTextSize.Small ->
            "swt:text-sm swt:font-semibold", "swt:text-xs swt:text-base-content/70", "swt:size-4"
        | BlankslateTextSize.Medium ->
            "swt:text-base swt:font-semibold", "swt:text-sm swt:text-base-content/70", "swt:size-5"
        | BlankslateTextSize.Large ->
            "swt:text-lg swt:font-semibold", "swt:text-base swt:text-base-content/70", "swt:size-6"

    let resolvePrimaryButtonClassName (color: DaisyuiColors) =
        let colorClassName =
            match color with
            | DaisyuiColors.Primary -> "swt:btn-primary"
            | DaisyuiColors.Secondary -> "swt:btn-secondary"
            | DaisyuiColors.Accent -> "swt:btn-accent"
            | DaisyuiColors.Info -> "swt:btn-info"
            | DaisyuiColors.Success -> "swt:btn-success"
            | DaisyuiColors.Warning -> "swt:btn-warning"
            | DaisyuiColors.Error -> "swt:btn-error"

        $"swt:btn {colorClassName} swt:gap-2"

open BlankslateHelper

[<Erase; Mangle(false)>]
type Blankslate =

    [<ReactComponent>]
    static member private PrimaryActionButton(action: BlankslatePrimaryAction) =
        let buttonClassName = resolvePrimaryButtonClassName action.Color

        Html.button [
            prop.className buttonClassName
            prop.disabled action.Disabled
            prop.onClick (fun _ -> action.OnClick())
            prop.children [
                match action.IconClassName with
                | Some iconClassName -> Html.span [ prop.className $"swt:iconify {iconClassName} swt:size-4" ]
                | None -> Html.none
                Html.span action.Label
            ]
        ]

    [<ReactComponent>]
    static member private SecondaryActionButton(action: BlankslateSecondaryAction) =
        Html.button [
            prop.className
                "swt:inline-flex swt:items-center swt:gap-1 swt:text-sm swt:link swt:link-hover swt:text-base-content/70"
            prop.disabled action.Disabled
            prop.onClick (fun _ -> action.OnClick())
            prop.children [
                match action.IconClassName with
                | Some iconClassName -> Html.span [ prop.className $"swt:iconify {iconClassName} swt:size-4" ]
                | None -> Html.none
                Html.span action.Label
            ]
        ]

    [<ReactComponent(true)>]
    static member Blankslate
        (
            title: string,
            ?description: string,
            ?iconClassName: string,
            ?textSize: BlankslateTextSize,
            ?primaryActions: BlankslatePrimaryAction list,
            ?secondaryActions: BlankslateSecondaryAction list,
            ?leadingElement: ReactElement,
            ?trailingElement: ReactElement,
            ?fullHeight: bool,
            ?testId: string,
            ?className: string
        ) : ReactElement =
        let textSize = defaultArg textSize BlankslateTextSize.Medium

        let titleClassName, descriptionClassName, iconSizeClassName =
            resolveTextClasses textSize

        let fullHeight = defaultArg fullHeight true
        let primaryActions = defaultArg primaryActions []
        let secondaryActions = defaultArg secondaryActions []

        Html.div [
            if testId.IsSome then
                prop.testId testId.Value
            prop.className [
                "swt:flex swt:flex-col swt:justify-center swt:p-4"
                if fullHeight then
                    "swt:h-full"
                match className with
                | Some customClassName -> customClassName
                | None -> ()
            ]
            prop.children [
                Html.div [
                    prop.className
                        "swt:rounded-box swt:border swt:border-base-content/10 swt:bg-base-100 swt:p-4 swt:flex swt:flex-col swt:items-center swt:text-center swt:gap-4"
                    prop.children [
                        Html.div [
                            prop.className "swt:flex swt:items-center swt:gap-2 swt:font-semibold"
                            prop.children [
                                match iconClassName with
                                | Some iconClass ->
                                    Html.span [
                                        prop.className $"swt:iconify {iconClass} {iconSizeClassName}"
                                    ]
                                | None -> Html.none
                                Html.h2 [ prop.className titleClassName; prop.text title ]
                            ]
                        ]
                        match description with
                        | Some contentText ->
                            Html.p [
                                prop.className $"swt:mt-2 {descriptionClassName}"
                                prop.text contentText
                            ]
                        | None -> Html.none
                        match leadingElement with
                        | Some content -> Html.div [ prop.className "swt:mt-4"; prop.children [ content ] ]
                        | None -> Html.none
                        if not primaryActions.IsEmpty then
                            Html.div [
                                prop.className "swt:mt-4 swt:flex swt:flex-wrap swt:justify-center swt:gap-2"
                                prop.children (primaryActions |> List.map Blankslate.PrimaryActionButton)
                            ]
                        if not secondaryActions.IsEmpty then
                            Html.div [
                                prop.className [
                                    "swt:flex swt:flex-wrap swt:justify-center swt:gap-x-4 swt:gap-y-1"
                                    if primaryActions.IsEmpty then "swt:mt-4" else "swt:mt-1"
                                ]
                                prop.children (secondaryActions |> List.map Blankslate.SecondaryActionButton)
                            ]
                        match trailingElement with
                        | Some content -> Html.div [ prop.className "swt:mt-4"; prop.children [ content ] ]
                        | None -> Html.none
                    ]
                ]
            ]
        ]
