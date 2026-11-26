module internal Arcitisa.Components.Page.ValidationPackageBrowser.Sample

open Fable.Core
open Feliz
open Types
open ARCtrl.ValidationPackages

module private Fixtures =

    let mkTag name = {
        Name = Some name
        TermSourceREF = None
        TermAccessionNumber = None
    }

    let mkAuthor fullName = {
        FullName = Some fullName
        Email = None
        Affiliation = None
        AffiliationLink = None
    }

    let tags = [|
        mkTag "DataPLANT"
        mkTag "Metadata"
        mkTag "Invenio"
        mkTag "Conversion"
        mkTag "CQC"
    |]

    let authors = [|
        mkAuthor "Kevin Frey"
        mkAuthor "Freya Muster"
        mkAuthor "Lukas Weil"
    |]

    let random = System.Random(42)

    let randomSubset (items: 'T array) =
        let count = random.Next(items.Length + 1)
        items |> Array.sortBy (fun _ -> random.Next()) |> Array.take count

    let mkPackage (index: int) =
        let name = sprintf "Package%02d" index
        let tag = randomSubset tags

        let author = randomSubset authors

        ValidationPackageDTO.Create(
            name,
            $"Summary for {name}",
            $"Description for {name}.\nIt does things.",
            index % 3,
            index,
            index % 7,
            (if index % 5 = 0 then "alpha.1" else ""),
            "",
            System.DateTime(2026, 8, 19).AddDays(float index),
            tag,
            $"Release notes for {name}",
            "",
            author,
            "python"
        )

    let packages = [|
        for i in 0..24 do
            yield mkPackage i

        yield
            ValidationPackageDTO.Create(
                "Invenio",
                "Invenio is a validation package for the Invenio project",
                "Invenio is a validation package for the Invenio project.\nIt does it very good, it does it very well.\nIt does it very fast, it does it very swell.",
                1,
                0,
                0,
                "",
                "",
                System.DateTime(2026, 8, 19),
                [| mkTag "Invenio" |],
                "",
                "",
                [| mkAuthor "Kevin Frey" |],
                "python"
            )

        yield
            ValidationPackageDTO.Create(
                "MySummaryPackage",
                "A package with a distinctive summary containing the word Quokka",
                "Description does not contain that word.",
                2,
                0,
                0,
                "",
                "",
                System.DateTime(2026, 8, 19),
                [| mkTag "Metadata" |],
                "",
                "",
                [| mkAuthor "Lukas Weil" |],
                "python"
            )
    |]

[<ReactComponent(true)>]
let Main () =

    let currentConfig, setCurrentConfig =
        React.useState (fun () ->
            ValidationPackagesConfig.make
                (ResizeArray [
                    ValidationPackage("Invenio", ?version = Some "0.9.0")
                    ValidationPackage("LegacyPackage", ?version = Some "1.0.0")
                ])
                None
        )

    let fetch () : JS.Promise<ValidationPackageDTO[]> = promise {
        do! Promise.sleep 500
        return Fixtures.packages
    }

    let write (config: ValidationPackagesConfig) =
        Promise.lift (
            try
                setCurrentConfig config
                Ok()
            with ex ->
                Error ex
        )

    React.Fragment [
        Html.div [
            prop.className "swt:border-b-2 swt:p-2 swt:mb-2"
            prop.children [
                Html.div [
                    prop.className "swt:p-2 swt:border swt:rounded"
                    prop.children [
                        Html.h1 "Current Config"
                        Html.textarea [
                            prop.testId "validation-package-selector-config"
                            prop.className "swt:w-full swt:h-32"
                            prop.value (currentConfig.ToString())
                            prop.readOnly true
                        ]
                    ]
                ]
            ]
        ]
        ValidationPackageBrowser.ValidationPackageBrowser(
            config = currentConfig,
            writeConfig = write,
            fetchValidationPackages = fetch
        )
    ]
