module Main.Bindings.Chokidar


open Fable.Core
open Fable.Core.JS

[<StringEnum(CaseRules.LowerFirst)>]
type Events =
    | Add
    | Change
    | Outer
    | Unlink
    | AddDir
    | UnlinkDir
    | Error
    | Ready
    | Raw
    | All

/// The part of fs.Stats that chokidar hands to the ignored callback. The value is absent during some directory reads.
type IWatchedStats =
    abstract member isDirectory: unit -> bool

/// Chokidar's object form of awaitWriteFinish. Both values are milliseconds.
[<Pojo>]
type AwaitWriteFinishOptions(?stabilityThreshold: int, ?pollInterval: int) =
    member val stabilityThreshold: int option = stabilityThreshold with get, set
    member val pollInterval: int option = pollInterval with get, set

[<Pojo>]
type WatchOptions
    (
        ?persistent: bool,
        ?ignored: U4<string, ResizeArray<string>, (string -> bool), System.Func<string, IWatchedStats option, bool>>,
        ?ignoreInitial: bool,
        ?followSimlinks: bool,
        ?cwd: string,
        ?awaitWriteFinish: U2<bool, AwaitWriteFinishOptions>,
        ?usePolling: bool,
        ?interval: int,
        ?binaryInterval: int
    ) =
    member val persistent: bool option = persistent with get, set
    member val ignored = ignored with get, set
    member val ignoreInitial: bool option = ignoreInitial with get, set
    member val followSimlinks: bool option = followSimlinks with get, set
    member val cwd: string option = cwd with get, set
    member val awaitWriteFinish = awaitWriteFinish with get, set
    member val usePolling: bool option = usePolling with get, set
    member val interval: int option = interval with get, set
    member val binaryInterval: int option = binaryInterval with get, set

type IWatched =
    [<EmitIndexerAttribute>]
    abstract member item: path: string -> string[] with get

type IWatcher =
    abstract member close: unit -> Promise<unit>
    abstract member add: paths: string -> IWatcher
    abstract member add: paths: string[] -> IWatcher
    abstract member unwatch: paths: string -> IWatcher
    abstract member unwatch: paths: string[] -> IWatcher
    abstract member on: eventName: Events * callback: (string -> unit) -> IWatcher
    abstract member on: eventName: Events * callback: (exn -> unit) -> IWatcher
    abstract member on: eventName: Events * callback: (unit -> unit) -> IWatcher
    abstract member on: eventName: Events * callback: (string -> string -> unit) -> IWatcher

    abstract member getWatched: unit -> IWatched


[<Erase>]
type Chokidar =

    [<Import("watch", "chokidar")>]
    static member watch(paths: string, options: WatchOptions) : IWatcher = jsNative

    [<Import("watch", "chokidar")>]
    static member watch(paths: string[], options: WatchOptions) : IWatcher = jsNative

    [<Import("watch", "chokidar")>]
    [<ParamObjectAttribute(1)>]
    static member watch
        (paths: string, ?persistent: bool, ?ignored: string, ?ignoreInitial: bool, ?followSymlinks: bool, ?cwd: string)
        : IWatcher =
        jsNative


// let watcher = Chokidar.watch("C:/Users/User/source/repos/Fable.Electron/src/main", persistent = true, ignoreInitial = true, followSymlinks = true)
// let watcher = Chokidar.watch("./", WatchOptions(persistent = true, ignoreInitial = true, followSimlinks = true))

// watcher
//     .on(Events.Add, fun path -> console.log($"File added {path}"))
//     .on(Events.Change, fun path -> console.log($"File changed {path}"))
//     .on(Events.Unlink, fun path -> console.log($"File unlinked {path}"))
//     .on(Events.Ready, fun path ->
//         let getWatched = watcher.getWatched()
//         console.log($"getWatched: {getWatched}")
//         console.log(getWatched)
//     )
