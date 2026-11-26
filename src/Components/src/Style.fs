namespace Arcitisa.Components

open Fable.Core
open Fable.Core.JsInterop

[<Erase>]
type ArcitisaStyle =
    static member import = importSideEffects "./arcitisa-components.css"
