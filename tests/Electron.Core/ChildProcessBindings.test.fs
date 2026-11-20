module ElectronCore.ChildProcessBindingsTests

open Fable.Core
open Main.Bindings
open Vitest

[<Emit("process.execPath")>]
let private processExecutable () : string = jsNative

Vitest.test (
    "process launchers preserve the working directory and arguments and report failed exits",
    fun () -> promise {
        let! directory = TestHelpers.createTempDirectoryAsync "swate launcher & spaces-"

        try
            do!
                ChildProcess.run
                    (processExecutable ())
                    [|
                        "-e"
                        "if (process.cwd() !== process.argv[1]) process.exit(2)"
                        directory
                    |]
                    directory

            let mutable failed = false

            try
                do! ChildProcess.run (processExecutable ()) [| "-e"; "process.exit(7)" |] directory
            with _ ->
                failed <- true

            Vitest.expect(failed).toBe true
        with error ->
            do! TestHelpers.removeDirectoryAsync directory
            return raise error

        do! TestHelpers.removeDirectoryAsync directory
    }
)

Vitest.test (
    "detached launches reject missing executables instead of leaving an unhandled process error",
    fun () -> promise {
        let mutable errorCode = None

        try
            do! ChildProcess.startDetached "swate-nonexistent-terminal-1146" [||] (Path.resolve [| "." |])
        with error ->
            errorCode <- Filesystem.tryGetNodeErrorCode error

        Vitest.expect(errorCode).toEqual (Some "ENOENT")
    }
)
