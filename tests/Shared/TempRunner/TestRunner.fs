module TempRunner

open Expecto
open Swate.Components.Page.FileExplorer.FileExplorerGitLfsHelper
open Swate.Components.Page.FileExplorer.Types
open Swate.Components.Shared.Tests

let private createLfsItem (downloaded: bool) (isPointer: bool) = {
    FileTree.createFile "data.bin" (Some "data.bin") FileItemIcon.Document with
        IsLFS = Some true
        Downloaded = Some downloaded
        IsLFSPointer = Some isPointer
        LfsActivity = Some "Busy"
}

let private lfsBusyTests =
    testList "FileExplorer LFS busy actions" [
        testCase "pill action stays disabled during download and is absent while freeing"
        <| fun _ ->
            let downloading = createLfsItem false true
            let freeing = createLfsItem true false

            match lfsPillAction downloading (Some ignore) with
            | Some action -> Expect.equal action.Disabled (Some true) "The downloading pill action must be disabled."
            | None -> failtest "A downloading LFS file must keep its pill action."

            // A file with a local copy has no pill action, so freeing it cannot be triggered from the pill.
            Expect.isNone (lfsPillAction freeing (Some ignore)) "A freeing LFS file must not offer a pill action."

        testCase "context menu actions stay disabled during download or free"
        <| fun _ ->
            let downloading = createLfsItem false true
            let freeing = createLfsItem true false

            for item in [ downloading; freeing ] do
                let actions = contextMenuItems item (fun _ _ -> ()) None None
                Expect.equal (List.length actions) 4 "Busy LFS files must keep all action entries in the menu."

                Expect.isTrue
                    (actions |> List.forall (fun action -> action.Disabled = Some true))
                    "Every context menu action must be disabled while an LFS operation runs."
    ]

[<EntryPoint>]
let main argv =
    runTestsWithCLIArgs [] argv (testList "TempRunner" [ shared; lfsBusyTests ])
