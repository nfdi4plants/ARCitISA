namespace Swate.Components.Composite.Template

open Fable.Core
open Feliz
open ARCtrl
open Swate.Components
open Swate.Components.Composite.Template
open Swate.Components.Composite.Template.Types

[<Erase; Mangle(false)>]
type TemplateBrowser =

    [<ReactComponent>]
    static member private DisabledMessage(message: string) =
        Html.span [
            prop.className "swt:text-xs swt:opacity-70"
            prop.text message
        ]

    [<ReactComponent>]
    static member private TemplateImportButton(isDisabled: bool, submit: unit -> unit) =
        Html.button [
            prop.className [
                "swt:btn swt:w-full swt:join-item"
                "swt:btn-primary"
                if isDisabled then
                    "swt:btn-disabled"
            ]
            prop.disabled isDisabled
            prop.onClick (fun _ -> submit ())
            prop.text "Import"
        ]

    [<ReactComponent>]
    static member private SelectedTemplatesLabel(selectedCount: int) =
        if selectedCount > 0 then
            Html.span [
                prop.className "swt:text-xs swt:opacity-70"
                prop.textf "%i selected" selectedCount
            ]
        else
            Html.none

    [<ReactComponent(true)>]
    static member TemplateBrowser
        (
            templates: Template[],
            isLoading: bool,
            selectedTemplateIds: Set<System.Guid>,
            refreshTemplates: unit -> unit,
            openImportDialog: unit -> unit,
            toggleTemplateSelection: System.Guid -> unit,
            ?disabledMessage: string
        ) =

        let templateImportDisabled =
            selectedTemplateIds.Count <= 0 || disabledMessage.IsSome

        TemplateFilter.TemplateFilterProvider(
            React.Fragment [
                TemplateBrowser.TemplateImportButton(templateImportDisabled, openImportDialog)
                TemplateBrowser.SelectedTemplatesLabel(selectedTemplateIds.Count)

                TemplateFilter.TemplateFilter(
                    templates,
                    templateSearchClassName = "swt:grow swt:min-w-0 swt:max-w-full"
                )
                TemplateFilter.FilteredTemplateRenderer(fun filteredTemplates ->
                    Html.div [
                        prop.className "swt:flex-1 swt:min-h-0 swt:overflow-y-auto"
                        prop.style [ style.scrollbarGutter.stable ] // no tailwind class for this, and prevents layout shift when scrollbar appears
                        prop.children [
                            match disabledMessage with
                            | Some message -> TemplateBrowser.DisabledMessage message
                            | None ->
                                TemplatesDisplay.TemplatesDisplay(
                                    templates = filteredTemplates,
                                    selectedTemplateIds = selectedTemplateIds,
                                    toggleTemplateSelection = toggleTemplateSelection,
                                    isLoading = isLoading,
                                    refreshTemplates = refreshTemplates
                                )
                        ]
                    ]
                )
            ]
        )
