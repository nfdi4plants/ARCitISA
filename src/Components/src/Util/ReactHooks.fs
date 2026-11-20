[<AutoOpenAttribute>]
module Swate.Components.ReactHooks

// https://github.com/Shmew/Feliz.UseListener/blob/master/src/Feliz.UseListener/Listener.fs

open Browser.Types
open Browser.Dom
open Fable.Core
open Fable.Core.JsInterop
open Fable.Core
open Feliz
open System.ComponentModel

type UseMeasureRect =
    abstract width: float option
    abstract height: float option

[<EditorBrowsable(EditorBrowsableState.Never)>]
module Impl =
    [<Emit("typeof window !== 'undefined'")>]
    let isWindowDefined () : bool = jsNative

    [<Emit("typeof window.addEventListener === 'function'")>]
    let isWindowListenerFunction () : bool = jsNative

    [<Emit("Object.defineProperty({}, 'passive', {get () { $0() }})")>]
    let definePassive (updater: unit -> unit) : JS.PropertyDescriptor = jsNative

    let allowsPassiveEvents =
        let mutable passive = false

        try
            if isWindowDefined () && isWindowListenerFunction () then
                let options = jsOptions<AddEventListenerOptions> (fun o -> o.passive <- true)

                window.addEventListener ("testPassiveEventSupport", ignore, options)
                window.removeEventListener ("testPassiveEventSupport", ignore)
        with _ ->
            ()

        passive

    let defaultPassive = jsOptions<AddEventListenerOptions> (fun o -> o.passive <- true)

    let adjustPassive (maybeOptions: AddEventListenerOptions option) =
        maybeOptions
        |> Option.map (fun options ->
            if options.passive && not allowsPassiveEvents then
                jsOptions<AddEventListenerOptions> (fun o ->
                    o.capture <- options.capture
                    o.once <- options.once
                    o.passive <- false
                )
            else
                options
        )

    let createRemoveOptions (maybeOptions: AddEventListenerOptions option) =
        maybeOptions
        |> Option.bind (fun options ->
            if options.capture then
                Some(jsOptions<RemoveEventListenerOptions> (fun o -> o.capture <- true))
            else
                None
        )

/// React.useLayoutEffect for an effect with a cleanup. In Feliz 3.1.1 the `unit -> (unit -> unit)` overload
/// carries ImportMember, so React receives the uncurried lambda and runs the cleanup right after the setup.
/// The IDisposable overload hands React an object. This binding calls React directly.
///
/// The setup returns the cleanup function boxed, or `box ()` when there is none.
[<Import("useLayoutEffect", "react")>]
let useLayoutEffectWithCleanup (setup: unit -> obj, dependencies: obj[]) : unit = jsNative

type React =

    static member inline useDebouncedCallback<'A>(func: 'A -> unit, ?delay: int) =
        let timeout = React.useRef (None)
        let delay = defaultArg delay 500

        React.useCallback (
            (fun (arg: 'A) ->

                let later =
                    fun () ->
                        timeout.current |> Option.iter (Fable.Core.JS.clearTimeout)
                        func arg

                timeout.current |> Option.iter (Fable.Core.JS.clearTimeout)
                timeout.current <- Some(Fable.Core.JS.setTimeout later delay)
            ),
            [| func; delay |]
        )

    static member inline useDebouncedCallbackWithCancel<'A>
        (
            func: 'A -> unit,
            ?delay: int,
            ?oncancel: unit -> unit,
            ?ondebouncestart: unit -> unit,
            ?ondebouncerun: unit -> unit
        ) =
        let timeout = React.useRef (None)
        let delay = defaultArg delay 500

        let debouncedCallBack =
            React.useCallback (
                (fun (arg: 'A) ->

                    let later =
                        fun () ->
                            timeout.current |> Option.iter (Fable.Core.JS.clearTimeout)
                            ondebouncerun |> Option.iter (fun f -> f ())
                            func arg

                    ondebouncestart |> Option.iter (fun f -> f ())
                    timeout.current |> Option.iter (Fable.Core.JS.clearTimeout)
                    timeout.current <- Some(Fable.Core.JS.setTimeout later delay)
                ),
                [| func; delay |]
            )

        let cancel =
            React.useCallback (
                (fun () ->
                    if timeout.current.IsSome then
                        Fable.Core.JS.clearTimeout (timeout.current.Value)
                        oncancel |> Option.iter (fun f -> f ())
                )
            )

        cancel, debouncedCallBack

    [<ImportMember("@uidotdev/usehooks")>]
    static member useLocalStorage<'A>(key: string, defaultValue: 'A) : ('A * ('A -> unit)) = jsNative

    [<ImportMember("@uidotdev/usehooks")>]
    static member useDebounce<'A>(value: 'A, delay: int) : 'A = jsNative

    [<ImportMember("@uidotdev/usehooks")>]
    static member useThrottle<'A>(value: 'A, delay: int) : 'A = jsNative

    [<ImportMember("@uidotdev/usehooks")>]
    static member useMeasure<'T when 'T :> Element>() : (('T option -> unit) * UseMeasureRect) = jsNative

[<Erase; RequireQualifiedAccess>]
module React =

    [<Erase>]
    type useListener =
        static member inline on
            (eventType: string, action: #Event -> unit, ?options: AddEventListenerOptions, ?dependencies: obj[])
            =
            let addOptions =
                React.useMemo ((fun () -> Impl.adjustPassive options), [| options |])

            let removeOptions =
                React.useMemo ((fun () -> Impl.createRemoveOptions options), [| options |])

            let fn =
                React.useMemo (
                    (fun () -> unbox<#Event> >> action),
                    [|
                        action
                        if dependencies.IsSome then
                            yield! dependencies.Value
                    |]
                )

            let listener =
                React.useCallback (fun () ->
                    match addOptions with
                    | Some options -> document.addEventListener (eventType, fn, options)
                    | None -> document.addEventListener (eventType, fn)

                    FsReact.createDisposable (fun () ->
                        match removeOptions with
                        | Some options -> document.removeEventListener (eventType, fn, options)
                        | None -> document.removeEventListener (eventType, fn)
                    )
                )

            React.useEffect (listener)

        static member inline onMouseDown(action: MouseEvent -> unit, ?options: AddEventListenerOptions, ?dependencies) =
            useListener.on ("mousedown", action, ?options = options, ?dependencies = dependencies)

        static member inline onTouchStart
            (action: TouchEvent -> unit, ?options: AddEventListenerOptions, ?dependencies)
            =
            useListener.on ("touchstart", action, ?options = options, ?dependencies = dependencies)

        static member inline onResize(action: Event -> unit, ?options: AddEventListenerOptions) =
            useListener.on ("resize", action, ?options = options)

        /// Invokes the callback when a click event is not within the given element.
        ///
        /// Uses separate handlers for touch and mouse events.
        ///
        /// This listener is passive by default.
        static member inline onClickAway
            (
                elemRef: IRefValue<#HTMLElement option>,
                callback: MouseEvent -> unit,
                touchCallback: TouchEvent -> unit,
                ?options: AddEventListenerOptions
            ) =
            let options = Option.defaultValue Impl.defaultPassive options

            useListener.onMouseDown (
                (fun ev ->
                    match elemRef.current with
                    | Some elem when not (elem.contains (unbox ev.target)) -> callback ev
                    | _ -> ()
                ),
                options
            )

            useListener.onTouchStart (
                (fun ev ->
                    match elemRef.current with
                    | Some elem when not (elem.contains (unbox ev.target)) -> touchCallback ev
                    | _ -> ()
                ),
                options
            )

        /// Invokes the callback when a click event is not within the given element.
        ///
        /// Shares a common callback for both touch and mouse events.
        ///
        /// This listener is passive by default.
        static member inline onClickAway
            (
                elemRef: IRefValue<#HTMLElement option>,
                callback: UIEvent -> unit,
                ?options: AddEventListenerOptions,
                ?dependencies
            ) =
            let options = Option.defaultValue Impl.defaultPassive options

            useListener.onMouseDown (
                (fun ev ->
                    match elemRef.current with
                    | Some elem when not (elem.contains (unbox ev.target)) -> callback ev
                    | _ -> ()
                ),
                options,
                ?dependencies = dependencies
            )

            useListener.onTouchStart (
                (fun ev ->
                    match elemRef.current with
                    | Some elem when not (elem.contains (unbox ev.target)) -> callback ev
                    | _ -> ()
                ),
                options,
                ?dependencies = dependencies
            )

        static member inline onKeyDown(action: KeyboardEvent -> unit, ?options: AddEventListenerOptions) =
            useListener.on ("keydown", action, ?options = options)

    [<Erase>]
    type useElementListener =
        static member inline on
            (
                elemRef: IRefValue<#HTMLElement option>,
                eventType: string,
                action: #Event -> unit,
                ?options: AddEventListenerOptions
            ) =
            let addOptions =
                React.useMemo ((fun () -> Impl.adjustPassive options), [| options |])

            let removeOptions =
                React.useMemo ((fun () -> Impl.createRemoveOptions options), [| options |])

            let fn = React.useMemo ((fun () -> unbox<#Event> >> action), [| action |])

            let listener =
                React.useCallback (fun () ->
                    elemRef.current
                    |> Option.iter (fun elem ->
                        match addOptions with
                        | Some options -> elem.addEventListener (eventType, fn, options)
                        | None -> elem.addEventListener (eventType, fn)
                    )

                    FsReact.createDisposable (fun () ->
                        elemRef.current
                        |> Option.iter (fun elem ->
                            match removeOptions with
                            | Some options -> elem.removeEventListener (eventType, fn, options)
                            | None -> elem.removeEventListener (eventType, fn)
                        )
                    )
                )

            React.useEffect (listener)

        static member inline onResize
            (elemRef: IRefValue<#HTMLElement option>, action: Event -> unit, ?options: AddEventListenerOptions)
            =
            useElementListener.on (elemRef, "resize", action, ?options = options)

        static member inline onKeyDown
            (elemRef: IRefValue<#HTMLElement option>, action: KeyboardEvent -> unit, ?options: AddEventListenerOptions)
            =
            useElementListener.on (elemRef, "keydown", action, ?options = options)

/// Starts the Web Worker of a definition (see Util/WebWorker.fs) for as long as the component is mounted.
///
/// Returns the definition and a stable dispatch function. The definition is returned as it came in, so a
/// caller that does not need it writes `let _, dispatch = useWebWorker MyWorker.Worker`. The worker is created
/// in an effect and terminated on cleanup. A request sent before that effect has run, or after the worker has
/// failed, runs the handler on the main thread. A request that is still pending when the component unmounts
/// never settles, so the caller has to ignore a late result itself.
///
/// The definition must be a module-level value, because a new definition starts a new worker.
[<Hook>]
let useWebWorker
    (def: Util.WebWorker.WorkerDef<'Req, 'Res>)
    : Util.WebWorker.WorkerDef<'Req, 'Res> * ('Req -> JS.Promise<'Res>) =
    let client = React.useRef<Util.WebWorker.WorkerClient<'Req, 'Res> option> None

    React.useEffect (
        (fun () ->
            let started = Util.WebWorker.start def
            client.current <- Some started

            FsReact.createDisposable (fun () ->
                client.current <- None
                started.Terminate()
            )
        ),
        [| box def |]
    )

    let dispatch =
        React.useCallback (
            (fun (req: 'Req) ->
                match client.current with
                | Some started -> started.Dispatch req
                | None -> def.Handle req
            ),
            [| box def |]
        )

    def, dispatch
