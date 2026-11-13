module Main.Bindings.ChildProcess

open Fable.Core

[<JS.Pojo>]
type Options(?cwd: string, ?detached: bool, ?stdio: string, ?windowsHide: bool) =
    member val cwd = cwd with get, set
    member val detached = detached with get, set
    member val stdio = stdio with get, set
    member val windowsHide = windowsHide with get, set

type ChildProcess =
    [<Emit("$0.once('error', $1)")>]
    abstract onceError: (exn -> unit) -> unit

    [<Emit("$0.once('spawn', $1)")>]
    abstract onceSpawn: (unit -> unit) -> unit

    abstract unref: unit -> unit

[<Import("spawn", "child_process")>]
let private spawn (command: string) (args: string[]) (options: Options) : ChildProcess = jsNative

[<Import("execFile", "child_process")>]
let private execFile
    (command: string)
    (args: string[])
    (options: Options)
    (callback: exn option -> string -> string -> unit)
    : ChildProcess =
    jsNative

/// Starts an independent interactive application without passing its arguments through a shell.
let startDetached command args cwd : JS.Promise<unit> =
    Promise.create (fun resolve reject ->
        let child =
            spawn command args (Options(cwd = cwd, detached = true, stdio = "ignore", windowsHide = false))

        child.onceError reject

        child.onceSpawn (fun () ->
            child.unref ()
            resolve ()
        )
    )

/// Waits for a short-lived launcher and reports a nonzero exit as a failure.
let run command args cwd : JS.Promise<unit> =
    Promise.create (fun resolve reject ->
        execFile
            command
            args
            (Options(cwd = cwd, windowsHide = true))
            (fun error _ _ ->
                match error with
                | Some error -> reject error
                | None -> resolve ()
            )
        |> ignore
    )
