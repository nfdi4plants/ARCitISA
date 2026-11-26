module Arcitisa.Components.Api.ArcitisaApi

open Arcitisa.Components
open Arcitisa.Components.Shared
open Fable.Core
open Fable.Core.JsInterop
open Fable.Remoting.Client

let ArcitisaApi: IOntologyAPIv3 =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<IOntologyAPIv3>

let ArcitisaTemplateApi: ITemplateAPIv1 =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<ITemplateAPIv1>
