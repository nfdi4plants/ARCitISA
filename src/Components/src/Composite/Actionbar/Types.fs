module Arcitisa.Components.Composite.Actionbar.Types

open Fable.Core


type ButtonInfo = {
    icon: string
    toolTip: string option
    onClick: Browser.Types.MouseEvent -> unit
} with

    static member create(icon: string, toolTip: string, (onClick: Browser.Types.MouseEvent -> unit)) = {
        icon = icon
        toolTip = Some toolTip
        onClick = onClick
    }
