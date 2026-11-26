module Renderer.Components.MainContent.ProvenanceGroupingTarget

open Fable.Core
open Feliz
open Arcitisa.Components.Shared.ProvenanceGrouping.Edit
open Arcitisa.Components.Shared.ProvenanceGrouping.Session
open Arcitisa.Components.Page.ProvenanceGrouping.Types
open Arcitisa.Electron.Shared.DTOs.ProvenanceGroupingDto

module ProvenanceGroupingTargetHelper =

    let scopeLabel scope =
        match scope with
        | ProvenanceTableScopeDto.Study -> "Study"
        | ProvenanceTableScopeDto.Assay -> "Assay"
        | ProvenanceTableScopeDto.Run -> "Run"

    let buttonClasses isSelected = [
        "swt:btn swt:btn-sm swt:justify-start swt:w-full"
        if isSelected then "swt:btn-primary" else "swt:btn-ghost"
    ]

type private LoadState = {
    Tables: ProvenanceTableSelectionDto[]
    Selected: ProvenanceTableSelectionDto option
    Session: ProvenanceSession option
    Warnings: string list
    Patches: ProvenanceTablePatch list
    IsLoading: bool
    Error: string option
}

module private LoadState =
    let init = {
        Tables = [||]
        Selected = None
        Session = None
        Warnings = []
        Patches = []
        IsLoading = true
        Error = None
    }

[<ReactComponent>]
let private WarningList (warnings: string list) =
    match warnings with
    | [] -> Html.none
    | _ ->
        Html.div [
            prop.className "swt:alert swt:alert-warning swt:items-start"
            prop.children [
                Html.i [
                    prop.className "swt:iconify swt:fluent--warning-20-regular swt:size-5"
                ]
                Html.div [
                    prop.className "swt:flex swt:flex-col swt:gap-1"
                    prop.children [
                        Html.strong "Conversion warnings"
                        Html.ul [
                            prop.className "swt:list-disc swt:pl-4 swt:text-sm"
                            prop.children [
                                for warning in warnings do
                                    Html.li warning
                            ]
                        ]
                    ]
                ]
            ]
        ]

[<ReactComponent(true)>]
let ProvenanceGroupingTarget () =
    let state, setState = React.useStateWithUpdater LoadState.init

    let loadSelection =
        React.useCallback (
            (fun (selection: ProvenanceTableSelectionDto) ->
                setState (fun current -> {
                    current with
                        Selected = Some selection
                        Session = None
                        Warnings = []
                        Patches = []
                        IsLoading = true
                        Error = None
                })

                promise {
                    match! Api.ipcArcVaultApi.loadProvenanceTable selection with
                    | Ok result ->
                        let model = ProvenanceModelDto.toModel result.Model

                        setState (fun current -> {
                            current with
                                Selected = Some result.Selection
                                Session = Some(Session.init model)
                                Warnings = result.Warnings |> Array.toList
                                Patches = []
                                IsLoading = false
                                Error = None
                        })
                    | Error exn ->
                        setState (fun current -> {
                            current with
                                Session = None
                                Warnings = []
                                Patches = []
                                IsLoading = false
                                Error = Some $"Could not load provenance table: {exn.Message}"
                        })
                }
                |> Promise.start
            ),
            [||]
        )

    React.useEffect (
        (fun () ->
            promise {
                setState (fun current -> {
                    current with
                        IsLoading = true
                        Error = None
                })

                match! Api.ipcArcVaultApi.listProvenanceTables () with
                | Ok tables ->
                    setState (fun current -> {
                        current with
                            Tables = tables
                            IsLoading = false
                    })

                    match tables |> Array.tryHead with
                    | Some first -> loadSelection first
                    | None ->
                        setState (fun current -> {
                            current with
                                IsLoading = false
                                Error = Some "No study, assay, or run tables are available in the active ARC."
                        })
                | Error exn ->
                    setState (fun current -> {
                        current with
                            Tables = [||]
                            Session = None
                            IsLoading = false
                            Error = Some $"Could not list provenance tables: {exn.Message}"
                    })
            }
            |> Promise.start
        ),
        [| box loadSelection |]
    )

    let onChange (change: ProvenanceEditorChange) =
        setState (fun current -> {
            current with
                Session = Some change.Session
                Patches = current.Patches @ change.Patches
        })

    Html.div [
        prop.className
            "swt:size-full swt:min-w-0 swt:min-h-0 swt:grid swt:grid-cols-[18rem_minmax(0,1fr)] swt:overflow-hidden"
        prop.testId "provenance-grouping-page"
        prop.children [
            Html.aside [
                prop.className
                    "swt:min-h-0 swt:overflow-y-auto swt:border-r swt:border-base-300 swt:bg-base-100 swt:p-3"
                prop.children [
                    Html.div [
                        prop.className "swt:flex swt:items-center swt:gap-2 swt:pb-3"
                        prop.children [
                            Html.i [
                                prop.className
                                    "swt:iconify swt:fluent--text-paragraph-24-regular swt:size-5 swt:text-primary"
                            ]
                            Html.h2 [
                                prop.className "swt:text-sm swt:font-semibold"
                                prop.text "Provenance"
                            ]
                        ]
                    ]
                    Html.div [
                        prop.className "swt:flex swt:flex-col swt:gap-1"
                        prop.children [
                            for table in state.Tables do
                                let isSelected = state.Selected |> Option.exists ((=) table)

                                Html.button [
                                    prop.type'.button
                                    prop.className (ProvenanceGroupingTargetHelper.buttonClasses isSelected)
                                    prop.onClick (fun _ -> loadSelection table)
                                    prop.children [
                                        Html.span [
                                            prop.className "swt:badge swt:badge-outline swt:badge-sm"
                                            prop.text (ProvenanceGroupingTargetHelper.scopeLabel table.Scope)
                                        ]
                                        Html.span [
                                            prop.className "swt:truncate"
                                            prop.text table.DisplayLabel
                                        ]
                                    ]
                                ]
                        ]
                    ]
                ]
            ]
            Html.main [
                prop.className "swt:min-w-0 swt:min-h-0 swt:overflow-hidden swt:bg-base-200"
                prop.children [
                    Html.div [
                        prop.className "swt:flex swt:h-full swt:flex-col swt:gap-3 swt:p-3"
                        prop.children [
                            match state.Error with
                            | Some error ->
                                Html.div [
                                    prop.className "swt:alert swt:alert-error"
                                    prop.children [
                                        Html.i [
                                            prop.className "swt:iconify swt:fluent--error-circle-24-regular swt:size-5"
                                        ]
                                        Html.span error
                                    ]
                                ]
                            | None -> Html.none

                            WarningList state.Warnings

                            match state.Session, state.IsLoading with
                            | _, true ->
                                Html.div [
                                    prop.className
                                        "swt:flex swt:flex-1 swt:items-center swt:justify-center swt:text-sm swt:text-base-content/70"
                                    prop.text "Loading provenance..."
                                ]
                            | Some session, false ->
                                Html.div [
                                    prop.className "swt:flex-1 swt:min-h-0 swt:min-w-0 swt:overflow-hidden"
                                    prop.children [
                                        Arcitisa.Components.Page.ProvenanceGrouping.ProvenanceGrouping.Main(
                                            session,
                                            onChange
                                        )
                                    ]
                                ]
                            | None, false ->
                                Html.div [
                                    prop.className
                                        "swt:flex swt:flex-1 swt:items-center swt:justify-center swt:text-sm swt:text-base-content/70"
                                    prop.text "Select a provenance table to load the editor."
                                ]
                        ]
                    ]
                ]
            ]
        ]
    ]
