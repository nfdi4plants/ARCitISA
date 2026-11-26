module Arcitisa.Components.Primitive.ErrorModal.Helper

open Arcitisa.Components.Primitive.ErrorModal.Types

let reducer (queue: ErrorModalEntry list) (msg: ErrorModalMsg) =
    match msg with
    | Enqueue entry -> queue @ [ entry ]
    | EnqueueMany entries -> queue @ entries
    | DismissById id -> queue |> List.filter (fun entry -> entry.Id <> id)
    | DismissManyByIds ids -> queue |> List.filter (fun entry -> not (ids.Contains entry.Id))
    | DismissBatchItem(batchId, itemId) ->
        queue
        |> List.choose (fun entry ->
            match entry with
            | ErrorModalEntry.Batch batch when batch.Id = batchId ->
                let nextErrors = batch.Errors |> List.filter (fun request -> request.Id <> itemId)

                if nextErrors.IsEmpty then
                    None
                else
                    ErrorModalEntry.Batch { batch with Errors = nextErrors } |> Some
            | _ -> Some entry
        )

let entriesInScope (scopeId: string option) (queue: ErrorModalEntry list) =
    queue |> List.filter (fun entry -> entry.ScopeId = scopeId)

let withDefaultRequestScope (scopeId: string option) (request: ErrorModalRequest) =
    match request.ScopeId with
    | Some _ -> request
    | None -> { request with ScopeId = scopeId }

let withDefaultBatchScope (scopeId: string option) (batch: ErrorModalBatch) =
    let batchScope =
        match batch.ScopeId with
        | Some _ -> batch.ScopeId
        | None -> scopeId

    {
        batch with
            ScopeId = batchScope
            Errors = batch.Errors |> List.map (withDefaultRequestScope batchScope)
    }

let dismissSingleRequest (dismissById: string -> unit) (request: ErrorModalRequest) =
    request.OnDismiss |> Option.iter (fun callback -> callback ())
    dismissById request.Id

let dismissBatch (dismissById: string -> unit) (batch: ErrorModalBatch) =
    batch.Errors
    |> List.iter (fun request -> request.OnDismiss |> Option.iter (fun callback -> callback ()))

    batch.OnDismiss |> Option.iter (fun callback -> callback ())
    dismissById batch.Id

let dismissBatchItemRequest
    (dismissBatchItem: string -> string -> unit, batch: ErrorModalBatch, request: ErrorModalRequest)
    =
    let isLastItem = batch.Errors.Length <= 1

    request.OnDismiss |> Option.iter (fun callback -> callback ())

    if isLastItem then
        batch.OnDismiss |> Option.iter (fun callback -> callback ())

    dismissBatchItem batch.Id request.Id

let resolveEntryForScopedDismiss (entry: ErrorModalEntry) =
    match entry with
    | ErrorModalEntry.Single request -> request.OnDismiss |> Option.iter (fun callback -> callback ())
    | ErrorModalEntry.Batch batch ->
        batch.Errors
        |> List.iter (fun request -> request.OnDismiss |> Option.iter (fun callback -> callback ()))

        batch.OnDismiss |> Option.iter (fun callback -> callback ())
