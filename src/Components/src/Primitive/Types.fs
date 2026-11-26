[<AutoOpen>]
module Arcitisa.Components.Primitive.Types

open Fable.Core

[<StringEnum>]
[<RequireQualifiedAccess>]
type DaisyuiSize =
    | XS
    | SM
    | MD
    | LG
    | XL

[<StringEnum>]
[<RequireQualifiedAccess>]
type DaisyuiColors =
    | Primary
    | Secondary
    | Accent
    | Info
    | Success
    | Warning
    | Error

[<StringEnum>]
[<RequireQualifiedAccess>]
type DaisyuiTooltipPosition =
    | Top
    | Right
    | Bottom
    | Left
