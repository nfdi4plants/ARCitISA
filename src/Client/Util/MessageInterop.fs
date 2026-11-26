module MessageInterop

open System
open FSharp.Reflection
open Fable.Core
open System.Collections.Generic
open Fable.Core.JsInterop

type private IMessagePayload = {|
    arcitisa: bool
    api: string option
    requestId: string
    data: obj option
    error: string option
|}

type private PendingRequests = Dictionary<string, (obj -> unit) * (exn -> unit)>

/// This serves as container for queries.
/// Might be a good idea to use NanoStores: https://github.com/nanostores/nanostores ...
/// ... or to integrate within elmish.
let private PendingRequests = PendingRequests()

type InteropOptions = {
    Target: unit -> Browser.Types.Window
    GenericErrorHandler: exn -> unit
}

open System
open Fable.Core
open Fable.SimpleJson
open Browser.Types


module MessageInteropHelper =

    let private sendMsgWithResponse (target: unit -> Browser.Types.Window) (payload: IMessagePayload) =
        Promise.create (fun resolve reject ->
            // create timeout for response
            let timeout =
                Fable.Core.JS.setTimeout
                    (fun () ->
                        PendingRequests.Remove(payload.requestId) |> ignore
                        reject (new TimeoutException("Request timed out"))
                    )
                    5000

            PendingRequests.Add(payload.requestId, (resolve, reject))
            target().postMessage (payload, "*")
        )

    let rec private getReturnType typ =
        if Reflection.FSharpType.IsFunction typ then
            let _, res = Reflection.FSharpType.GetFunctionElements typ
            getReturnType res
        elif typ.IsGenericType then
            typ.GetGenericArguments() |> Array.head
        else
            typ

    let private proxyCall (target: unit -> Browser.Types.Window) (func: RecordField) =

        let argumentType: TypeInfo =
            match func.FieldType with
            | TypeInfo.Func getArgs ->
                match getArgs () with
                | [| _ as typeInfo; TypeInfo.Promise _ |] -> typeInfo
                | anyElse ->
                    failwithf
                        "MessageInterop-Error: Only Promise return types with 1 argument are supported for outgoing messages: %A"
                        anyElse
            | _ -> failwithf "MessageInterop-Error: Field %s does not have a valid definiton" func.FieldName

        let executeRequest =

            fun requestBody -> sendMsgWithResponse target requestBody

        fun arg0 ->

            let data: obj =
                match argumentType with
                | TypeInfo.Unit -> [||]
                | TypeInfo.Tuple _ -> arg0
                | _ -> arg0

            let requestBody: IMessagePayload = {|
                arcitisa = true
                api = Some func.FieldName
                data = Some data
                requestId = System.Guid.NewGuid().ToString()
                error = None
            |}

            executeRequest requestBody

    // Function to generate a new instance dynamically
    let buildOutProxyInner (target: unit -> Browser.Types.Window, resolvedType: Type) : 'T =

        if not (FSharpType.IsRecord resolvedType) then
            failwithf "MessageInterop-Error: Provided type is not a record. %s" resolvedType.FullName

        let schemaType = createTypeInfo resolvedType

        match schemaType with
        | TypeInfo.Record getFields ->
            let (fields, recordType) = getFields ()

            let recordFields = [|
                for field in fields do
                    let normalize n =
                        let fn = proxyCall target field
                        // this match case comes from Fable.Remoting
                        // https://github.com/Zaid-Ajaj/Fable.Remoting/blob/9bf4dab1987abad342c671cb4ff1a8a7e0e846d0/Fable.Remoting.Client/Remoting.fs#L58
                        // I cannot trigger any case other than 1 arguments, as all record type arguments are parsed into a tuple
                        match n with
                        | 0 -> box (fn null)
                        | 1 -> box (fun a -> fn a)
                        | _ ->
                            failwithf
                                "MessageInterop-Error: Cannot generate proxy function for %s. Only up to 1 argument is supported. Consider using a record type as input"
                                field.FieldName

                    let argumentCount =
                        match field.FieldType with
                        | TypeInfo.Async _ -> 0
                        | TypeInfo.Promise _ -> 0
                        | TypeInfo.Func getArgs -> Array.length (getArgs ()) - 1
                        | _ -> 0

                    normalize argumentCount
            |]

            let proxy = FSharpValue.MakeRecord(recordType, recordFields)
            unbox<'T> proxy
        | _ ->
            failwithf
                "MessageInterop-Error: Cannot build proxy. Exepected type %s to be a valid protocol definition which is a record of functions"
                resolvedType.FullName

    let buildInProxyInner
        (recordType: 'i, recordTypeType: Type, target: unit -> Browser.Types.Window, handleGenericError)
        =

        let schemaType = createTypeInfo recordTypeType

        match schemaType with
        | TypeInfo.Record getFields ->
            let (fields, _) = getFields ()

            for field in fields do
                let funcArgs: (TypeInfo[]) =
                    match field.FieldType with
                    | TypeInfo.Async _ -> [| field.FieldType |]
                    | TypeInfo.Promise _ -> [| field.FieldType |]
                    | TypeInfo.Func getArgs -> getArgs ()
                    | _ -> failwithf "MessageInterop-Error: Field %s does not have a valid definiton" field.FieldName

                let returnTypeAsync = Array.last funcArgs

                match returnTypeAsync with
                | TypeInfo.Promise _ -> ()
                | _ -> failwith "MessageInterop-Error: Only Promise return types are supported for incoming messages"
        | _ -> ()

        let verifyMsg (e: Browser.Types.MessageEvent) =
            let content = e.data :?> IMessagePayload
            if content.arcitisa then Some content else None

        let getEventHandlerByName (inst: 'A) (s: string) =
            let fields = Microsoft.FSharp.Reflection.FSharpType.GetRecordFields(recordTypeType)

            match fields |> Array.tryFind (fun t -> t.Name = s) with
            | Some pi -> Some(pi.GetValue(inst))
            | None -> None

        let runApiFromName (apiHandler: 'E) (apiName: string) (data: 'A) =
            let func = getEventHandlerByName apiHandler apiName

            match func with
            | Some f ->
                let f: 'A -> JS.Promise<obj> = !!f
                f data
            | None -> failwith $"MessageInterop-Error: No such API function found in Incoming API: {apiName}"

        // TODO: support async functions
        let resolveIncMessage (apiHandler: 'E) (content: IMessagePayload) =
            match content.api with
            | Some api ->
                promise {
                    let! payload =
                        try
                            promise {
                                let! r = runApiFromName apiHandler api content.data
                                let p: IMessagePayload = {| content with data = Some !!r |}
                                return p
                            }
                        with exn ->
                            let p: IMessagePayload = {|
                                content with
                                    error = Some exn.Message
                                    data = None
                            |}

                            Promise.lift p
                        |> Promise.map (fun (payload: IMessagePayload) ->
                            let result: IMessagePayload = {| payload with api = None |}
                            result
                        )

                    target().postMessage (payload, "*")
                }
                |> Promise.start
            | None ->
                let payload: IMessagePayload = {|
                    content with
                        error = Some "No API name given!"
                |}

                target().postMessage (payload, "*")

        let handle =
            fun (e: Browser.Types.Event) ->
                let e = e :?> Browser.Types.MessageEvent

                match verifyMsg e with
                | Some content ->
                    if content.error.IsSome then
                        let exn = new Exception(content.error.Value)

                        match PendingRequests.TryGetValue(content.requestId) with
                        | true, (_, reject) ->
                            PendingRequests.Remove(content.requestId) |> ignore
                            reject exn
                        | _ -> handleGenericError exn
                    else
                        match PendingRequests.TryGetValue(content.requestId) with
                        | true, (resolve, _) ->
                            log $"[ARCitISA] Response from ARCitect"
                            PendingRequests.Remove(content.requestId) |> ignore
                            resolve content.data
                        | _ ->
                            log $"[ARCitISA] Request from ARCitect: {content.api}"
                            resolveIncMessage recordType content
                | None -> ()

        Browser.Dom.window.addEventListener ("message", handle)
        fun () -> Browser.Dom.window.removeEventListener ("message", handle)

module MessageInterop =

    let createApi () : InteropOptions = {
        Target = fun () -> Browser.Dom.window.parent
        GenericErrorHandler = fun exn -> Browser.Dom.console.error ($"Proxy Error: {exn.Message}")
    }

    let withErrorHandler errorHandler options : InteropOptions = {
        options with
            GenericErrorHandler = errorHandler
    }

    let withTarget target options : InteropOptions = { options with Target = target }

type MessageInterop() =

    static member inline buildOutProxy<'o>(options: InteropOptions) : 'o =
        let outType = typeof<'o>
        MessageInteropHelper.buildOutProxyInner (options.Target, outType)

    /// Returns a function to remove the event listener
    static member inline buildInProxy<'i> (incomingMsgHandler: 'i) (options: InteropOptions) : (unit -> unit) =
        let inType: Type = typeof<'i>
        MessageInteropHelper.buildInProxyInner (incomingMsgHandler, inType, options.Target, options.GenericErrorHandler)
