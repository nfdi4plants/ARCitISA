module internal Swate.Components.Tests.TableFiles

open ARCtrl
open Swate.Components.Shared

/// The file types that can hold annotation tables, each with a function that creates an empty file.
let tableFileTypes: (string * (unit -> ArcFiles)) list = [
    "assay", (fun () -> ArcFiles.Assay(ArcAssay.init "Assay"))
    "study", (fun () -> ArcFiles.Study(ArcStudy.init "Study", []))
    "run", (fun () -> ArcFiles.Run(ArcRun.init "Run"))
]

let withTables (createEmptyFile: unit -> ArcFiles) (tables: ArcTable list) =
    let file = createEmptyFile ()
    file.ArcTables().AddTables tables
    file
