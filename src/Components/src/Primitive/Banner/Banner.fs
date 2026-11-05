namespace Swate.Components.Primitive.Banner

open Fable.Core
open Feliz
open Swate.Components.Primitive.Banner.Types

module private BannerHelper =

    /// Both line break styles start a new line.
    let splitLines (message: string) : string[] =
        message.Replace("\r\n", "\n").Split('\n')

    let variantClass (variant: BannerVariant) =
        match variant with
        | BannerVariant.Info -> "swt:alert-info"
        | BannerVariant.Success -> "swt:alert-success"
        | BannerVariant.Warning -> "swt:alert-warning"
        | BannerVariant.Error -> "swt:alert-error"

    let variantIcon (variant: BannerVariant) =
        match variant with
        | BannerVariant.Info -> "swt:fluent--info-24-regular"
        | BannerVariant.Success -> "swt:fluent--checkmark-circle-24-regular"
        | BannerVariant.Warning -> "swt:fluent--warning-24-regular"
        | BannerVariant.Error -> "swt:fluent--error-circle-24-regular"

open BannerHelper

/// A notice across the full width of its container. With onClose it has a close button, which calls onClose and
/// leaves the removal to the host. Without onClose it stays until the host removes it.
[<Erase; Mangle(false)>]
type Banner =

    [<ReactComponent>]
    static member private MessageLine(line: string, testId: string, ?key: string) =
        Html.span [
            prop.testId testId
            prop.className "swt:block swt:min-h-[1lh]"
            prop.text line
        ]

    [<ReactComponent>]
    static member private ActionButton(action: BannerAction, testId: string, ?key: string) =
        Html.button [
            prop.testId testId
            prop.type'.button
            prop.className "swt:btn swt:btn-sm swt:gap-1"
            prop.disabled action.Disabled
            prop.onClick (fun _ -> action.OnClick())
            prop.children [
                (match action.Icon with
                 | Some icon ->
                     Html.span [
                         prop.className $"swt:iconify {icon} swt:size-4"
                         prop.ariaHidden true
                     ]
                 | None -> Html.none)
                Html.span action.Label
            ]
        ]

    [<ReactComponent(true)>]
    static member Banner
        (
            message: string,
            ?actions: BannerAction[],
            ?variant: BannerVariant,
            ?icon: string,
            ?testId: string,
            ?className: string,
            ?onClose: unit -> unit
        ) : ReactElement =
        let actions = defaultArg actions [||]
        let variant = defaultArg variant BannerVariant.Info
        let testId = defaultArg testId "Banner"

        Html.div [
            prop.testId testId
            prop.role.alert
            prop.className [
                "swt:alert swt:flex swt:items-center swt:gap-x-3 swt:w-full swt:min-w-0 swt:rounded-none swt:px-4 swt:py-2 swt:text-sm"
                variantClass variant
                defaultArg className ""
            ]
            prop.children [
                Html.span [
                    prop.testId $"{testId}Icon"
                    prop.className $"swt:iconify {defaultArg icon (variantIcon variant)} swt:size-5 swt:shrink-0"
                    prop.ariaHidden true
                ]
                // Only the message and the actions wrap, so the close button stays at the right edge. The basis lets
                // the actions wrap below the message when the width runs out.
                Html.div [
                    prop.className
                        "swt:flex swt:flex-1 swt:flex-wrap swt:items-center swt:gap-x-3 swt:gap-y-2 swt:min-w-0"
                    prop.children [
                        Html.div [
                            prop.testId $"{testId}Message"
                            prop.className "swt:flex-1 swt:basis-64 swt:min-w-0 swt:wrap-anywhere"
                            prop.children (
                                splitLines message
                                |> Array.mapi (fun index line ->
                                    Banner.MessageLine(line, $"{testId}Line{index}", key = string index)
                                )
                            )
                        ]
                        (if actions.Length > 0 then
                             Html.div [
                                 prop.testId $"{testId}Actions"
                                 prop.className "swt:flex swt:flex-wrap swt:items-center swt:gap-2"
                                 prop.children (
                                     actions
                                     |> Array.mapi (fun index action ->
                                         Banner.ActionButton(action, $"{testId}Action{index}", key = string index)
                                     )
                                 )
                             ]
                         else
                             Html.none)
                    ]
                ]
                (match onClose with
                 | Some onClose ->
                     Html.button [
                         prop.testId $"{testId}Close"
                         prop.type'.button
                         prop.className "swt:btn swt:btn-ghost swt:btn-sm swt:btn-square swt:shrink-0"
                         prop.title "Close"
                         prop.ariaLabel "Close"
                         prop.onClick (fun _ -> onClose ())
                         prop.children [
                             Html.span [
                                 prop.className "swt:iconify swt:fluent--dismiss-24-regular swt:size-4"
                                 prop.ariaHidden true
                             ]
                         ]
                     ]
                 | None -> Html.none)
            ]
        ]
