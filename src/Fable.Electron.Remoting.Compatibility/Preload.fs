// An immensely simple implementation to allow Fable.Remoting like configuration.
// All we really care about in this implementation, is creating the proxy for making the calls.
// Serialization is handled entirely by electron, and argument calls for the proxies is immaterial since
// type safety is assured by FCS
// However, we will ensure the type signatures are correct for completeness.
module Fable.Electron.Remoting.Preload

open System
open System.ComponentModel
open Browser
open FSharp.Core
open Fable.Core
open Fable.Core.DynamicExtensions
open Fable.Core.JsInterop
open Fable.Core.Reflection
open FSharp.Reflection
open Fable.Electron
open Fable.Electron.Renderer
open Fable.SimpleJson
//%REMOTING_TYPE%START%
/// <summary>
/// Configuration of Proxy routers
/// </summary>
type RemotingConfig = {
    /// <summary>
    /// Used in the ApiNameMap to create the property name on the window that the proxy
    /// is exposed through.
    /// </summary>
    ApiNameBase: string
    /// <summary>
    /// A map that takes the <c>ApiNameBase</c> and the <c>TypeName</c> of the implementation to
    /// create the name of the property on the window that the proxy is exposed through.
    /// </summary>
    ApiNameMap: string -> string -> string
    /// <summary>
    /// A function that takes the <c>Type</c> name and the <c>Field</c> name to create a channel
    /// name which messages are proxied through.
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

[<EditorBrowsable(EditorBrowsableState.Never)>]
module private Proxy =
    let proxyFetch (typeName: string) (func: RecordField) (config: RemotingConfig) =
        let funcArgs: TypeInfo[] =
            match func.FieldType with
            | TypeInfo.Async _ -> [| func.FieldType |]
            | TypeInfo.Promise _ -> [| func.FieldType |]
            | TypeInfo.Func getArgs -> getArgs ()
            | _ -> failwithf $"Field %s{func.FieldName} does not have a valid definition"

        let makeChannelName = config.ChannelNameMap
        let argumentCount = (Array.length funcArgs) - 1
        let channelName = makeChannelName typeName func.FieldName

        let funcNeedParameters =
            match funcArgs with
            | [| TypeInfo.Async _ |] -> false
            | [| TypeInfo.Promise _ |] -> false
            | [| TypeInfo.Unit; TypeInfo.Async _ |] -> false
            | _ -> true

        fun arg0 arg1 arg2 arg3 arg4 arg5 arg6 arg7 ->
            let inputArguments =
                if funcNeedParameters then
                    Array.take argumentCount [|
                        box arg0
                        box arg1
                        box arg2
                        box arg3
                        box arg4
                        box arg5
                        box arg6
                        box arg7
                    |]
                else
                    [||]

            unbox<FSharpFunc<_, _>> <| ipcRenderer.invoke (channelName, inputArguments)

[<Erase>]
type Remoting =
    [<EditorBrowsable(EditorBrowsableState.Never)>]
    static member buildRendererToMainProxy(config: RemotingConfig, resolvedType: Type) =
        let schemaType = createTypeInfo resolvedType

        match schemaType with
        | TypeInfo.Record getFields ->
            let fields, recordType = getFields ()

            let recordFields = [|
                for field in fields do
                    let normalize n =
                        let fn = Proxy.proxyFetch recordType.Name field config

                        match n with
                        | 0 -> box (fn null null null null null null null null)
                        | 1 -> box (fun a -> fn a null null null null null null null)
                        | 2 ->
                            let proxyF a b = fn a b null null null null null null
                            unbox (System.Func<_, _, _> proxyF)
                        | 3 ->
                            let proxyF a b c = fn a b c null null null null null
                            unbox (System.Func<_, _, _, _> proxyF)
                        | 4 ->
                            let proxyF a b c d = fn a b c d null null null null
                            unbox (System.Func<_, _, _, _, _> proxyF)
                        | 5 ->
                            let proxyF a b c d e = fn a b c d e null null null
                            unbox (System.Func<_, _, _, _, _, _> proxyF)
                        | 6 ->
                            let proxyF a b c d e f = fn a b c d e f null null
                            unbox (System.Func<_, _, _, _, _, _, _> proxyF)
                        | 7 ->
                            let proxyF a b c d e f g = fn a b c d e f g null
                            unbox (System.Func<_, _, _, _, _, _, _, _> proxyF)
                        | 8 ->
                            let proxyF a b c d e f g h = fn a b c d e f g h
                            unbox (System.Func<_, _, _, _, _, _, _, _, _> proxyF)
                        | _ ->
                            failwithf
                                $"Cannot generate proxy function for %s{field.FieldName}. \
                                Only up to 8 arguments are supported. Consider using a record type as input"

                    let argumentCount =
                        match field.FieldType with
                        | TypeInfo.Async _ -> 0
                        | TypeInfo.Promise _ -> 0
                        | TypeInfo.Func getArgs -> Array.length (getArgs ()) - 1
                        | _ -> 0

                    normalize argumentCount
            |]

            let proxy = FSharpValue.MakeRecord(recordType, recordFields)
            contextBridge.exposeInMainWorld (config.ApiNameMap config.ApiNameBase resolvedType.Name, proxy)
        | _ ->
            failwithf
                $"Cannot build proxy. Exepected type %s{resolvedType.FullName} to be \
                a valid protocol definition which is a record of functions"

    [<EditorBrowsable(EditorBrowsableState.Never)>]
    static member buildMainToRendererProxy(config: RemotingConfig, resolvedType: Type) =
        let schemaType = createTypeInfo resolvedType
        let bridgeName = config.ApiNameMap config.ApiNameBase resolvedType.Name
        let makeChannelName = config.ChannelNameMap resolvedType.Name

        match schemaType with
        | TypeInfo.Record getFields ->
            let fields, recordType = getFields ()

            let recordFields = [|
                for field in fields do
                    let fieldName = field.FieldName
                    let channelName = makeChannelName fieldName

                    let func =
                        emitJsExpr
                            channelName
                            "(callback) => {
                                let disposed = false;
                                const listener = (_event, ...args) => callback(...args);
                                ipcRenderer.on($0, listener);
                                return () => {
                                    if (disposed) { return; }
                                    disposed = true;
                                    ipcRenderer.removeListener($0, listener);
                                };
                            }"

                    box func
            |]

            let proxy = FSharpValue.MakeRecord(recordType, recordFields)
            contextBridge.exposeInMainWorld (bridgeName, proxy)
        | _ ->
            failwithf
                $"Cannot build proxy. Exepected type %s{resolvedType.FullName} to be \
                a valid protocol definition which is a record of functions"

    /// <summary>
    /// Builds the preload router for a two way <c>Main &lt;-> Renderer</c> interprocess communication
    /// in Fable.Remoting style.
    /// </summary>
    /// <remarks>
    /// <para>All three processes must be built </para>
    /// </remarks>
    /// <param name="config"></param>
    static member inline buildTwoWayBridge<'t>(config: RemotingConfig) =
        Remoting.buildRendererToMainProxy (config, typeof<'t>)

    /// <summary>
    /// Builds the preload router for a <c>Main -> Renderer</c> interprocess communication
    /// in Fable.Remoting style.
    /// </summary>
    /// <param name="config"></param>
    static member inline buildBridge<'T>(config: RemotingConfig) =
        Remoting.buildMainToRendererProxy (config, typeof<'T>)
