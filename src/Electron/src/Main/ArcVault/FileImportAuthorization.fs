module Main.FileImportAuthorization

open System
open System.Collections.Generic

type private AuthorizedSelection = {
    AuthorizationId: string
    AbsolutePaths: string[]
}

let private selectionsByWindow = Dictionary<int, AuthorizedSelection>()

/// Replaces any earlier, unused picker selection for this renderer window.
let issue windowId absolutePaths =
    let selection = {
        AuthorizationId = Guid.NewGuid().ToString("N")
        AbsolutePaths = Array.copy absolutePaths
    }

    selectionsByWindow.[windowId] <- selection

    selection.AuthorizationId

/// Picker authorizations are window-bound and single-use for attempts that pass the busy check.
let consume windowId authorizationId =
    match selectionsByWindow.TryGetValue windowId with
    | true, selection when
        not (String.IsNullOrWhiteSpace authorizationId)
        && String.Equals(selection.AuthorizationId, authorizationId, StringComparison.Ordinal)
        ->
        selectionsByWindow.Remove windowId |> ignore
        Ok(Array.copy selection.AbsolutePaths)
    | _ -> Error(exn "The selected files are not authorized for import. Pick the files again.")
