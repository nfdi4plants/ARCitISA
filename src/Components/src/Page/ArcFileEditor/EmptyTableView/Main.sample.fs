namespace Swate.Components.Page.ArcFileEditor.EmptyTableViewSample

open ARCtrl
open Fable.Core
open Feliz
open Swate.Components
open Swate.Components.Shared
open Swate.Components.Composite.AnnotationTable
open Swate.Components.Composite.Template.TemplateCacheContext
open Swate.Components.Composite.TermSearch.Context
open Swate.Components.Composite.TermSearch.Types
open Swate.Components.Shared.ProvenanceGrouping.ARCtrlConverter
open Swate.Components.Page.ArcFileEditor.EmptyTableView.Types

module internal Fixtures =

    let arcFile () =
        let assay = ArcAssay.init "Inline setup"
        assay.AddTable(ArcTable.init "Empty one")
        assay.AddTable(ArcTable.init "Empty two")
        ArcFiles.Assay assay

    let templates () =
        let make (id: string) name organisation termName =
            let template = Template.init name
            template.Id <- System.Guid.Parse id
            template.Organisation <- organisation
            template.Version <- "1.0.0"
            template.Table.AddColumn(CompositeHeader.Input IOType.Source)
            template.Table.AddColumn(CompositeHeader.Parameter(OntologyAnnotation(termName)))
            template.Table.AddColumn(CompositeHeader.Output IOType.Sample)
            template.Table.AddRowsEmpty 1
            template

        [|
            make "00000000-0000-0000-0000-000000000001" "Leaf template" Organisation.DataPLANT "Leaf temperature"
            make "00000000-0000-0000-0000-000000000002" "Root template" Organisation.DataPLANT "Root temperature"
            make
                "00000000-0000-0000-0000-000000000003"
                "Instrument template"
                (Organisation.Other "Other Lab")
                "Instrument model"
        |]

    /// Stands in for the tables an app loads from the rest of the ARC.
    let arcNodes () = promise {
        do! Promise.sleep 300
        let harvest = ArcTable.init "Harvest"
        harvest.AddColumn(CompositeHeader.Input IOType.Source, ResizeArray [ CompositeCell.createFreeText "Plant 1" ])
        harvest.AddColumn(CompositeHeader.Output IOType.Sample, ResizeArray [ CompositeCell.createFreeText "Leaf 1" ])

        return [|
            {
                Location = {
                    Scope = ArcTableScope.Study
                    ParentIdentifier = "Field trial"
                    TableName = harvest.Name
                }
                Table = harvest
            }
        |]
    }

    let termSearch (query: string) = promise {
        return
            ResizeArray [
                if "Temperature".ToLowerInvariant().Contains(query.ToLowerInvariant()) then
                    Term(name = "Temperature", id = "PATO:0000146", source = "PATO")
            ]
    }

[<Erase; Mangle(false)>]
type internal Main =

    [<ReactComponent>]
    static member Providers(children: ReactElement) =
        let templates = React.useMemo (Fixtures.templates, [||])

        let cache = {
            Templates = templates
            IsLoading = false
            RefreshTemplates = ignore
        }

        let search = {
            TermSearchConfigContext.init () with
                hasProvider = true
                disableDefault = true
                termSearchQueries = ResizeArray [ "inline-story", Fixtures.termSearch ]
        }

        TermSearchConfigCtx.Provider(
            search,
            TemplateCacheCtx.Provider(cache, AnnotationTableContextProvider.AnnotationTableContextProvider(children))
        )

    [<ReactComponent(true)>]
    static member Entry(?withArcNodes: bool) =
        let arcFile, setArcFile = React.useState (Fixtures.arcFile)

        let fetchArcNodes =
            if defaultArg withArcNodes false then
                Some Fixtures.arcNodes
            else
                None

        Main.Providers(
            Html.div [
                prop.className "swt:flex swt:h-screen swt:w-full swt:min-w-0 swt:flex-col swt:overflow-hidden"
                prop.children [
                    Swate.Components.Page.ArcFileEditor.Main.ArcFileEditor(
                        arcFile,
                        setArcFile,
                        (fun () -> promise { return [||] }),
                        startingActiveView = Swate.Components.Page.ArcFileEditor.Types.ActiveView.Table 0,
                        ?fetchArcNodes = fetchArcNodes
                    )
                ]
            ]
        )
