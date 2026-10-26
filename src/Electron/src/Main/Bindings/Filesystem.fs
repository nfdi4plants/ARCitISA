module Main.Bindings.Filesystem

open Fable.Core

[<JS.PojoAttribute>]
type MkdirOptions(?recursive: bool) =
    member val recursive: bool option = recursive with get, set

[<JS.PojoAttribute>]
type RmOptions(?recursive: bool, ?force: bool, ?maxRetries: int, ?retryDelay: int) =
    member val recursive: bool option = recursive with get, set
    member val force: bool option = force with get, set
    member val maxRetries: int option = maxRetries with get, set
    member val retryDelay: int option = retryDelay with get, set

[<JS.PojoAttribute>]
type ReaddirOptions(?withFileTypes: bool) =
    member val withFileTypes: bool option = withFileTypes with get, set

[<StringEnum(CaseRules.LowerFirst)>]
type TextEncoding = | Utf8

type Stats =
    abstract member isDirectory: unit -> bool
    abstract member isFile: unit -> bool
    abstract member isSymbolicLink: unit -> bool
    abstract member size: float
    abstract member mtimeMs: float

type Dirent =
    abstract member name: string
    abstract member isDirectory: unit -> bool
    abstract member isFile: unit -> bool
    abstract member isSymbolicLink: unit -> bool

/// Returns the Node.js system error code of a filesystem failure, such as ENOENT.
[<Emit("(typeof $0?.code === 'string' ? $0.code : undefined)")>]
let tryGetNodeErrorCode (error: exn) : string option = jsNative

/// Returns the path a Node.js system error refers to, when it carries one.
[<Emit("(typeof $0?.path === 'string' ? $0.path : undefined)")>]
let tryGetNodeErrorPath (error: exn) : string option = jsNative

[<Import("mkdirSync", "fs")>]
let mkdirSync (path: string) (options: MkdirOptions) : unit = jsNative

[<Import("existsSync", "fs")>]
let existsSync (path: string) : bool = jsNative

[<Import("readFileSync", "fs")>]
let readFileSync (path: string) (encoding: TextEncoding) : string = jsNative

[<Import("writeFileSync", "fs")>]
let writeFileSync (path: string) (content: string) (encoding: TextEncoding) : unit = jsNative

[<Import("renameSync", "fs")>]
let renameSync (oldPath: string) (newPath: string) : unit = jsNative

[<Import("unlinkSync", "fs")>]
let unlinkSync (path: string) : unit = jsNative

[<Import("readdirSync", "fs")>]
let readdirSync (path: string) : string array = jsNative

[<Import("mkdir", "fs/promises")>]
let mkdirAsync (path: string) (options: MkdirOptions) : JS.Promise<obj> = jsNative

[<Import("readFile", "fs/promises")>]
let readFileAsync (path: string) (encoding: TextEncoding) : JS.Promise<string> = jsNative

[<Import("readFile", "fs/promises")>]
let readFileBufferAsync (path: string) : JS.Promise<obj> = jsNative

[<Emit("$0.toString('base64')")>]
let bufferToBase64 (buffer: obj) : string = jsNative

let readFileBase64Async (path: string) : JS.Promise<string> =
    readFileBufferAsync path |> Promise.map bufferToBase64

[<Import("writeFile", "fs/promises")>]
let writeFileAsync (path: string) (content: string) (encoding: TextEncoding) : JS.Promise<unit> = jsNative

[<Import("rename", "fs/promises")>]
let renameAsync (oldPath: string) (newPath: string) : JS.Promise<unit> = jsNative

[<Import("rm", "fs/promises")>]
let rmAsync (path: string) (options: RmOptions) : JS.Promise<unit> = jsNative

[<Import("copyFile", "fs/promises")>]
let copyFileAsync (sourcePath: string) (targetPath: string) (mode: int) : JS.Promise<unit> = jsNative

[<Import("constants", "fs")>]
let private fileSystemConstants: {| COPYFILE_EXCL: int |} = jsNative

let copyFileExclusiveFlag = fileSystemConstants.COPYFILE_EXCL

[<Import("link", "fs/promises")>]
let linkAsync (existingPath: string) (newPath: string) : JS.Promise<unit> = jsNative

[<Import("stat", "fs/promises")>]
let statAsync (path: string) : JS.Promise<Stats> = jsNative

[<Import("lstat", "fs/promises")>]
let lstatAsync (path: string) : JS.Promise<Stats> = jsNative

[<JS.PojoAttribute>]
type LstatBigintOptions(bigint: bool) =
    member val bigint: bool = bigint with get, set

/// Stats read with `bigint: true`, so inode numbers above 2^53 stay exact.
type BigIntStats =
    abstract member dev: bigint
    abstract member ino: bigint

[<Import("lstat", "fs/promises")>]
let lstatBigintAsync (path: string) (options: LstatBigintOptions) : JS.Promise<BigIntStats> = jsNative

[<Import("readdir", "fs/promises")>]
let readdirAsync (path: string) : JS.Promise<string[]> = jsNative

[<Import("readdir", "fs/promises")>]
let readdirWithTypesAsync (path: string) (options: ReaddirOptions) : JS.Promise<Dirent[]> = jsNative

type ReadResult =
    abstract member bytesRead: int
    abstract member buffer: byte[]

type FileHandle =
    abstract member close: unit -> JS.Promise<unit>
    abstract member read: buffer: byte[] * offset: int * length: int * position: int -> JS.Promise<ReadResult>

[<Import("open", "fs/promises")>]
let openAsync (path: string) (flags: string) : JS.Promise<FileHandle> = jsNative

[<Emit("Buffer.alloc($0)")>]
let allocBuffer (length: int) : byte[] = jsNative

/// Decodes the first length bytes of a Node buffer as UTF-8. A leading BOM stays in the text, as with readFile.
[<Emit("$0.toString('utf8', 0, $1)")>]
let bufferToUtf8Prefix (buffer: byte[]) (length: int) : string = jsNative

/// Decodes the first length bytes with a TextDecoder. The label is a WHATWG encoding label such as utf-16le.
/// The decoder drops a leading BOM.
[<Emit("new TextDecoder($2).decode($0.subarray(0, $1))")>]
let bufferDecodePrefix (buffer: byte[]) (length: int) (encodingLabel: string) : string = jsNative

[<Emit("$0.toString('base64', 0, $1)")>]
let bufferToBase64Prefix (buffer: byte[]) (length: int) : string = jsNative
