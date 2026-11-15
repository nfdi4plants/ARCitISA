module ElectronRenderer.FileTreeRenameModalTests

open Browser.Types
open Renderer.Components.LeftSidebar.FileExplorer.Modals
open Renderer.Components.LeftSidebar.FileExplorer.FileTreeRenameHelper
open Swate.Components.Page.FileExplorer.Types
open Swate.Components.Shared
open Swate.Electron.Shared.RenamePathRules
open Vitest

Vitest.afterEach (fun () -> RTL.cleanup ())

let private typeName name =
    RTL.fireEvent.change (RTL.screen.getByRole "textbox", {| target = {| value = name |} |})

let private button label =
    RTL.screen.getByRole ("button", ByRoleOptions(name = Text label)) :?> HTMLButtonElement

let private renderRename isFile path submit =
    let item =
        if isFile then
            FileTree.createFile "Old" (Some path) FileItemIcon.Document
        else
            FileTree.createFolder "Old" (Some path) FileItemIcon.Folder

    let draft =
        match tryBuildRenameDraft item with
        | Ok draft -> draft
        | Error error -> failwith error

    RTL.render (FileTreeRenameModal.Main(true, Some draft, ignore, submit))
    |> ignore

for kind, zone, label in
    [
        ArcFilesDiscriminate.Assay, "assays", "Assay"
        ArcFilesDiscriminate.Study, "studies", "Study"
        ArcFilesDiscriminate.Workflow, "workflows", "Workflow"
        ArcFilesDiscriminate.Run, "runs", "Run"
    ] do
    Vitest.test (
        $"{label} create and rename reject the same invalid identifier and submit trimmed valid names",
        fun () ->
            let mutable submitted = None
            let submit name = submitted <- Some name

            let checkDialog submitLabel =
                typeName "sample.v2"
                Vitest.expect((button submitLabel).disabled).toBe true
                Vitest.expect(RTL.screen.getByText(arcIdentifierError).hidden).toBe false
                RTL.fireEvent.custom ("keyDown", RTL.screen.getByRole "textbox", {| key = "Enter"; code = "Enter" |})
                Vitest.expect(submitted).toEqual None
                typeName "  Sample_2 - batch 3  "
                Vitest.expect((button submitLabel).disabled).toBe false
                RTL.fireEvent.click (button submitLabel)
                Vitest.expect(submitted).toEqual (Some "Sample_2 - batch 3")

            RTL.render (CreateArcFileModal.Main(true, kind, ignore, (fun _ name -> submit name)))
            |> ignore

            checkDialog $"Create {label}"
            RTL.cleanup ()
            submitted <- None
            renderRename false $"{zone}/Old" submit
            checkDialog "Rename"
    )

Vitest.test (
    "renaming a file directly in a collection permits dots and preserves the extension",
    fun () ->
        let mutable submitted = None
        renderRename true "studies/notes.md" (fun name -> submitted <- Some name)
        typeName "notes.v2.md"
        Vitest.expect((button "Rename").disabled).toBe false
        RTL.fireEvent.click (button "Rename")
        Vitest.expect(submitted).toEqual (Some "notes.v2.md")
)
