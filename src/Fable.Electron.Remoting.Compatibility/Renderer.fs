module Fable.Electron.Remoting.Renderer

open System
open System.ComponentModel
open Browser
open FSharp.Core
open Fable.Core
open Fable.Core.DynamicExtensions
open FSharp.Reflection
open Fable.Core.JsInterop
open Fable.SimpleJson
//%REMOTING_TYPE%START%
/// <summary>
/// Config for a proxy IPC router.
/// </summary>
type RemotingConfig = {
    /// <summary>
    /// An argument to the <c>ApiNameMap</c> that creates the name of the property on the
    /// <c>window</c> object that the proxy/api/IPC is exposed through.
    /// </summary>
    ApiNameBase: string
    /// <summary>
    /// <c>ApiNameMap</c> takes the <c>ApiNameBase</c> and <c>Type</c> name of the
    /// implementation to create the name of the property on the
    /// <c>window</c> object that the proxy/api/IPC is exposed through.
    /// </summary>
    ApiNameMap: string -> string -> string
    /// <summary>
    /// No effect on Renderer process.
    /// </summary>
    ChannelNameMap: string -> string -> string
}
//%REMOTING_TYPE%END%
[<Erase>]
module Remoting =
    let createIpc () = {
        ApiNameBase = "FABLE_REMOTING"
        ApiNameMap = fun baseName typeName -> sprintf $"%s{baseName}_{typeName}"
        ChannelNameMap = fun typeName fieldName -> sprintf $"%s{typeName}:%s{fieldName}"
    }

    [<System.Obsolete("This method will be replaced by `createIpc()`. Please use that instead.")>]
    let init = createIpc ()

    let withApiNameBase apiName config = { config with ApiNameBase = apiName }
    let withApiNameMap func config = { config with ApiNameMap = func }
    let withChannelNameMap func config = { config with ChannelNameMap = func }

[<Erase>]
type Remoting =
    [<EditorBrowsable(EditorBrowsableState.Never)>]
    static member buildProxySenderInternal(config: RemotingConfig, resolvedType: Type) =
        let schemaType = createTypeInfo resolvedType

        match schemaType with
        | TypeInfo.Record getFields ->
            let fields, recordType = getFields ()

            let bridgeName = config.ApiNameMap config.ApiNameBase resolvedType.Name

            let recordFields = [|
                for field in fields do
                    match window.Item(bridgeName) with
                    | null ->
                        failwith
                            $"No API found on window for bridge name {bridgeName}. This might be because a related bridge in preload is missing."
                    | bridge ->
                        match bridge.Item(field.FieldName) with
                        | null -> failwith $"No API found on bridge {bridgeName} for field {field.FieldName}"
                        | apiFn -> apiFn
            |]

            let proxy = FSharpValue.MakeRecord(recordType, recordFields)
            unbox proxy
        | _ ->
            failwithf
                $"Cannot build proxy. Expected type %s{resolvedType.FullName} to be \
                a valid protocol definition which is a record of functions"

    [<EditorBrowsable(EditorBrowsableState.Never)>]
    static member buildProxyReceiverInternal(impl, config: RemotingConfig, resolvedType: Type) : unit -> unit =
        let schemaType = createTypeInfo resolvedType
        let bridgeName = config.ApiNameMap config.ApiNameBase resolvedType.Name

        match schemaType with
        | TypeInfo.Record getFields ->
            let fields, recordType = getFields ()
            let disposers = ResizeArray<unit -> unit>()

            for field in fields do
                let callSite = window.Item(bridgeName).Item(field.FieldName)
                let fieldTarget = impl.Item(field.FieldName)

                let disposer: unit -> unit =
                    emitJsExpr (callSite, fieldTarget, impl) "$0((...args) => { return $1(...args) })"

                disposers.Add disposer

            let mutable disposed = false

            fun () ->
                if not disposed then
                    disposed <- true

                    for dispose in disposers do
                        dispose ()
        | _ ->
            failwithf
                $"Cannot build proxy. Expected type %s{resolvedType.FullName} to be \
                a valid protocol definition which is a record of functions"

    /// <summary>
    /// Builds the client for the <c>Main &lt;-> Renderer</c> two way IPC communication.
    /// </summary>
    /// <param name="config"></param>
    static member inline buildProxySender<'t>(config: RemotingConfig) : 't =
        Remoting.buildProxySenderInternal (config, typeof<'t>)

    /// <summary>
    /// Builds the receiver for the <c>Main -> Renderer</c> IPC communication.
    /// </summary>
    /// <param name="impl">The implemented record of functions that respond to messages.</param>
    /// <param name="config"></param>
    static member inline buildProxyReceiverDisposable<'T> (impl: 'T) (config: RemotingConfig) : unit -> unit =
        Remoting.buildProxyReceiverInternal (impl, config, typeof<'T>)

    /// <summary>
    /// Builds the receiver for the <c>Main -> Renderer</c> IPC communication.
    /// </summary>
    /// <param name="impl">The implemented record of functions that respond to messages.</param>
    /// <param name="config"></param>
    static member inline buildProxyReceiver<'T> (impl: 'T) (config: RemotingConfig) =
        Remoting.buildProxyReceiverInternal (impl, config, typeof<'T>) |> ignore

    [<System.Obsolete("This method will be replaced by `buildProxySender` in a future version. Please use those instead.")>]
    static member inline buildClient<'T>(config: RemotingConfig) : 'T =
        Remoting.buildProxySenderInternal (config, typeof<'T>)

    [<System.Obsolete("This method will be replaced by `buildProxyReceiver` and `buildProxyReceiverDisposable` in a future version. Please use those instead.")>]
    static member inline buildHandler<'T> (impl: 'T) (config: RemotingConfig) : unit =
        Remoting.buildProxyReceiverInternal (impl, config, typeof<'T>) |> ignore
