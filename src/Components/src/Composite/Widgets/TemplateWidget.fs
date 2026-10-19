namespace Swate.Components.Composite.Widgets

open ARCtrl
open Fable.Core
open Feliz
open Swate.Components
open Swate.Components.Shared
open Swate.Components.Composite.Template
open Swate.Components.Composite.Widgets.Context
open Swate.Components.Composite.Widgets.Types

module TemplateTypes = Swate.Components.Composite.Template.Types
module TemplateActions = Swate.Components.Composite.Template.TemplateActions
module TemplateCacheContext = Swate.Components.Composite.Template.TemplateCacheContext

[<Erase; Mangle(false)>]
type TemplateWidget =

    [<ReactComponent(true)>]
    static member TemplateWidget
        // 👀 If you rename these variables, ensure that the names are forwarded for lazy loading in `src\Components\src\ARCFileEditor\ArcFileEditor.fs` as well!
        (arcFile: ArcFiles, activeTableIndex: int option, setArcFile: ArcFiles -> unit) =

        let templateCacheCtx = TemplateCacheContext.useTemplateCacheCtx ()
        let templates = templateCacheCtx.Templates
        let isLoading = templateCacheCtx.IsLoading

        let importType, setImportType = React.useState TableJoinOptions.Headers

        let selectedTemplateIds, setSelectedTemplateIds =
            React.useStateWithUpdater (Set.empty<System.Guid>)

        let showImportDialog, setShowImportDialog = React.useState false
        let widgetCtx = useWidgetControllerCtx ()

        let tryGetActiveTableIndex (arcFile: ArcFiles) =
            arcFile.TryGetActiveTable(activeTableIndex) |> Option.map fst

        let disabledMessage =
            match tryGetActiveTableIndex arcFile with
            | Some _ -> None
            | None -> Some "Select a table first to import templates."

        let syncSelectedTemplateIds (loadedTemplates: Template[]) =
            TemplateActions.syncSelectedTemplateIds loadedTemplates setSelectedTemplateIds

        let refreshTemplates () = templateCacheCtx.RefreshTemplates()

        React.useEffect ((fun () -> syncSelectedTemplateIds templates), [| box templates |])

        let toggleTemplateSelection (templateId: System.Guid) =
            setSelectedTemplateIds (fun current -> TemplateActions.toggleTemplateSelection templateId current)

        let importError, setImportError = React.useState (None: string option)

        let openImportDialog () =
            if not showImportDialog then
                setImportError None
                setShowImportDialog true

        let confirmImport (payload: TemplateTypes.ImportModalConfirmPayload) =
            match tryGetActiveTableIndex arcFile with
            | Some tableIndex when payload.SelectedTemplatesForImport.Length > 0 ->
                let importResult =
                    try
                        Ok(TemplateActions.importIntoCopy arcFile (Some tableIndex) payload)
                    with error ->
                        Error error.Message

                match importResult with
                | Error message -> setImportError (Some message)
                | Ok nextArcFile ->
                    setArcFile nextArcFile
                    setSelectedTemplateIds (fun _ -> Set.empty<System.Guid>)
                    setShowImportDialog false
                    widgetCtx.closeWidget WidgetType.Template
            | _ -> setShowImportDialog false

        let existingTableNames = arcFile.Tables() |> Seq.map _.Name |> Array.ofSeq

        let selectedTemplates =
            React.useMemo (
                (fun () ->
                    templates
                    |> Array.filter (fun template -> selectedTemplateIds.Contains template.Id)
                ),
                [| box templates; box selectedTemplateIds |]
            )

        React.Fragment [
            TemplateImportModal.TemplateImportModal(
                isOpen = showImportDialog,
                importType = importType,
                selectedTemplates = selectedTemplates,
                existingTableNames = existingTableNames,
                setIsOpen = setShowImportDialog,
                setImportType = setImportType,
                submitImport = confirmImport,
                ?importError = importError
            )
            Html.div [
                prop.className "swt:flex swt:flex-col swt:gap-2 swt:overflow-y-hidden swt:p-2"
                prop.children [
                    TemplateBrowser.TemplateBrowser(
                        templates,
                        isLoading,
                        selectedTemplateIds,
                        refreshTemplates,
                        openImportDialog,
                        toggleTemplateSelection,
                        ?disabledMessage = disabledMessage
                    )
                ]
            ]
        ]

    /// This will be used for tests in Widgets
    [<ReactComponent>]
    static member Entry
        (arcFile: ArcFiles, activeTableIndex: int option, setArcFile: ArcFiles -> unit, services: TemplateWidgetServices) =

        TemplateCacheProvider.TemplateCacheProvider(
            (fun () -> services.loadTemplates () |> Async.StartAsPromise),
            TemplateWidget.TemplateWidget(arcFile, activeTableIndex, setArcFile)
        )
