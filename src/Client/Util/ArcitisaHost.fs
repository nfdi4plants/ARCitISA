[<AutoOpen>]
module Host

[<RequireQualifiedAccess>]
type Arcitisahost =
    | Browser
    | Excel
    | ARCitect

    static member ofQueryParam(queryInteger: int option) =
        match queryInteger with
        | Some 1 -> Arcitisahost.ARCitect
        | Some 2 -> Arcitisahost.Excel
        | _ -> Browser

    member this.IsStandalone = this = Arcitisahost.Browser || this = Arcitisahost.ARCitect
