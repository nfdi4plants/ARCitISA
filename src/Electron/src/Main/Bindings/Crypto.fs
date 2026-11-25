module Main.Bindings.Crypto

open Fable.Core

type private Hash =
    abstract member update: byte[] -> Hash
    abstract member digest: string -> string

type private ReadStream =
    [<Emit("$0.on('data', $1)")>]
    abstract member onData: (byte[] -> unit) -> unit

    [<Emit("$0.on('end', $1)")>]
    abstract member onEnd: (unit -> unit) -> unit

    [<Emit("$0.on('error', $1)")>]
    abstract member onError: (obj -> unit) -> unit

[<Import("createHash", "crypto")>]
let private createHash (algorithm: string) : Hash = jsNative

[<Import("createReadStream", "fs")>]
let private createReadStream (path: string) : ReadStream = jsNative

/// The lowercase hex SHA-256 of the bytes.
let sha256Hex (bytes: byte[]) : string =
    (createHash "sha256").update(bytes).digest ("hex")

/// The lowercase hex SHA-256 of a file. The file is streamed, so it never sits in memory as a whole.
let hashFileAsync (path: string) : JS.Promise<string> =
    JS.Constructors.Promise.Create(fun resolve reject ->
        let hash = createHash "sha256"
        let stream = createReadStream path
        stream.onData (fun chunk -> hash.update chunk |> ignore)
        stream.onError reject
        stream.onEnd (fun () -> resolve (hash.digest "hex"))
    )

type private RandomBuffer =
    abstract member toString: string -> string

[<Import("randomBytes", "crypto")>]
let private randomBytes (byteCount: int) : RandomBuffer = jsNative

/// Lowercase hex of cryptographically random bytes. The result has twice as many characters as byteCount.
let randomHex (byteCount: int) : string = (randomBytes byteCount).toString "hex"
