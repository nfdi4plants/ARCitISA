module Arcitisa.Components.Composite.Widgets.Context

open Fable.Core
open Feliz

[<RequireQualifiedAccess>]
[<StringEnum>]
type WidgetType =
    | BuildingBlock
    | Template
    | FilePicker
    | DataAnnotator
    | JsonImport
    | JsonExport
    | Playground

type WidgetDefinition = {|
    prefix: string
    content: ReactElement
|}

type WidgetControllerContext = {
    activeWidgets: WidgetType list
    isActive: WidgetType -> bool
    openWidget: WidgetType -> unit
    closeWidget: WidgetType -> unit
    toggleWidget: WidgetType -> unit
    focusWidget: WidgetType -> unit
} with

    static member init() : WidgetControllerContext = {
        activeWidgets = []
        isActive = fun _ -> false
        openWidget = fun _ -> ()
        closeWidget = fun _ -> ()
        toggleWidget = fun _ -> ()
        focusWidget = fun _ -> ()
    }

let WidgetControllerCtx =
    React.createContext<WidgetControllerContext> (WidgetControllerContext.init ())

[<Hook>]
let useWidgetControllerCtx () = React.useContext WidgetControllerCtx
