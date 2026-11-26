module Api

open Arcitisa.Components.Shared
open Fable.Remoting.Client

/// A proxy you can use to talk to server directly
let api: IOntologyAPIv2 =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<IOntologyAPIv2>

let ontology: IOntologyAPIv3 =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<IOntologyAPIv3>

let templateApi: ITemplateAPIv1 =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<ITemplateAPIv1>

let serviceApi: IServiceAPIv1 =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<IServiceAPIv1>

let testAPIv1: ITestAPI =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<ITestAPI>
