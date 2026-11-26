module Arcitisa.Components.Primitive.Blankslate.Types

open Fable.Core
open Arcitisa.Components.Primitive

[<StringEnum>]
[<RequireQualifiedAccess>]
type BlankslateTextSize =
    | Small
    | Medium
    | Large

type BlankslatePrimaryAction = {
    Label: string
    OnClick: unit -> unit
    IconClassName: string option
    Disabled: bool
    Color: DaisyuiColors
} with

    static member create
        (label: string, onClick: unit -> unit, ?iconClassName: string, ?disabled: bool, ?color: DaisyuiColors)
        =
        {
            Label = label
            OnClick = onClick
            IconClassName = iconClassName
            Disabled = defaultArg disabled false
            Color = defaultArg color DaisyuiColors.Primary
        }

type BlankslateSecondaryAction = {
    Label: string
    OnClick: unit -> unit
    IconClassName: string option
    Disabled: bool
} with

    static member create(label: string, onClick: unit -> unit, ?iconClassName: string, ?disabled: bool) = {
        Label = label
        OnClick = onClick
        IconClassName = iconClassName
        Disabled = defaultArg disabled false
    }
