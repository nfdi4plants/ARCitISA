module Arcitisa.Components.Primitive.ErrorModal.Context

open Arcitisa.Components
open Arcitisa.Components.Primitive.ErrorModal.Types

open Feliz


type ErrorModalActionsContext = {
    enqueue: ErrorModalRequest -> unit
    enqueueMany: ErrorModalRequest list -> unit
    enqueueBatch: ErrorModalBatch -> unit
    report: string -> unit
    dismissById: string -> unit
} with

    static member Empty = {
        enqueue = ignore
        enqueueMany = ignore
        enqueueBatch = ignore
        report = ignore
        dismissById = ignore
    }

type ErrorModalHostContext = {
    current: ErrorModalEntry option
    queue: ErrorModalEntry list
    dismissCurrent: unit -> unit
    dismissById: string -> unit
    dismissBatchItem: string -> string -> unit
    dismissAll: unit -> unit
} with

    static member Empty = {
        current = None
        queue = []
        dismissCurrent = ignore
        dismissById = ignore
        dismissBatchItem = fun _ _ -> ()
        dismissAll = ignore
    }

let ErrorModalCtx =
    React.createContext<ErrorModalActionsContext> (ErrorModalActionsContext.Empty)

let ErrorModalStateCtx =
    React.createContext<ErrorModalHostContext> (ErrorModalHostContext.Empty)

[<Hook>]
let useErrorModalCtx () = React.useContext ErrorModalCtx

[<Hook>]
let useErrorModalStateCtx () = React.useContext ErrorModalStateCtx
