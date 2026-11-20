/// Runs a function in a Web Worker and keeps the call site the same when no worker is available.
///
/// The worker file defines the function with `createWorker`, and a caller starts it with `start` or, in a
/// component, with the `useWebWorker` hook from ReactHooks.fs. This module has no React or DOM dependency,
/// because the worker bundle of the file that defines the function includes it.
///
/// Requests and replies cross the worker boundary through structured clone. Keep them to plain data
/// (anonymous records, arrays, numbers, strings), because class instances lose their methods on the way.
///
/// In a Vite dev server, worker modules must stay out of React Fast Refresh (the exclude option of
/// @vitejs/plugin-react). The refresh runtime needs window, so it throws inside a worker. This matters for
/// files that Fable emits with a .jsx extension, which plugin-react wraps.
module Swate.Components.Util.WebWorker

open Fable.Core
open Swate.Components.JsBindings

[<AllowNullLiteral>]
type MessageEvent =
    abstract data: obj

[<AllowNullLiteral>]
type ErrorEvent =
    abstract message: string

/// The DOM Worker object. Fable.Browser.Dom has no binding for it.
[<AllowNullLiteral>]
type WorkerInstance =
    abstract postMessage: msg: obj -> unit
    abstract terminate: unit -> unit
    abstract onmessage: (MessageEvent -> unit) with get, set
    abstract onerror: (ErrorEvent -> unit) with get, set

/// Creates the worker for a module. It must be called from the worker file itself with the name of that file
/// and Compiler.extension, as in `selfWorker ("./MyWorker" + Compiler.extension)`. Fable folds that into one
/// string literal at transpile time, and Vite only bundles a worker for `new URL('<literal>', import.meta.url)`.
/// The URL is relative to the file the expression is emitted into, which is why the call cannot live in a
/// shared helper or in the calling file.
[<Emit("() => new Worker(new URL($0, import.meta.url), { type: 'module' })")>]
let selfWorker (modulePath: string) : (unit -> WorkerInstance) = jsNative

/// True inside a dedicated worker. jsdom and Node have no WorkerGlobalScope.
[<Emit("(typeof WorkerGlobalScope !== 'undefined' && self instanceof WorkerGlobalScope)")>]
let isWorkerScope () : bool = jsNative

[<Emit("typeof Worker !== 'undefined'")>]
let private hasWorker () : bool = jsNative

[<Emit("self.onmessage = $0")>]
let private setOnMessage (handler: MessageEvent -> unit) : unit = jsNative

[<Emit("self.postMessage($0)")>]
let private postToHost (msg: obj) : unit = jsNative

type private Request<'Req> = {| id: int; req: 'Req |}

type private Reply<'Res> = {|
    id: int
    ok: bool
    res: 'Res
    error: string
|}

/// A function that can run in a worker. Ctor is None where no Worker exists (jsdom, Node), and Handle is the
/// function itself, which the main thread runs when the worker is missing or has failed.
type WorkerDef<'Req, 'Res> = {
    Ctor: (unit -> WorkerInstance) option
    Handle: 'Req -> JS.Promise<'Res>
}

/// What `start` returns. Dispatch sends a request and resolves with the reply. Terminate stops the worker,
/// and later requests run on the main thread. A request that is still pending when the client is terminated
/// never settles. InWorker is true when a worker was created.
type WorkerClient<'Req, 'Res> = {
    Dispatch: 'Req -> JS.Promise<'Res>
    Terminate: unit -> unit
    InWorker: bool
}

/// Defines a worker function. Call it at the top level of the worker file, for example
/// `let Worker = createWorker (selfWorker ("./MyWorker" + Compiler.extension)) (fun req -> ...)`.
///
/// Inside a worker the handler is registered for incoming requests. Each request is answered with the
/// handler result or, when the handler throws or its promise rejects, with the error message (the text of
/// the thrown value when it has no message). Outside a worker nothing is registered and the definition is
/// only returned. A worker file calls it once, because the last registered handler wins.
let createWorker (ctor: unit -> WorkerInstance) (handle: 'Req -> JS.Promise<'Res>) : WorkerDef<'Req, 'Res> =
    if isWorkerScope () then
        setOnMessage (fun ev ->
            let msg = unbox<Request<'Req>> ev.data

            let run =
                try
                    handle msg.req
                with error ->
                    Promise.reject error

            run
            |> Promise.map (fun res ->
                postToHost {|
                    id = msg.id
                    ok = true
                    res = res
                    error = ""
                |}
            )
            |> Promise.catch (fun error ->
                postToHost {|
                    id = msg.id
                    ok = false
                    res = Unchecked.defaultof<'Res>
                    error = Errors.messageOf error
                |}
            )
            |> ignore
        )

    {
        Ctor = if hasWorker () then Some ctor else None
        Handle = handle
    }

let private mainThreadClient (def: WorkerDef<'Req, 'Res>) : WorkerClient<'Req, 'Res> = {
    Dispatch = def.Handle
    Terminate = ignore
    InWorker = false
}

// An error event of a script that did not load carries no message.
let private reasonOf (ev: ErrorEvent) : string =
    if System.String.IsNullOrEmpty ev.message then
        "script error"
    else
        ev.message

type private Waiting<'Req, 'Res> = {
    Req: 'Req
    Resolve: 'Res -> unit
    Reject: exn -> unit
}

/// Starts the worker of a definition. Without a Ctor, Dispatch runs the handler on the main thread.
///
/// A request whose handler fails is rejected with the error message of the handler. When the worker itself
/// fails (a script that does not load, or an error outside the handler), the client terminates it and writes
/// one console warning. It runs the pending requests on the main thread and sends later requests there too.
/// A Ctor that throws gives the same main-thread client and warning.
///
/// A request that is still pending when the client is terminated never settles, so a caller that cares has
/// to ignore it. `useWebWorker` wraps this for components.
let start (def: WorkerDef<'Req, 'Res>) : WorkerClient<'Req, 'Res> =
    let created =
        match def.Ctor with
        | None -> None
        | Some ctor ->
            try
                Some(ctor ())
            with error ->
                JS.console.warn (
                    "The worker could not be created, running on the main thread instead.",
                    Errors.messageOf error
                )

                None

    match created with
    | None -> mainThreadClient def
    | Some worker ->
        let waiting = JS.Constructors.Map.Create<int, Waiting<'Req, 'Res>>()
        let nextId = ref 0
        let onMainThread = ref false

        worker.onmessage <-
            fun ev ->
                let reply = unbox<Reply<'Res>> ev.data

                if waiting.has reply.id then
                    let request = waiting.get reply.id
                    waiting.delete reply.id |> ignore

                    if reply.ok then
                        request.Resolve reply.res
                    else
                        request.Reject(exn reply.error)

        let stop () =
            onMainThread.Value <- true
            worker.terminate ()

        worker.onerror <-
            fun ev ->
                if not onMainThread.Value then
                    JS.console.warn ("The worker failed, running on the main thread instead.", reasonOf ev)
                    stop ()

                    waiting.forEach (fun request _ _ ->
                        def.Handle request.Req
                        |> Promise.map request.Resolve
                        |> Promise.catchEnd request.Reject
                    )

                    waiting.clear ()

        {
            Dispatch =
                fun req ->
                    if onMainThread.Value then
                        def.Handle req
                    else
                        JS.Constructors.Promise.Create(fun resolve reject ->
                            nextId.Value <- nextId.Value + 1

                            waiting.set (
                                nextId.Value,
                                {
                                    Req = req
                                    Resolve = resolve
                                    Reject = reject
                                }
                            )
                            |> ignore

                            worker.postMessage {| id = nextId.Value; req = req |}
                        )
            Terminate = stop
            InWorker = true
        }
