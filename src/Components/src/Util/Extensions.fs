namespace Swate.Components

open Feliz
open ARCtrl
open Fable.Core

type style =
    static member resolveStyle(style: U2<string, string[]>) =
        match style with
        | U2.Case1 className -> className
        | U2.Case2 classNames -> classNames |> String.concat " "

    static member resolveStyle(style: U2<string, string[]> option) =
        match style with
        | Some(U2.Case1 className) -> className
        | Some(U2.Case2 classNames) -> classNames |> String.concat " "
        | None -> null

[<AutoOpen>]
module Extensions =
    type prop with
        static member inline testid(value: string) : IReactProperty = unbox ("data-testid", value)
        static member inline data(key: string, value: string) : IReactProperty = unbox ($"data-{key}", value)
        static member inline dataRow(value: int) : IReactProperty = unbox ("data-row", value)
        static member inline dataColumn(value: int) : IReactProperty = unbox ("data-column", value)

        static member inline spread(props: obj) : IReactProperty[] =
            props
            |> Fable.Core.JS.Constructors.Object.entries
            |> Seq.map (fun (k, v) -> unbox<IReactProperty> (k, v))
            |> Array.ofSeq

        static member inline onToggle(handler: Browser.Types.Event -> unit) : IReactProperty =
            unbox ("onToggle", handler)

        static member inline style(value: obj) : IReactProperty = unbox ("style", value)

module Keys =
    let inline mkLocalStorageKey (file: string) (component': string) (function': string) : string =
        $"swate-{file}-{component'}-{function'}"


[<RequireQualifiedAccess>]
module kbdEventCode =
    [<Literal>]
    let escape = "Escape"

    [<Literal>]
    let enter = "Enter"

    [<Literal>]
    let arrowDown = "ArrowDown"

    [<Literal>]
    let arrowUp = "ArrowUp"

    [<Literal>]
    let arrowLeft = "ArrowLeft"

    [<Literal>]
    let arrowRight = "ArrowRight"

    [<Literal>]
    let tab = "Tab"

    [<Literal>]
    let shift = "Shift"

    [<Literal>]
    let delete = "Delete"

    [<Literal>]
    let backspace = "Backspace"

    [<Literal>]
    let f2 = "F2"

    let key (key: string) = key.ToUpper() |> sprintf "Key%s"

open Fable.Core
open Fable.Core.JsInterop

[<Fable.Core.Global>]
type console =
    [<Emit("console.log($0)")>]
    static member inline log e = jsNative

    [<Emit("console.warn($0)")>]
    static member inline warn e = jsNative

    [<Emit("console.error($0)")>]
    static member inline error e = jsNative
