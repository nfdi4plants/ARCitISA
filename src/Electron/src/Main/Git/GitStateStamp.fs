/// Reads the Git state that the focus check compares, straight from the .git folder.
module Main.GitStateStamp

open Fable.Core
open Main.Bindings.Filesystem
open Main.Bindings.Path

/// What the focus check compares to notice Git changes made outside the app.
/// Head is the commit id that HEAD resolves to.
type GitStateStamp = { Head: string; IndexMtimeMs: float }

/// Finds the git directory of a working tree without starting git. A .git folder is the git directory. A .git file
/// (worktrees, submodules) names it in a gitdir line, relative to the root unless the path is absolute.
let internal tryResolveGitDirAsync (root: string) : Fable.Core.JS.Promise<string option> = promise {
    let dotGit = join [| root; ".git" |]

    try
        let! stats = statAsync dotGit

        if stats.isDirectory () then
            return Some dotGit
        else
            let! content = readFileAsync dotGit TextEncoding.Utf8

            return
                content.Split('\n')
                |> Array.map (fun line -> line.Trim())
                |> Array.tryFind (fun line -> line.StartsWith("gitdir:", System.StringComparison.Ordinal))
                |> Option.map (fun line ->
                    let target = line.Substring("gitdir:".Length).Trim()

                    if isAbsolute target then
                        target
                    else
                        resolve [| root; target |]
                )
    with error ->
        if tryGetNodeErrorCode error = Some "ENOENT" then
            return None
        else
            return raise error
}

let private tryReadTextFileAsync (path: string) : Fable.Core.JS.Promise<string option> = promise {
    try
        let! content = readFileAsync path TextEncoding.Utf8
        return Some content
    with error ->
        if tryGetNodeErrorCode error = Some "ENOENT" then
            return None
        else
            return raise error
}

let rec firstSomeAsync
    (candidates: (unit -> Fable.Core.JS.Promise<string option>) list)
    : Fable.Core.JS.Promise<string option> =
    promise {
        match candidates with
        | [] -> return None
        | next :: rest ->
            match! next () with
            | Some found -> return Some found
            | None -> return! firstSomeAsync rest
    }

/// Resolves HEAD to a commit id without starting git. A symbolic HEAD is followed to the loose ref file and then
/// to packed-refs, in the git directory and in the common directory of a linked worktree. A detached HEAD already
/// holds the commit id. A ref that cannot be found keeps the HEAD text, which still changes when the branch switches.
let private readHeadCommitAsync (gitDir: string) : Fable.Core.JS.Promise<string> = promise {
    let! headText = readFileAsync (join [| gitDir; "HEAD" |]) TextEncoding.Utf8
    let head = headText.Trim()

    if not (head.StartsWith("ref:", System.StringComparison.Ordinal)) then
        return head
    else
        let refName = head.Substring("ref:".Length).Trim()
        let! commonText = tryReadTextFileAsync (join [| gitDir; "commondir" |])

        let directories =
            match commonText with
            | Some text -> [ gitDir; resolve [| gitDir; text.Trim() |] ]
            | None -> [ gitDir ]

        let loose =
            directories
            |> List.map (fun directory () -> promise {
                let! content = tryReadTextFileAsync (join [| directory; refName |])
                return content |> Option.map (fun text -> text.Trim())
            })

        let packed =
            directories
            |> List.map (fun directory () -> promise {
                let! content = tryReadTextFileAsync (join [| directory; "packed-refs" |])

                return
                    content
                    |> Option.bind (fun text ->
                        text.Split('\n')
                        |> Array.map (fun line -> line.Trim())
                        |> Array.tryFind (fun line ->
                            not (line.StartsWith("#", System.StringComparison.Ordinal))
                            && line.EndsWith(" " + refName, System.StringComparison.Ordinal)
                        )
                        |> Option.map (fun line -> line.Substring(0, line.IndexOf ' '))
                    )
            })

        let! found = firstSomeAsync (loose @ packed)
        return found |> Option.defaultValue head
}

let internal readGitStateStampAsync (gitDir: string) : Fable.Core.JS.Promise<GitStateStamp> = promise {
    let! head = readHeadCommitAsync gitDir

    // A fresh repository has no index file yet.
    let! indexMtimeMs = promise {
        try
            let! stats = statAsync (join [| gitDir; "index" |])
            return stats.mtimeMs
        with error ->
            if tryGetNodeErrorCode error = Some "ENOENT" then
                return 0.0
            else
                return raise error
    }

    return {
        Head = head
        IndexMtimeMs = indexMtimeMs
    }
}
