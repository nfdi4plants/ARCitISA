module Client.Tests

open Fable.Mocha

let client =
    testList "Client" [
        ExplorePanelElmish.Main
        Components.Tests.Table.KeyboardNavigation.Main
        Spreadsheet.Tests.DataAnnotator.Main
        OfficeAddIn.AnnotationTable.Successful.Main
    ]

let all =
    testList "All" [
#if FABLE_COMPILER // This preprocessor directive makes editor happy
        Swate.Components.Shared.Tests.shared
#endif
        client
    ]

[<EntryPoint>]
let main _ = Mocha.runTests all
