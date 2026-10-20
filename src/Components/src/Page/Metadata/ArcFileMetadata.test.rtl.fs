module internal Swate.Components.Page.Metadata.ImportRtlTests

open ARCtrl
open Fable.Core
open Vitest
open Swate.Components.Shared

Vitest.afterEach (fun () -> RTL.cleanup ())

for name, create in
    [
        "investigation", (fun () -> ArcFiles.Investigation(ArcInvestigation("investigation")))
        "study", (fun () -> ArcFiles.Study(ArcStudy("study"), []))
        "assay", (fun () -> ArcFiles.Assay(ArcAssay("assay")))
        "run", (fun () -> ArcFiles.Run(ArcRun("run")))
        "workflow", (fun () -> ArcFiles.Workflow(ArcWorkflow("workflow")))
        "template", (fun () -> ArcFiles.Template(Template.init "template"))
    ] do
    Vitest.test (
        $"lazy {name} metadata forwards the import callback and applies selected persons",
        TestOptions(timeout = 20_000),
        (fun () -> promise {
            let mutable requests = 0
            let mutable saved: ArcFiles option = None

            RTL.render (
                ArcFileMetadata.ArcFileMetadata(
                    create (),
                    (fun next -> saved <- Some next),
                    onImportPersons =
                        (fun () -> promise {
                            requests <- requests + 1
                            return [| Person(firstName = "Imported") |]
                        })
                )
            )
            |> ignore

            do!
                RTL.waitFor (
                    (fun () -> RTL.screen.getByTestId "import-persons-button" |> ignore),
                    RTLWaitForOptions(timeout = 20_000)
                )

            let! importButton =
                RTL.waitFor (
                    (fun () -> RTL.screen.getByTestId "import-persons-button"),
                    RTLWaitForOptions(timeout = 4000)
                )

            Vitest.expect(requests).toBe 0
            RTL.fireEvent.click importButton

            let! candidate =
                RTL.waitFor (
                    (fun () -> RTL.screen.getByTestId "import-person-Imported-checkbox"),
                    RTLWaitForOptions(timeout = 4000)
                )

            Vitest.expect(requests).toBe 1
            Vitest.expect(saved.IsNone).toBe true
            RTL.fireEvent.click candidate
            RTL.fireEvent.click (RTL.screen.getByTestId "import-selected-persons-button")

            let persons =
                match saved.Value with
                | ArcFiles.Investigation value -> value.Contacts
                | ArcFiles.Study(value, _) -> value.Contacts
                | ArcFiles.Assay value -> value.Performers
                | ArcFiles.Run value -> value.Performers
                | ArcFiles.Workflow value -> value.Contacts
                | ArcFiles.Template value -> value.Authors
                | _ -> failwith "Expected metadata with persons."

            Vitest.expect(persons.Count).toBe 1
            Vitest.expect(persons.[0].FirstName).toEqual (Some "Imported")
        })
    )
