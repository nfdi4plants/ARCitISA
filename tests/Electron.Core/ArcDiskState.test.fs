module ElectronCore.ArcDiskStateTests

open ARCtrl
open Main.ArcDiskState
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Swate.Components.Shared
open Vitest

let private withTempArc =
    TestHelpers.withTempArcWith "swate-arc-disk-state-" "ArcDiskStateArc"

let private snapshotOf caseInsensitive (files: (string * float * float) list) : ArcDiskSnapshot =
    files
    |> List.map (fun (relativePath, sizeBytes, mtimeMs) ->
        PathKey.ofPath caseInsensitive relativePath,
        {
            RelativePath = relativePath
            Fingerprint = {
                SizeBytes = sizeBytes
                MtimeMs = mtimeMs
            }
        }
    )
    |> Map.ofList

let private assayPath = "assays/Assay/isa.assay.xlsx"
let private studyPath = "studies/Study/isa.study.xlsx"

let private relativePathsOf (snapshot: ArcDiskSnapshot) =
    snapshot
    |> Map.toList
    |> List.map (fun (_, file) -> file.RelativePath)
    |> List.sort

Vitest.describe (
    "ArcDiskState diff",
    fun () ->
        Vitest.test (
            "equal snapshots have no changes",
            fun () ->
                let snapshot = snapshotOf false [ assayPath, 10.0, 1.0 ]
                Vitest.expect(diff snapshot snapshot).toEqual ([]: ArcDiskChange list)
        )

        Vitest.test (
            "a new file is added",
            fun () ->
                let known = snapshotOf false [ assayPath, 10.0, 1.0 ]
                let current = snapshotOf false [ assayPath, 10.0, 1.0; studyPath, 5.0, 1.0 ]
                Vitest.expect(diff known current).toEqual [ Added studyPath ]
        )

        Vitest.test (
            "a different size or modified time is a change",
            fun () ->
                let known = snapshotOf false [ assayPath, 10.0, 1.0; studyPath, 5.0, 1.0 ]
                let current = snapshotOf false [ assayPath, 11.0, 1.0; studyPath, 5.0, 2.0 ]
                Vitest.expect(diff known current).toEqual [ Changed assayPath; Changed studyPath ]
        )

        Vitest.test (
            "a file that is gone is removed",
            fun () ->
                let known = snapshotOf false [ assayPath, 10.0, 1.0 ]
                Vitest.expect(diff known Map.empty).toEqual [ Removed assayPath ]
        )

        Vitest.test (
            "paths differing only in case name one file when the file system ignores case",
            fun () ->
                let known = snapshotOf true [ assayPath, 10.0, 1.0 ]
                let current = snapshotOf true [ "Assays/ASSAY/isa.assay.xlsx", 10.0, 1.0 ]
                Vitest.expect(diff known current).toEqual ([]: ArcDiskChange list)
        )

        Vitest.test (
            "paths differing only in case name two files when the file system respects case",
            fun () ->
                let renamedPath = "assays/ASSAY/isa.assay.xlsx"
                let known = snapshotOf false [ assayPath, 10.0, 1.0 ]
                let current = snapshotOf false [ renamedPath, 10.0, 1.0 ]
                Vitest.expect(diff known current).toEqual [ Added renamedPath; Removed assayPath ]
        )
)

Vitest.describe (
    "ArcDiskState scan",
    fun () ->
        Vitest.test (
            "lists structural files and leaves out payload",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("Assay")))
                    (fun arcPath -> promise {
                        let datasetPath = join [| arcPath; "assays/Assay/dataset" |]
                        let! _ = mkdirAsync datasetPath (MkdirOptions(recursive = true))
                        do! writeFileAsync (join [| datasetPath; "data.txt" |]) "payload" TextEncoding.Utf8

                        let! snapshot = scan false arcPath

                        Vitest.expect(relativePathsOf snapshot |> List.contains assayPath).toBe (true)
                        Vitest.expect(relativePathsOf snapshot |> List.contains "isa.investigation.xlsx").toBe (true)

                        Vitest
                            .expect(relativePathsOf snapshot |> List.exists (fun path -> path.Contains "dataset"))
                            .toBe (false)
                    })
        )

        Vitest.test (
            "a rewritten file shows up as a change and an untouched one does not",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("Assay")))
                    (fun arcPath -> promise {
                        let! before = scan false arcPath
                        do! writeFileAsync (join [| arcPath; assayPath |]) "different content" TextEncoding.Utf8
                        let! after = scan false arcPath

                        Vitest.expect(diff before after).toEqual [ Changed assayPath ]
                    })
        )

        Vitest.test (
            "a file that vanishes during the scan counts as missing",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("Assay")))
                    (fun arcPath -> promise {
                        let statVanishingAssay (path: string) =
                            if path.EndsWith "isa.assay.xlsx" then
                                statAsync (path + ".vanished")
                            else
                                statAsync path

                        let! snapshot = scanWithAsync readdirWithTypesAsync statVanishingAssay false arcPath

                        Vitest.expect(relativePathsOf snapshot).toEqual [ "isa.investigation.xlsx" ]
                    })
        )

        Vitest.test (
            "a zone folder that vanishes during the scan counts as missing",
            fun () ->
                withTempArc
                    (fun arc -> arc.AddAssay(ArcAssay("Assay")))
                    (fun arcPath -> promise {
                        let readVanishingZone (path: string) (options: ReaddirOptions) =
                            if path.EndsWith "assays" then
                                readdirWithTypesAsync (path + ".vanished") options
                            else
                                readdirWithTypesAsync path options

                        let! snapshot = scanWithAsync readVanishingZone statAsync false arcPath

                        Vitest.expect(relativePathsOf snapshot).toEqual [ "isa.investigation.xlsx" ]
                    })
        )

        Vitest.test (
            "a missing ARC root is an error and not an empty ARC",
            fun () ->
                withTempArc
                    ignore
                    (fun arcPath -> promise {
                        let! outcome = scan false (join [| arcPath; "missing" |]) |> Promise.result

                        match outcome with
                        | Ok _ -> failwith "The scan should fail when the ARC root is missing."
                        | Error _ -> ()
                    })
        )
)

Vitest.describe (
    "ArcDiskState acknowledge",
    fun () ->
        Vitest.test (
            "an own write moves only the entries that changed between the scans around it",
            fun () ->
                let known =
                    snapshotOf false [
                        assayPath, 10., 1.
                        studyPath, 20., 1.
                        "LICENSE", 5., 1.
                    ]

                // The assay was written, the license was removed by the write and the study differed before it.
                let pre =
                    snapshotOf false [
                        assayPath, 10., 1.
                        studyPath, 21., 2.
                        "LICENSE", 5., 1.
                    ]

                let post = snapshotOf false [ assayPath, 12., 3.; studyPath, 21., 2. ]

                let acknowledged = acknowledge known pre post

                Vitest
                    .expect(acknowledged |> Map.find (PathKey.ofPath false assayPath))
                    .toEqual (post |> Map.find (PathKey.ofPath false assayPath))

                Vitest.expect(diff acknowledged post).toEqual [ Changed studyPath ]
        )
)
