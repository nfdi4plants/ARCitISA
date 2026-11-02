/// Thin renderer client over the provider-neutral version control IPC. It turns the
/// transport error into a string and leaves the structured operation result intact.
/// Every call takes the request with its operation id, so the caller owns the id it
/// may need to cancel the operation before the main process reports anything back.
module Renderer.VersionControlApiClient

open System
open Fable.Core
open Swate.Electron.Shared.IPCTypes
open Swate.Electron.Shared.VersionControlTypes

let private api: IVersionControlApi = Api.ipcVersionControlApi

let private call (invoke: unit -> JS.Promise<Result<'T, exn>>) : JS.Promise<Result<'T, string>> = promise {
    let! result = invoke ()
    return result |> Result.mapError _.Message
}

/// A fresh operation id.
let newOperationId () = Guid.NewGuid().ToString()

let getSessionInfo (dto: OperationRequestDto) = call (fun () -> api.getSessionInfo dto)

let cloneWorkspace (dto: CloneWorkspaceRequestDto) = call (fun () -> api.cloneWorkspace dto)

let initializeWorkspace (dto: InitializeWorkspaceRequestDto) =
    call (fun () -> api.initializeWorkspace dto)

let bindWorkspace (dto: BindWorkspaceRequestDto) = call (fun () -> api.bindWorkspace dto)

let cancelOperation (key: OperationRequestDto) =
    call (fun () -> api.cancelOperation key)

let checkDependencies (dto: OperationRequestDto) =
    call (fun () -> api.checkDependencies dto)

let installDependency (dto: InstallDependencyRequestDto) =
    call (fun () -> api.installDependency dto)

let getStatus (dto: OperationRequestDto) = call (fun () -> api.getStatus dto)

let listRefs (dto: OperationRequestDto) = call (fun () -> api.listRefs dto)

let createRef (dto: CreateRefRequestDto) = call (fun () -> api.createRef dto)

let preflightSwitchRef (dto: SwitchRefRequestDto) =
    call (fun () -> api.preflightSwitchRef dto)

let switchRef (dto: SwitchRefRequestDto) = call (fun () -> api.switchRef dto)

let createRevision (dto: CreateRevisionRequestDto) = call (fun () -> api.createRevision dto)

let restorePaths (dto: RestorePathsRequestDto) = call (fun () -> api.restorePaths dto)

let refreshSynchronization (dto: OperationRequestDto) =
    call (fun () -> api.refreshSynchronization dto)

let synchronize (dto: SynchronizeRequestDto) = call (fun () -> api.synchronize dto)

let resolveConflict (dto: ResolveConflictRequestDto) =
    call (fun () -> api.resolveConflict dto)

let finalizeConflict (dto: FinalizeConflictRequestDto) =
    call (fun () -> api.finalizeConflict dto)

let cancelConflict (dto: CancelConflictRequestDto) = call (fun () -> api.cancelConflict dto)

let listObjects (dto: OperationRequestDto) = call (fun () -> api.listObjects dto)

let materializeObject (dto: ObjectPathRequestDto) =
    call (fun () -> api.materializeObject dto)

let dematerializeObject (dto: ObjectPathRequestDto) =
    call (fun () -> api.dematerializeObject dto)

let materializeObjects (dto: ObjectPathsRequestDto) =
    call (fun () -> api.materializeObjects dto)

let dematerializeObjects (dto: ObjectPathsRequestDto) =
    call (fun () -> api.dematerializeObjects dto)

let getStoragePolicySettings (dto: OperationRequestDto) =
    call (fun () -> api.getStoragePolicySettings dto)

let setStoragePolicySettings (dto: StoragePolicySettingsRequestDto) =
    call (fun () -> api.setStoragePolicySettings dto)

let getUploadRightAway () =
    call (fun () -> api.getUploadRightAway ())

let setUploadRightAway (value: bool) =
    call (fun () -> api.setUploadRightAway value)

let setPathStoragePolicy (dto: PathStoragePolicyRequestDto) =
    call (fun () -> api.setPathStoragePolicy dto)

let pruneStorage (dto: OperationRequestDto) = call (fun () -> api.pruneStorage dto)

let deduplicateStorage (dto: OperationRequestDto) =
    call (fun () -> api.deduplicateStorage dto)

let getRepositoryWebUrl (dto: OperationRequestDto) =
    call (fun () -> api.getRepositoryWebUrl dto)

let clearStaleLock (dto: OperationRequestDto) = call (fun () -> api.clearStaleLock dto)

let openTextDiff (dto: OpenTextDiffRequestDto) = call (fun () -> api.openTextDiff dto)

let readTextDiffPage (dto: ReadTextDiffPageRequestDto) =
    call (fun () -> api.readTextDiffPage dto)

let replayTextDiffPage (dto: ReplayTextDiffPageRequestDto) =
    call (fun () -> api.replayTextDiffPage dto)

let expandTextDiff (dto: ExpandTextDiffRequestDto) = call (fun () -> api.expandTextDiff dto)

let readTextDiffLine (dto: ReadTextDiffLineRequestDto) =
    call (fun () -> api.readTextDiffLine dto)

let closeTextDiff (dto: TextDiffHandleRequestDto) = call (fun () -> api.closeTextDiff dto)
