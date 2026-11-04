namespace Swate.Components.Primitive.Banner

open Fable.Core
open Feliz
open Swate.Components.Primitive.Banner.Types

module private BannerHelper =

    /// Both line break styles start a new line.
    let splitLines (message: string) : string[] =
        message.Replace("\r\n", "\n").Split('\n')

    let messageLine (testId: string) (index: int) (line: string) =
        Html.span [
            prop.key index
            prop.testId $"{testId}Line{index}"
            prop.className "swt:block swt:min-h-[1lh]"
            prop.text line
        ]

    let actionButton (testId: string) (index: int) (action: BannerAction) =
        Html.button [
            prop.key index
            prop.testId $"{testId}Action{index}"
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

open BannerHelper

/// A notice across the full width of its container. It has no close control, so it stays until the host removes it.
[<Erase; Mangle(false)>]
type Banner =

    [<ReactComponent(true)>]
    static member Banner
        (message: string, ?actions: BannerAction[], ?testId: string, ?className: string)
        : ReactElement =
        let actions = defaultArg actions [||]
        let testId = defaultArg testId "Banner"

        Html.div [
            prop.testId testId
            prop.role.alert
            prop.className [
                "swt:alert swt:alert-warning swt:flex swt:flex-wrap swt:items-center swt:gap-x-3 swt:gap-y-2 swt:w-full swt:min-w-0 swt:rounded-none swt:px-4 swt:py-2 swt:text-sm"
                defaultArg className ""
            ]
            prop.children [
                Html.span [
                    prop.className "swt:iconify swt:fluent--warning-24-regular swt:size-5 swt:shrink-0"
                    prop.ariaHidden true
                ]
                // The basis lets the actions wrap below the message when the width runs out.
                Html.div [
                    prop.testId $"{testId}Message"
                    prop.className "swt:flex-1 swt:basis-64 swt:min-w-0 swt:wrap-anywhere"
                    prop.children (splitLines message |> Array.mapi (messageLine testId))
                ]
                (if actions.Length > 0 then
                     Html.div [
                         prop.testId $"{testId}Actions"
                         prop.className "swt:flex swt:flex-wrap swt:items-center swt:gap-2"
                         prop.children (actions |> Array.mapi (actionButton testId))
                     ]
                 else
                     Html.none)
            ]
        ]
