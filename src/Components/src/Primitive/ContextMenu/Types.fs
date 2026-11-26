module Arcitisa.Components.Primitive.ContextMenu.Types

open Fable.Core
open Feliz

[<Global; AllowNullLiteral>]
type ContextMenuItem
    [<ParamObjectAttribute; Emit("$0")>]
    (
        ?text: ReactElement,
        ?icon: ReactElement,
        ?label: string,
        ?kbdbutton:
            {|
                element: ReactElement
                label: string
            |},
        ?isDivider: bool,
        ?onClick:
            {|
                buttonEvent: Browser.Types.MouseEvent
                spawnData: obj
            |}
                -> unit
    ) =
    member val text = text with get, set
    member val label = label with get, set
    member val icon = icon with get, set
    member val kbdbutton = kbdbutton with get, set
    member val isDivider: bool = defaultArg isDivider false with get, set
    member val onClick = onClick with get, set
