module LocalStorage.ArcitisaSearchConfig

module Util =

    [<Literal>]
    let BASE_KEY = "Arcitisa_Search_Config"

[<RequireQualifiedAccess>]
module ArcitisaDefaultSearch =

    [<Literal>]
    let KEY = Util.BASE_KEY + "_ArcitisaDefaultSearch"

    let Get () : bool =
        try
            let v = Browser.WebStorage.localStorage.getItem (KEY)

            match v.ToLower() with
            | "true"
            | "1" -> true
            | "false"
            | "0" -> false
            | _ -> true
        with _ ->
            true

    let Set (v: bool) =
        Browser.WebStorage.localStorage.setItem (KEY, string v)

[<RequireQualifiedAccess>]
module TIBSearch =

    open Fable.SimpleJson

    [<Literal>]
    let KEY = Util.BASE_KEY + "_TIBSearch"

    let Get () : string[] =
        try
            let v = Browser.WebStorage.localStorage.getItem (KEY)
            Json.parseAs<string[]> v
        with _ -> [||]

    let Set (v: string[]) =
        let s = Json.serialize v
        Browser.WebStorage.localStorage.setItem (KEY, s)
