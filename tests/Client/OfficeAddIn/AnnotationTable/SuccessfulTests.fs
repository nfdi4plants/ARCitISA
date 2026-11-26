module OfficeAddIn.AnnotationTable.Successful

open Fable.Core
open Fable.Core.JsInterop
open Fable.Mocha

open ExcelJS.Fable.Excel

open ARCtrl
open Arcitisa.Components.Shared

open OfficeInterop
open ExcelHelper
open Core

// ⚠️ These tests do only work on node.js as the we sents user data to microsoft !!! ⚠️
// Check out https://github.com/OfficeDev/Office-Addin-Scripts/issues/905

// 💡 Read More
// - https://learn.microsoft.com/en-us/office/dev/add-ins/testing/unit-testing
// - RequestContext object: https://learn.microsoft.com/en-us/javascript/api/excel/excel.requestcontext?view=excel-js-preview

let compositeColumn =
    let header = CompositeHeader.Parameter(OntologyAnnotation.create ("Test"))
    CompositeColumn.create (header)

let arcTable =
    let table = ArcTable.init ("annotationTable 2")
    let header = CompositeHeader.Component(OntologyAnnotation.create ("TestI"))
    let body = CompositeCell.emptyTerm
    ArcTable.addColumnFill (header, body) table

let arcInvestigation =
    let investigation = ArcInvestigation.init ("New Investigation")
    ArcFiles.Investigation investigation

let arcAssay =
    let assay = ArcAssay.init ("New Assay")
    assay.AddTable(arcTable)
    ArcFiles.Assay assay

let arcStudy =
    let study = ArcStudy.init ("New Study")
    study.AddTable(arcTable)
    ArcFiles.Study(study, [])

let arcTemplate =
    let template = Template.init ("New Template")
    template.Table <- arcTable
    ArcFiles.Template template

let private TestsBasic =
    testList "Basic tests" [
        testCaseAsync "develop mock"
        <| async {
            let testContext: RequestContext =
                importDefault "../../Fixtures/OfficeMockObjects/BasicFixture.js"

            let! result = getSelectedRangeAdress testContext |> Async.AwaitPromise
            Expect.equal result "C2:G3" "Verify correct setup"
        }
    ]

let private TestsSuccessful =
    testList "Successful tests" [
        testCaseAsync "develop getTablesTest successful"
        <| async {
            let testContext: RequestContext =
                importDefault "../../Fixtures/OfficeMockObjects/AnnotationTableFixtureSuccessful.js"

            let! resultRes = getExcelAnnotationTables testContext |> Async.AwaitPromise

            let result =
                Expect.wantOk (Result.Ok resultRes) "develop getTablesTest successful failed"

            Expect.equal (Result.toOption result).Value.Count 1 $"Error: {result}"
        }

        testCaseAsync "develop rectifyTermColumnsTest successful"
        <| async {
            let testContext: RequestContext =
                importDefault "../../Fixtures/OfficeMockObjects/AnnotationTableFixtureSuccessful.js"

            let getTerms (termNames: string list) = promise { return [| None |] }

            let! resultRes =
                OfficeInterop.Core.Main.rectifyTermColumns (testContext, getTerms)
                |> Async.AwaitPromise

            let result =
                Expect.wantOk (Result.Ok resultRes) "develop rectifyTermColumnsTest successful failed"

            Expect.equal
                result.Head.MessageTxt
                "The annotation table annotationTable 1 is valid"
                $"Error: {result |> List.map (fun item -> item.MessageTxt)}"
        }

        testCaseAsync "develop addCompositeColumnTest successful"
        <| async {
            let testContext: RequestContext =
                importDefault "../../Fixtures/OfficeMockObjects/AnnotationTableFixtureSuccessful.js"

            let! resultRes =
                OfficeInterop.Core.Main.addCompositeColumn (compositeColumn, context0 = testContext)
                |> Async.AwaitPromise

            let result =
                Expect.wantOk (Result.Ok resultRes) "develop addCompositeColumnTest successful failed"

            Expect.equal result.IsEmpty true $"Error: {result |> List.map (fun item -> item.MessageTxt)}"
        }

        testCaseAsync "develop ArcInvestigation updateTest successful"
        <| async {
            let testContext: RequestContext =
                importDefault "../../Fixtures/OfficeMockObjects/AnnotationTableFixtureSuccessful.js"

            let! resultRes =
                OfficeInterop.Core.Main.updateArcFile (arcInvestigation, testContext)
                |> Async.AwaitPromise

            let result =
                Expect.wantOk (Result.Ok resultRes) "develop ArcInvestigation updateTest successful failed"

            Expect.equal
                result.Head.MessageTxt
                "Replaced existing ARCitISA information! Added 0 tables!"
                $"Error: {result |> List.map (fun item -> item.MessageTxt)}"
        }

        testCaseAsync "develop ArcAssay updateTest successful"
        <| async {
            let testContext: RequestContext =
                importDefault "../../Fixtures/OfficeMockObjects/AnnotationTableFixtureSuccessful.js"

            let! resultRes =
                OfficeInterop.Core.Main.updateArcFile (arcAssay, testContext)
                |> Async.AwaitPromise

            let result =
                Expect.wantOk (Result.Ok resultRes) "develop ArcAssay updateTest successful failed"

            Expect.equal
                result.Head.MessageTxt
                "Replaced existing ARCitISA information! Added 1 tables!"
                $"Error: {result |> List.map (fun item -> item.MessageTxt)}"
        }

        testCaseAsync "develop ArcStudy updateTest successful"
        <| async {
            let testContext: RequestContext =
                importDefault "../../Fixtures/OfficeMockObjects/AnnotationTableFixtureSuccessful.js"

            let! resultRes =
                OfficeInterop.Core.Main.updateArcFile (arcStudy, testContext)
                |> Async.AwaitPromise

            let result =
                Expect.wantOk (Result.Ok resultRes) "develop ArcStudy updateTest successful failed"

            Expect.equal
                result.Head.MessageTxt
                "Replaced existing ARCitISA information! Added 1 tables!"
                $"Error: {result |> List.map (fun item -> item.MessageTxt)}"
        }

        testCaseAsync "develop Template updateTest successful"
        <| async {
            let testContext: RequestContext =
                importDefault "../../Fixtures/OfficeMockObjects/AnnotationTableFixtureSuccessful.js"

            let! resultRes =
                OfficeInterop.Core.Main.updateArcFile (arcTemplate, testContext)
                |> Async.AwaitPromise

            let result =
                Expect.wantOk (Result.Ok resultRes) "develop Template updateTest successful failed"

            Expect.equal
                result.Head.MessageTxt
                "Replaced existing ARCitISA information! Added 1 tables!"
                $"Error: {result |> List.map (fun item -> item.MessageTxt)}"
        }

        testCaseAsync "develop parseExcelInvestigationMetaDataToArcFileTest successful"
        <| async {
            let testContext: RequestContext =
                importDefault "../../Fixtures/OfficeMockObjects/AnnotationTableFixtureSuccessful.js"

            let! resultRes =
                OfficeInterop.Core.Main.tryParseToArcFile (context0 = testContext)
                |> Async.AwaitPromise

            let result =
                Expect.wantOk
                    (Result.Ok resultRes)
                    "develop parseExcelInvestigationMetaDataToArcFileTest successful failed"

            Expect.equal (Result.toOption result).IsNone true $"Error: {result}"
        }
    ]

let Main = testList "OfficeAddin" [ TestsBasic; TestsSuccessful ]
