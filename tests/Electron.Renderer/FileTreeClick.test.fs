module ElectronRenderer.FileTreeClickTests

open Renderer.Components.LeftSidebar.FileExplorer
open Swate.Components.Page.FileExplorer.Types
open Vitest

Vitest.describe (
    "FileTreeClick",
    fun () ->
        Vitest.test (
            "an entity folder click is classified as a folder",
            fun () ->
                let folder = {
                    FileTree.createFolder "A" (Some "assays/A") FileItemIcon.Folder with
                        Id = "assays/A"
                }

                Vitest.expect(FileTreeClick.targetOfClick folder).toEqual FileTreeClick.Folder
        )

        Vitest.test (
            "a workbook click is classified as that file",
            fun () ->
                let workbook = {
                    FileTree.createFile "isa.assay.xlsx" (Some "assays/A/isa.assay.xlsx") FileItemIcon.Document with
                        Id = "assays/A/isa.assay.xlsx"
                }

                Vitest
                    .expect(FileTreeClick.targetOfClick workbook)
                    .toEqual (FileTreeClick.File "assays/A/isa.assay.xlsx")
        )
)
