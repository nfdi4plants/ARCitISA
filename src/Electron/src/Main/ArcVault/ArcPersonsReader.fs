module Main.ArcPersonsReader

open ARCtrl
open ARCtrl.Json

/// Read current metadata, including edits that have not been saved to disk.
let serialize (arc: ARC) : string[] =
    [|
        yield! arc.GetAllPersons()

        // ARCtrl's GetAllPersons currently covers investigation, studies and assays.
        // not worth opening an issue, as we soon shift to a new model
        for run in arc.Runs do
            yield! run.Performers

        for workflow in arc.Workflows do
            yield! workflow.Contacts
    |]
    |> Array.distinct
    |> Array.map (Person.toJsonString ())
