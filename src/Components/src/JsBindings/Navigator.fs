namespace Arcitisa.Components

open Fable.Core


[<Erase>]
type Navigator =
    abstract member connection: obj option with get
    abstract member clipboard: Clipboard
    abstract member platform: string with get
    abstract member userAgent: string with get

[<AutoOpen>]
module GlobalBindings =

    [<Emit("navigator")>]
    let navigator: Navigator = jsNative
