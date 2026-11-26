module Arcitisa.Components.Primitive.ErrorModal.Types

[<RequireQualifiedAccess>]
type ErrorModalActionStyle =
    | Neutral
    | Primary
    | Error

type ErrorModalAction = {
    Label: string
    OnClick: unit -> unit
    IconClassName: string option
    Style: ErrorModalActionStyle
} with

    static member create(label: string, onClick: unit -> unit, ?iconClassName: string, ?style: ErrorModalActionStyle) = {
        Label = label
        OnClick = onClick
        IconClassName = iconClassName
        Style = defaultArg style ErrorModalActionStyle.Neutral
    }

type ErrorModalRequest = {
    Id: string
    ScopeId: string option
    Title: string
    Message: string
    Details: string option
    DismissLabel: string
    OnDismiss: (unit -> unit) option
    Actions: ErrorModalAction list
} with

    static member create
        (
            message: string,
            ?title: string,
            ?details: string,
            ?dismissLabel: string,
            ?onDismiss: unit -> unit,
            ?actions: ErrorModalAction list,
            ?scopeId: string,
            ?id: string
        ) =
        {
            Id = defaultArg id (System.Guid.NewGuid().ToString())
            ScopeId = scopeId
            Title = defaultArg title "Something went wrong"
            Message = message
            Details = details
            DismissLabel = defaultArg dismissLabel "OK"
            OnDismiss = onDismiss
            Actions = defaultArg actions []
        }

type ErrorModalBatch = {
    Id: string
    ScopeId: string option
    Title: string
    Summary: string option
    Errors: ErrorModalRequest list
    DismissLabel: string
    OnDismiss: (unit -> unit) option
} with

    static member create
        (
            errors: ErrorModalRequest list,
            ?title: string,
            ?summary: string,
            ?dismissLabel: string,
            ?onDismiss: unit -> unit,
            ?scopeId: string,
            ?id: string
        ) =
        {
            Id = defaultArg id (System.Guid.NewGuid().ToString())
            ScopeId = scopeId
            Title = defaultArg title "Multiple errors occurred"
            Summary = summary
            Errors = errors
            DismissLabel = defaultArg dismissLabel "Dismiss Visible Errors"
            OnDismiss = onDismiss
        }

[<RequireQualifiedAccess>]
type ErrorModalEntry =
    | Single of ErrorModalRequest
    | Batch of ErrorModalBatch

    member this.Id =
        match this with
        | Single request -> request.Id
        | Batch batch -> batch.Id

    member this.Title =
        match this with
        | Single request -> request.Title
        | Batch batch -> batch.Title

    member this.ScopeId =
        match this with
        | Single request -> request.ScopeId
        | Batch batch -> batch.ScopeId

type ErrorModalMsg =
    | Enqueue of ErrorModalEntry
    | EnqueueMany of ErrorModalEntry list
    | DismissById of string
    | DismissManyByIds of Set<string>
    | DismissBatchItem of string * string
