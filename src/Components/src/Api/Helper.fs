module Arcitisa.Components.Api.Helper

open Arcitisa.Components
open Arcitisa.Components.Shared
open Fable.Core
open Fable.Core.JsInterop
open Fable.Remoting.Client
open Fetch

let makeQueryParam (name: string, value: obj) =
    Fable.Core.JS.encodeURIComponent name
    + "="
    + (string >> Fable.Core.JS.encodeURIComponent) value

let makeQueryParamStr (queryParams: (string * obj) list) : string =
    queryParams
    |> List.map (fun (name, value) -> makeQueryParam (name, value))
    |> String.concat "&"
    |> (+) "?"

let appendQueryParams (url: string) (queryParams: (string * obj) list) : string = url + makeQueryParamStr queryParams
