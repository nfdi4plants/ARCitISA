module Main.Services.MarkdownResolverService.AssetProtocol

open Fable.Core
open Fable.Electron
open Fable.Electron.Main
open Fetch
open Arcitisa.Electron.Shared

/// Declares the scheme. Electron accepts this call only before the app is ready.
let registerScheme () : unit =
    protocol.registerSchemesAsPrivileged [|
        CustomScheme(MarkdownAssetUrl.Scheme, Types.CustomScheme.Privileges(standard = true, secure = true))
    |]

/// Answers one request. Only a GET with a known window and its current nonce reaches the file system.
let handleRequest
    (tryFindService: int -> MarkdownResolverService option)
    (method: string)
    (url: string)
    : JS.Promise<Response> =
    if method.ToUpperInvariant() <> "GET" then
        promise { return ResponseFactory.refusal 403 }
    else
        match MarkdownAssetUrl.tryParse url with
        | None -> promise { return ResponseFactory.refusal 400 }
        | Some parsed ->
            match tryFindService parsed.WindowId with
            | Some service when service.Authorizes parsed.Nonce -> service.ServeAsset parsed.ArcPath
            | _ -> promise { return ResponseFactory.refusal 403 }

/// Routes the requests of the scheme to the service of the window. Call it before the first window loads.
let install (tryFindService: int -> MarkdownResolverService option) : unit =
    protocol.handle (
        MarkdownAssetUrl.Scheme,
        fun request -> U2.Case2(handleRequest tryFindService request.``method`` request.url)
    )
