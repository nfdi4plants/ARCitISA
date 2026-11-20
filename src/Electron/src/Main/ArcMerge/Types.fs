namespace Main.ArcMerge

[<RequireQualifiedAccess>]
type EventName =
    | Add
    | Unlink
    | Change

module EventName =
    let parse (s: string) =
        match s.ToLowerInvariant() with
        | "add" -> EventName.Add
        | "unlink" -> EventName.Unlink
        | "change" -> EventName.Change
        | other -> failwithf "Unknown event name: '%s'" other

type FileEvent = { EventName: EventName; Path: string }

[<RequireQualifiedAccess>]
type ArcEntityRef =
    | Investigation
    | License
    | Assay of string
    | AssayDataMap of string
    | Study of string
    | StudyDataMap of string
    | Run of string
    | RunDataMap of string
    | Workflow of string
    | WorkflowDataMap of string
    | Unknown of string
