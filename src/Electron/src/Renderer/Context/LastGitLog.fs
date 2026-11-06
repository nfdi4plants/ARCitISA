module Renderer.Context.LastGitLog

open Feliz

/// The last Git log of this window, from sidebar operations and file tree actions, whichever ended later. The
/// Git sidebar and the DataHub page show it.
[<Hook>]
let useLastGitLog () : Swate.Components.Page.GitSidebarTypes.GitSidebarLastLog option =
    let gitStateCtx = GitStateContext.useGitStateCtx ()
    let workflow = gitStateCtx.state.LastLog
    let fileTree = LfsActivityContext.useLfsLastLogCtx ()

    React.useMemo (
        (fun () ->
            OperationLog.newest workflow fileTree
            |> Option.bind OperationLog.toSidebarLastLog
        ),
        [| box workflow; box fileTree |]
    )
