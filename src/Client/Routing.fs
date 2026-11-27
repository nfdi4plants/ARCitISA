module Routing

open Elmish.UrlParser
open Feliz
open Arcitisa.Components
open Arcitisa.Components.Primitive

[<RequireQualifiedAccess>]
type SidebarPage =
    | BuildingBlock
    | TermSearch
    | FilePicker
    | Protocol
    | JsonImport
    | JsonExport
    | DataAnnotator

    member this.AsStringRdbl =
        match this with
        | BuildingBlock -> "Building Blocks"
        | TermSearch -> "Terms"
        | FilePicker -> "File Picker"
        | Protocol -> "Templates"
        | JsonImport -> "Json Import"
        | JsonExport -> "Json Export"
        | DataAnnotator -> "Data Annotator"

    member this.AsIcon() =
        let createElem (icons: ReactElement) =
            Html.i [ prop.title this.AsStringRdbl; prop.children [ icons ] ]

        match this with
        | TermSearch -> createElem <| Icons.MagnifyingGlassPlus()
        | BuildingBlock -> createElem <| Icons.BuildingBlock()
        | Protocol -> createElem <| Icons.Templates()
        | JsonImport -> createElem <| Icons.FileImport()
        | JsonExport -> createElem <| Icons.FileExport()
        | FilePicker -> createElem <| Icons.FilePicker()
        | DataAnnotator -> createElem <| Icons.DataAnnotator()

[<RequireQualifiedAccess>]
type MainPage =
    | Default
    | About
    | PrivacyPolicy
    | Settings

    member this.AsStringRdbl =
        match this with
        | About -> "About"
        | PrivacyPolicy -> "Privacy Policy"
        | Settings -> "Settings"
        | Default -> "Home"

/// The different pages of the application. If you add a new page, then add an entry here.
[<RequireQualifiedAccess>]
type Route = Home of int option

///explained here: https://elmish.github.io/browser/routing.html
//let curry f x y = f (x,y)

module Routing =

    open Elmish
    open Elmish.Navigation

    /// The URL is turned into a Result. The host comes from is_arcitisahost, or from is_swatehost,
    /// which Excel add-ins installed from a manifest older than 4.0.0 still send.
    let route: Parser<Route -> Route, _> =
        oneOf [
            map
                (fun arcitisaHost swateHost -> Route.Home(Option.orElse swateHost arcitisaHost))
                (s "" <?> intParam "is_arcitisahost" <?> intParam "is_swatehost")
        ]

    let parsePath (location: Browser.Types.Location) : Route option =
        Elmish.UrlParser.parsePath route location
