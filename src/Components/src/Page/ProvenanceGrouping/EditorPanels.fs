namespace Arcitisa.Components.Page.ProvenanceGrouping

open System
open System.Globalization
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Arcitisa.Components.Composite.FolderedDraggableList
open Arcitisa.Components.Composite.FolderedDraggableList.Types
open Arcitisa.Components.JsBindings
open Arcitisa.Components.Shared.ProvenanceGrouping.Types
open Arcitisa.Components.Shared.ProvenanceGrouping.Grouping
open Arcitisa.Components.Shared.ProvenanceGrouping.Edit
open Arcitisa.Components.Shared.ProvenanceGrouping.Session
open Arcitisa.Components.Page.ProvenanceGrouping.Types

/// Alert and detail panels rendered around the main grouping surface.
module EditorPanels =

    let errorAlert (error: string) =
        Html.div [
            prop.className "swt:alert swt:alert-error"
            prop.text error
        ]

    let assignmentBatchWarning debug (pending: PendingAssignmentBatch) onConfirm onCancel =
        let overwriteCount = pending.AffectedValueCount
        let sideCount = pending.AffectedSideCount
        let isFanOutApply = pending.Batch.Overwrites.IsEmpty

        let headers =
            [
                yield! pending.Batch.Overwrites |> List.map (fun w -> w.Header.Category.Name)
                yield! pending.Batch.Adds |> List.map (fun a -> a.Header.Category.Name)
            ]
            |> List.distinct

        let headerText = headers |> List.tryHead |> Option.defaultValue "annotation"

        let valueText =
            pending.Batch.Overwrites
            |> List.tryHead
            |> Option.map (fun w -> Formatting.formatValue w.Value w.Unit)
            |> Option.orElse (
                pending.Batch.Adds
                |> List.tryHead
                |> Option.map (fun a -> Formatting.formatValue a.Value a.Unit)
            )
            |> Option.defaultValue "new value"

        let heading =
            if isFanOutApply then
                $"Apply {headerText} value to {pending.AffectedGroupCount} selected groups?"
            else
                match headers with
                | _ :: _ :: _ -> $"Overwrite {overwriteCount} values across {headers.Length} annotations?"
                | _ when overwriteCount > 1 -> $"Overwrite {overwriteCount} {headerText} values?"
                | _ -> $"Overwrite {headerText} value?"

        let body =
            if isFanOutApply then
                $"Adds {valueText} to {pending.AffectedEntityCount} entities across the selected groups."
            else
                match headers with
                | _ :: _ :: _ ->
                    let headerList = headers |> String.concat ", "
                    $"The selected targets already have values for {headerList}. Confirm to replace them across {sideCount} side(s)."
                | _ ->
                    $"The selected targets already have a {headerText} value. Confirm to replace it with {valueText} across {sideCount} side(s)."

        Html.div [
            prop.className [
                "swt:alert swt:flex-wrap swt:items-start"
                if isFanOutApply then
                    "swt:alert-info"
                else
                    "swt:alert-warning"
            ]
            if debug then
                if isFanOutApply then
                    prop.testId "provenance-apply-batch-prompt"
                else
                    prop.testId "provenance-overwrite-warning"
            prop.children [
                Html.i [
                    prop.className [
                        "swt:iconify swt:size-5"
                        if isFanOutApply then
                            "swt:fluent--info-20-regular"
                        else
                            "swt:fluent--warning-20-regular"
                    ]
                ]
                Html.div [
                    prop.className "swt:flex swt:flex-col swt:gap-1"
                    prop.children [
                        Html.strong [ prop.text heading ]
                        Html.span [ prop.className "swt:text-sm"; prop.text body ]
                    ]
                ]
                Html.div [
                    prop.className "swt:ml-auto swt:flex swt:gap-2"
                    prop.children [
                        Html.button [
                            prop.type'.button
                            prop.className [
                                "swt:btn swt:btn-sm"
                                if isFanOutApply then
                                    "swt:btn-primary"
                                else
                                    "swt:btn-warning"
                            ]
                            if debug then
                                if isFanOutApply then
                                    prop.testId "provenance-confirm-apply"
                                else
                                    prop.testId "provenance-confirm-overwrite"
                            prop.onPointerUp (fun _ -> onConfirm pending)
                            prop.onClick (fun _ -> onConfirm pending)
                            prop.text (if isFanOutApply then "Apply" else "Overwrite")
                        ]
                        Html.button [
                            prop.type'.button
                            prop.className "swt:btn swt:btn-ghost swt:btn-sm"
                            prop.onClick (fun _ -> onCancel ())
                            prop.text "Cancel"
                        ]
                    ]
                ]
            ]
        ]

    let hintPanel debug (hint: string) onDismiss =
        Html.div [
            prop.className "swt:alert swt:alert-info"
            if debug then
                prop.testId "provenance-hint"
            prop.children [
                Html.i [
                    prop.className "swt:iconify swt:fluent--lightbulb-20-regular swt:size-5"
                ]
                Html.span [ prop.className "swt:text-sm"; prop.text hint ]
                Html.button [
                    prop.type'.button
                    prop.className "swt:btn swt:btn-ghost swt:btn-xs swt:ml-auto"
                    prop.ariaLabel "Dismiss hint"
                    if debug then
                        prop.testId "provenance-hint-dismiss"
                    prop.onClick (fun _ -> onDismiss ())
                    prop.text "Dismiss"
                ]
            ]
        ]

    let memberResolutionPrompt debug (pending: PendingMemberResolution) onPairByOrder onAllToAll onManual onCancel =
        let memberText count side =
            if count = 1 then
                $"{count} {side} member"
            else
                $"{count} {side} members"

        let inputMemberText = memberText pending.InputMemberCount "input"
        let outputMemberText = memberText pending.OutputMemberCount "output"

        let canPairByOrder =
            pending.InputMemberCount = pending.OutputMemberCount
            && pending.InputMemberCount > 0

        Html.div [
            prop.className "swt:alert swt:alert-warning swt:flex-wrap swt:items-start"
            if debug then
                prop.testId "provenance-member-resolution-prompt"
            prop.children [
                Html.i [
                    prop.className "swt:iconify swt:fluent--text-paragraph-24-regular swt:size-5"
                ]
                Html.div [
                    prop.className "swt:flex swt:flex-col swt:gap-1"
                    prop.children [
                        Html.strong "Choose how to connect the members"
                        Html.span [
                            prop.className "swt:text-sm"
                            prop.text $"This connection has {inputMemberText} and {outputMemberText}."
                        ]
                    ]
                ]
                Html.div [
                    prop.className "swt:ml-auto swt:flex swt:flex-wrap swt:gap-2"
                    prop.children [
                        if canPairByOrder then
                            Html.button [
                                prop.type'.button
                                prop.className "swt:btn swt:btn-primary swt:btn-sm"
                                prop.ariaLabel "Pair members by order"
                                prop.title
                                    "Connect members pairwise in name order (first with first, second with second, …)"
                                if debug then
                                    prop.testId "provenance-member-resolution-pair-by-order"
                                prop.onClick (fun _ -> onPairByOrder pending)
                                prop.text "Pair by order"
                            ]
                        Html.button [
                            prop.type'.button
                            prop.className "swt:btn swt:btn-warning swt:btn-sm"
                            prop.ariaLabel "Create all-to-all connections"
                            prop.title "Connect every input member with every output member"
                            if debug then
                                prop.testId "provenance-member-resolution-all-to-all"
                            prop.onClick (fun _ -> onAllToAll pending)
                            prop.text "All-to-all"
                        ]
                        Html.button [
                            prop.type'.button
                            prop.className "swt:btn swt:btn-outline swt:btn-sm"
                            prop.ariaLabel "Resolve manually"
                            if debug then
                                prop.testId "provenance-member-resolution-manual"
                            prop.onPointerUp (fun _ -> onManual pending)
                            prop.onClick (fun _ -> onManual pending)
                            prop.text "Resolve manually"
                        ]
                        Html.button [
                            prop.type'.button
                            prop.className "swt:btn swt:btn-ghost swt:btn-sm"
                            prop.ariaLabel "Cancel member resolution"
                            if debug then
                                prop.testId "provenance-member-resolution-cancel"
                            prop.onClick (fun _ -> onCancel ())
                            prop.text "Cancel"
                        ]
                    ]
                ]
            ]
        ]

    let private groupTitle (groups: DisplayGroup list) groupId =
        groups
        |> List.tryFind (fun group -> group.Id = groupId)
        |> Option.map GroupCardData.title
        |> Option.defaultValue groupId

    let connectionDetails
        debug
        (model: ProvenanceModel)
        (inputGroups: DisplayGroup list)
        (outputGroups: DisplayGroup list)
        (connections: DisplayConnection list)
        detail
        (onRemove: DisplayConnection -> unit)
        =
        match detail with
        | Some(ProvenanceDetail.Connection connectionId) ->
            let resolved = connections |> List.tryFind (fun c -> c.Id = connectionId)

            match resolved with
            | Some conn ->
                let underlying =
                    conn.ConnectionIds |> List.choose (fun id -> model.Connections.TryFind id)

                let inputCount =
                    underlying
                    |> List.map (fun connection -> connection.InputSetId)
                    |> List.distinct
                    |> List.length

                let outputCount =
                    underlying
                    |> List.map (fun connection -> connection.OutputSetId)
                    |> List.distinct
                    |> List.length

                let setName (sets: Map<ProvenanceSetId, ProvenanceSet>) setId =
                    sets.TryFind setId
                    |> Option.map (fun set -> set.Name)
                    |> Option.defaultValue setId

                let shapeText =
                    match underlying.Length with
                    | 1 -> "1 connection"
                    | count -> $"{count} connections: {inputCount} inputs × {outputCount} outputs"

                Html.div [
                    prop.className
                        "swt:mx-4 swt:mt-4 swt:flex swt:flex-col swt:gap-2 swt:rounded-box swt:border swt:border-base-300 swt:bg-base-100 swt:p-3 swt:motion-pop-in"
                    prop.custom ("data-connection-id", conn.Id)
                    if debug then
                        prop.testId "provenance-connection-details"
                    prop.children [
                        Html.div [
                            prop.className "swt:flex swt:flex-wrap swt:items-center swt:gap-2"
                            prop.children [
                                Html.h3 [
                                    prop.className "swt:grow swt:font-semibold swt:text-primary"
                                    prop.text
                                        $"{groupTitle inputGroups conn.SourceGroupId} → {groupTitle outputGroups conn.TargetGroupId}"
                                ]
                                Html.button [
                                    prop.type'.button
                                    prop.className "swt:btn swt:btn-outline swt:btn-error swt:btn-sm"
                                    prop.ariaLabel "Remove connection"
                                    if debug then
                                        prop.testId "provenance-connection-remove"
                                    prop.onClick (fun _ -> onRemove conn)
                                    prop.children [
                                        Html.i [
                                            prop.className "swt:iconify swt:fluent--delete-20-regular swt:size-4"
                                        ]
                                        Html.span "Remove connection"
                                    ]
                                ]
                            ]
                        ]
                        Html.p [ prop.className "swt:text-sm"; prop.text shapeText ]
                        Html.ul [
                            prop.className "swt:flex swt:flex-col swt:gap-0.5 swt:text-sm"
                            if debug then
                                prop.testId "provenance-connection-pairs"
                            prop.children [
                                for connection in underlying do
                                    Html.li [
                                        prop.children [
                                            Html.span (setName model.InputSets connection.InputSetId)
                                            Html.span [
                                                prop.className "swt:px-1 swt:text-base-content/60"
                                                prop.text "→"
                                            ]
                                            Html.span (setName model.OutputSets connection.OutputSetId)
                                            match connection.ProcessName with
                                            | Some processName ->
                                                Html.span [
                                                    prop.className "swt:pl-2 swt:text-xs swt:text-base-content/60"
                                                    prop.text processName
                                                ]
                                            | None -> Html.none
                                        ]
                                    ]
                            ]
                        ]
                    ]
                ]
            | None -> Html.none
        | _ -> Html.none
