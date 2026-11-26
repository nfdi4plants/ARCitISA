module Arcitisa.Components.Composite.Widgets.LocalStorage

open Feliz
open Fable.Core.JsInterop
open Fable.SimpleJson
open Arcitisa.Components.Composite.Widgets.Types

let private tryLoadRect (key: string) =
    match Browser.WebStorage.localStorage.getItem key with
    | null -> None
    | item ->
        try
            Json.parseAs<Rect> item |> Some
        with _ ->
            Browser.WebStorage.localStorage.removeItem key
            None

[<RequireQualifiedAccess>]
module WidgetLiterals =

    [<Literal>]
    let BuildingBlock = "BuildingBlock"

    [<Literal>]
    let Templates = "Templates"

    [<Literal>]
    let FilePicker = "FilePicker"

    [<Literal>]
    let TableSelect = "TableSelect"

    [<Literal>]
    let DataAnnotator = "DataAnnotator"

[<RequireQualifiedAccess>]
module Position =

    open Browser

    [<Literal>]
    let private Key_Prefix = "WidgetsPosition_"

    let write (modalName: string, dt: Rect) =
        let s = Json.serialize dt
        WebStorage.localStorage.setItem (Key_Prefix + modalName, s)

    let load (modalName: string) =
        let key = Key_Prefix + modalName
        tryLoadRect key


[<RequireQualifiedAccess>]
module Size =
    open Browser

    [<Literal>]
    let private Key_Prefix = "WidgetsSize_"

    let write (modalName: string, dt: Rect) =
        let s = Json.serialize dt
        WebStorage.localStorage.setItem (Key_Prefix + modalName, s)

    let load (modalName: string) =
        let key = Key_Prefix + modalName
        tryLoadRect key
