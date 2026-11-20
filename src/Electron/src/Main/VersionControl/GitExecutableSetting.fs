/// The Git executable the user chose, kept in memory for the version control library. The library
/// asks for the selection whenever it starts Git or creates a Git client for an operation, so the
/// answer has to come without a file read. Main fills the cell at startup from the user defaults and
/// updates it after a saved change.
module Main.VersionControl.GitExecutableSetting

module GitExecution = VersionControlService.Git.GitExecution

let mutable private chosenPath: string option = None

/// The chosen absolute path. None means Git from PATH.
let get () : string option = chosenPath

/// Replaces the chosen path. Git starts and operations that begin after this call use it.
let set (path: string option) : unit = chosenPath <- path

/// Fills the cell and makes it the library's selection for every provider in this process. The
/// library keeps one selection, so call this once before the first provider runs.
let install (initialPath: string option) : unit =
    chosenPath <- initialPath
    GitExecution.setGitExecutableProvider (fun () -> chosenPath)
