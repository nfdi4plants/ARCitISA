module LandingTests

#if FABLE_COMPILER
open Fable.Mocha
#else
open Expecto
#endif

open ARCtrl
open Arcitisa.Components.Page.Landing
open Arcitisa.Components.Shared

let private mkPerson (firstName: string) (lastName: string) =
    Person.create (firstName = firstName, lastName = lastName)

let private mkComment (name: string) (value: string) = Comment(name, value)

let private mkDescriptor (name: string) (accession: string) =
    OntologyAnnotation.create (name, "MS", accession)

let private expectBasicAnnotationTable (table: ArcTable) (expectedName: string) =
    Expect.equal table.Name expectedName "The default table should use the identifier name."
    Expect.equal table.ColumnCount 3 "The default table should create basic starter columns."

    Expect.equal
        table.RowCount
        ARCtrlHelper.ArcFileDefaults.BasicAnnotationTableRowCount
        "The default table should create starter rows."

    Expect.equal
        (table.Headers.[0].ToString())
        "Input [Source Name]"
        "The default table should start with an input column."

    Expect.equal (table.Headers.[1].ToString()) "Protocol Uri" "The default table should include a protocol column."

    Expect.equal
        (table.Headers.[2].ToString())
        "Output [Sample Name]"
        "The default table should end with an output column."

let private mkBaseDraft () = {
    LandingDraft.init with
        Identifier = "study_01"
        Title = " My Study Title "
        Description = " My Study Description "
        InvolvedPeople = ResizeArray([ mkPerson "Ada" "Lovelace"; mkPerson "Grace" "Hopper" ])
        Comments = ResizeArray([ mkComment "source" "generated"; mkComment "batch" "42" ])
        MainText = " Protocol body "
        Files = [ "raw/file_1.fastq"; " raw/file_2.fastq "; " " ]
        SubmissionDate = " 2026-01-10T13:45 "
        PublicReleaseDate = " 2026-02-11T08:30 "
        StudyDesignDescriptors =
            ResizeArray(
                [
                    mkDescriptor "design one" "MS:1000001"
                    mkDescriptor "design two" "MS:1000002"
                ]
            )
        MeasurementType = Some(mkDescriptor "measurement" "MS:1000100")
        TechnologyType = Some(mkDescriptor "technology" "MS:1000200")
        TechnologyPlatform = Some(mkDescriptor "platform" "MS:1000300")
}

let tests =
    testList "Landing" [
        testCase "Validation.toOptionalString trims and filters empty input"
        <| fun _ ->
            Expect.equal (Validation.toOptionalString "  value  ") (Some "value") "Value should be trimmed."
            Expect.equal (Validation.toOptionalString "   ") None "Whitespace-only values should become None."

        testCase "Validation.resolveIdentifier prefers explicit identifier"
        <| fun _ ->
            let draft = {
                mkBaseDraft () with
                    Identifier = "  explicit_id  "
            }

            Expect.equal (Validation.resolveIdentifier draft) "explicit_id" "Explicit identifier should be used."

        testCase "Validation.resolveIdentifier sanitizes explicit identifier input"
        <| fun _ ->
            let draft = {
                mkBaseDraft () with
                    Identifier = " ../../../etc/passwd "
            }

            Expect.equal
                (Validation.resolveIdentifier draft)
                "etc_passwd"
                "Unsafe path-like characters should be removed from explicit identifiers."

        testCase "Validation.resolveIdentifier falls back to title when explicit identifier is unusable"
        <| fun _ ->
            let draft = {
                mkBaseDraft () with
                    Identifier = " ...////... "
                    Title = "Fallback From Title"
            }

            Expect.equal
                (Validation.resolveIdentifier draft)
                "Fallback_From_Title"
                "Empty/invalid explicit identifiers should fall back to the title-derived identifier."

        testCase "Validation.resolveIdentifier falls back to sanitized title"
        <| fun _ ->
            let draft = {
                mkBaseDraft () with
                    Identifier = ""
                    Title = "A/B:C   test"
            }

            Expect.equal
                (Validation.resolveIdentifier draft)
                "A_B_C_test"
                "Title-derived identifier should be sanitized."

        testCase "Validation.isRequiredDataValid enforces title and description"
        <| fun _ ->
            let baseDraft = mkBaseDraft ()
            Expect.isTrue (Validation.isRequiredDataValid baseDraft) "Valid draft should pass."

            Expect.isFalse
                ({ baseDraft with Title = "   " } |> Validation.isRequiredDataValid)
                "Empty title should fail."

            Expect.isFalse
                ({ baseDraft with Description = "" } |> Validation.isRequiredDataValid)
                "Empty description should fail."

        testCase "Conversion.toArcFile maps study fields and initializes a basic identifier-named annotation table"
        <| fun _ ->
            let draft = mkBaseDraft ()
            let identifier, arcFile = Conversion.toArcFile draft LandingTarget.Study

            Expect.equal identifier "study_01" "Identifier should be preserved."

            match arcFile with
            | ArcFiles.Study(study, _) ->
                Expect.equal study.Identifier "study_01" "Study identifier should be set."
                Expect.equal study.Title (Some "My Study Title") "Study title should be trimmed and set."

                Expect.equal
                    study.Description
                    (Some "My Study Description")
                    "Study description should be trimmed and set."

                Expect.equal study.Contacts.Count 2 "Involved people should be copied to contacts."
                Expect.equal study.Comments.Count 2 "Comments should be copied."
                Expect.equal study.StudyDesignDescriptors.Count 2 "Study descriptors should be copied."
                Expect.equal study.SubmissionDate (Some "2026-01-10T13:45") "Submission date should be trimmed."
                Expect.equal study.PublicReleaseDate (Some "2026-02-11T08:30") "Public release date should be trimmed."

                expectBasicAnnotationTable study.Tables.[0] "study_01 Table"
            | _ -> failwith "Expected ArcFiles.Study"

        testCase "Conversion.toArcFile maps assay fields and initializes a basic identifier-named annotation table"
        <| fun _ ->
            let draft = {
                mkBaseDraft () with
                    Identifier = "assay_01"
            }

            let identifier, arcFile = Conversion.toArcFile draft LandingTarget.Assay

            Expect.equal identifier "assay_01" "Identifier should be preserved."

            match arcFile with
            | ArcFiles.Assay assay ->
                Expect.equal assay.Identifier "assay_01" "Assay identifier should be set."
                Expect.equal assay.Title (Some "My Study Title") "Assay title should be trimmed and set."

                Expect.equal
                    assay.Description
                    (Some "My Study Description")
                    "Assay description should be trimmed and set."

                Expect.equal assay.Performers.Count 2 "Involved people should be copied to performers."
                Expect.equal assay.Comments.Count 2 "Comments should be copied."
                Expect.equal assay.MeasurementType draft.MeasurementType "Measurement type should be copied."
                Expect.equal assay.TechnologyType draft.TechnologyType "Technology type should be copied."
                Expect.equal assay.TechnologyPlatform draft.TechnologyPlatform "Technology platform should be copied."

                expectBasicAnnotationTable assay.Tables.[0] "assay_01 Table"
            | _ -> failwith "Expected ArcFiles.Assay"

        testCase "Conversion.toSubmitPayload includes protocol intent when main text is present"
        <| fun _ ->
            let draft = mkBaseDraft ()
            let payload = Conversion.toSubmitPayload draft LandingTarget.Study

            Expect.equal payload.Identifier "study_01" "Payload identifier should match resolved identifier."
            Expect.equal payload.Target LandingTarget.Study "Payload target should be preserved."

            match payload.ProtocolIntent with
            | Some protocol ->
                Expect.equal
                    protocol.RelativePath
                    "studies/study_01/protocols/study_01_protocol.md"
                    "Protocol path should target study protocols."

                Expect.equal protocol.Content "Protocol body" "Protocol content should be trimmed."
            | None -> failwith "Expected protocol intent to be present."

        testCase "Conversion.toSubmitPayload sanitizes identifier used in protocol path"
        <| fun _ ->
            let draft = {
                mkBaseDraft () with
                    Identifier = "../../../etc/passwd"
                    MainText = "Protocol body"
            }

            let payload = Conversion.toSubmitPayload draft LandingTarget.Study

            Expect.equal payload.Identifier "etc_passwd" "Payload identifier should be sanitized."

            match payload.ProtocolIntent with
            | Some protocol ->
                Expect.equal
                    protocol.RelativePath
                    "studies/etc_passwd/protocols/etc_passwd_protocol.md"
                    "Protocol path should use the sanitized identifier."

                Expect.isFalse
                    (protocol.RelativePath.Contains "..")
                    "Protocol path must not contain traversal segments."

                Expect.isFalse (protocol.RelativePath.Contains "\\") "Protocol path must not contain backslashes."
            | None -> failwith "Expected protocol intent to be present."

        testCase "Conversion.toSubmitPayload omits protocol intent when main text is empty"
        <| fun _ ->
            let draft = { mkBaseDraft () with MainText = "   " }

            let payload = Conversion.toSubmitPayload draft LandingTarget.Assay
            Expect.equal payload.ProtocolIntent None "No protocol intent should be produced for empty main text."
    ]
