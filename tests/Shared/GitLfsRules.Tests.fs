module GitLfsRulesTests

#if FABLE_COMPILER
open Fable.Mocha
#else
open Expecto
#endif

open Arcitisa.Components.Shared

let tests =
    testList "GitLfsRules" [
        testList "isIsaMetadataFile" [
            testCase "matches isa.investigation.xlsx at the ARC root"
            <| fun _ ->
                Expect.isTrue
                    (GitLfsRules.isIsaMetadataFile "isa.investigation.xlsx")
                    "Root level isa metadata files should match."

            testCase "matches nested isa.study.xlsx"
            <| fun _ ->
                Expect.isTrue
                    (GitLfsRules.isIsaMetadataFile "studies/study_01/isa.study.xlsx")
                    "Nested isa metadata files should match."

            testCase "matches isa metadata files case-insensitively"
            <| fun _ ->
                Expect.isTrue
                    (GitLfsRules.isIsaMetadataFile "assays/assay_01/ISA.Assay.XLSX")
                    "Casing should not affect the match."

            testCase "matches windows-style separators"
            <| fun _ ->
                Expect.isTrue
                    (GitLfsRules.isIsaMetadataFile "studies\\study_01\\isa.datamap.xlsx")
                    "Backslash paths should be normalized before matching."

            testCase "does not match ordinary xlsx files"
            <| fun _ ->
                Expect.isFalse
                    (GitLfsRules.isIsaMetadataFile "assays/assay_01/measurements.xlsx")
                    "Ordinary spreadsheets should not match."

            testCase "does not match isa.xlsx without a middle segment"
            <| fun _ ->
                Expect.isFalse (GitLfsRules.isIsaMetadataFile "isa.xlsx") "isa.*.xlsx requires a middle name segment."

            testCase "does not match files inside an isa-named folder"
            <| fun _ ->
                Expect.isFalse
                    (GitLfsRules.isIsaMetadataFile "isa.study.xlsx/raw.bin")
                    "Only the file name itself should be inspected."

            testCase "does not match empty paths"
            <| fun _ -> Expect.isFalse (GitLfsRules.isIsaMetadataFile "") "Empty paths should not match."
        ]

        testList "isInDatasetFolder" [
            testCase "matches files directly inside a dataset folder"
            <| fun _ ->
                Expect.isTrue
                    (GitLfsRules.isInDatasetFolder "assays/assay_01/dataset/raw.bin")
                    "Files below a dataset folder should match."

            testCase "matches files nested deeper below a dataset folder"
            <| fun _ ->
                Expect.isTrue
                    (GitLfsRules.isInDatasetFolder "assays/assay_01/dataset/run_01/raw.bin")
                    "Deeper nesting below a dataset folder should match."

            testCase "matches dataset segments case-insensitively"
            <| fun _ ->
                Expect.isTrue
                    (GitLfsRules.isInDatasetFolder "assays/assay_01/DataSet/raw.bin")
                    "Casing of the dataset segment should not matter."

            testCase "does not match a file named dataset"
            <| fun _ ->
                Expect.isFalse
                    (GitLfsRules.isInDatasetFolder "assays/assay_01/dataset")
                    "The dataset segment must be a folder, not the item itself."

            testCase "does not match folders that only start with dataset"
            <| fun _ ->
                Expect.isFalse
                    (GitLfsRules.isInDatasetFolder "assays/assay_01/datasets/raw.bin")
                    "Only exact dataset segments should match."

            testCase "does not match empty paths"
            <| fun _ -> Expect.isFalse (GitLfsRules.isInDatasetFolder "") "Empty paths should not match."
        ]

        testList "exceedsNonLfsSizeLimit" [
            testCase "does not flag files at exactly 25 MB"
            <| fun _ ->
                Expect.isFalse
                    (GitLfsRules.exceedsNonLfsSizeLimit (Some(25L * 1024L * 1024L)))
                    "Exactly 25 MB should still be allowed outside LFS."

            testCase "flags files above 25 MB"
            <| fun _ ->
                Expect.isTrue
                    (GitLfsRules.exceedsNonLfsSizeLimit (Some(25L * 1024L * 1024L + 1L)))
                    "Files above 25 MB must stay in LFS."

            testCase "does not flag unknown sizes"
            <| fun _ ->
                Expect.isFalse
                    (GitLfsRules.exceedsNonLfsSizeLimit None)
                    "Unknown sizes should not trigger the size rule."
        ]

        testList "toggle blocking" [
            testCase "blocks marking isa metadata files as LFS"
            <| fun _ ->
                Expect.isSome
                    (GitLfsRules.tryGetMarkAsLfsBlockedReason "studies/study_01/isa.study.xlsx")
                    "isa.*.xlsx must not be marked as LFS."

            testCase "allows marking ordinary files as LFS"
            <| fun _ ->
                Expect.isNone
                    (GitLfsRules.tryGetMarkAsLfsBlockedReason "assays/assay_01/dataset/raw.bin")
                    "Ordinary files may be marked as LFS."

            testCase "blocks unmarking files inside a dataset folder"
            <| fun _ ->
                Expect.isSome
                    (GitLfsRules.tryGetUnmarkAsLfsBlockedReason "assays/assay_01/dataset/raw.bin" None)
                    "Files below dataset folders must stay LFS."

            testCase "blocks unmarking files above the size limit"
            <| fun _ ->
                Expect.isSome
                    (GitLfsRules.tryGetUnmarkAsLfsBlockedReason "large.bin" (Some(26L * 1024L * 1024L)))
                    "Files above 25 MB must stay LFS."

            testCase "allows unmarking small files outside dataset folders"
            <| fun _ ->
                Expect.isNone
                    (GitLfsRules.tryGetUnmarkAsLfsBlockedReason "docs/readme.pdf" (Some 1024L))
                    "Small files outside dataset folders may be unmarked."

            testCase "allows unmarking metadata files even when other rules would block them"
            <| fun _ ->
                Expect.isNone
                    (GitLfsRules.tryGetToggleBlockedReason
                        "assays/assay_01/dataset/isa.study.xlsx"
                        (Some(26L * 1024L * 1024L))
                        false)
                    "Metadata files stay inline under the revision policy."

            testCase "tryGetToggleBlockedReason routes by direction"
            <| fun _ ->
                Expect.isSome
                    (GitLfsRules.tryGetToggleBlockedReason "isa.investigation.xlsx" None true)
                    "Marking an isa metadata file must be blocked."

                Expect.isNone
                    (GitLfsRules.tryGetToggleBlockedReason "isa.investigation.xlsx" None false)
                    "Unmarking an isa metadata file must stay possible."
        ]
    ]
