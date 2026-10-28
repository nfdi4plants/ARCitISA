module Main.Bindings.Node

open Fable.Core

[<Emit("process.platform")>]
let processPlatform () : string = jsNative

/// The value of an environment variable, or None when it is not set.
[<Emit("process.env[$0]")>]
let environmentVariable (name: string) : string option = jsNative

[<Import("homedir", "os")>]
let homeDirectory () : string = jsNative

[<Emit("$0.length")>]
let bufferLength (buffer: obj) : int = jsNative

[<Emit("$0[$1]")>]
let bufferByteAt (buffer: obj) (index: int) : int = jsNative

[<Emit("Buffer.concat($0)")>]
let bufferConcat (buffers: obj[]) : obj = jsNative

[<Emit("$0.subarray($1, $2)")>]
let bufferSubarray (buffer: obj) (startIndex: int) (endIndex: int) : obj = jsNative

[<Emit("$0.toString('utf8')")>]
let bufferToUtf8String (buffer: obj) : string = jsNative

[<Emit("Buffer.byteLength($0, 'utf8')")>]
let utf8ByteLength (text: string) : int = jsNative

/// The number of UTF-16 units of the text that encode into at most maxBytes of UTF-8.
/// TextEncoder.encodeInto never writes half a character, so a surrogate pair stays whole.
[<Emit("new TextEncoder().encodeInto($0, new Uint8Array($1)).read")>]
let utf8PrefixUnits (text: string) (maxBytes: int) : int = jsNative

[<Emit("performance.now()")>]
let performanceNow () : float = jsNative
