namespace Arcitisa.Components.Composite.Notes.Editor

open System
open Feliz
open Arcitisa.Components.Shared

[<RequireQualifiedAccess>]
module TargetSelector =

    [<ReactComponent>]
    let Main
        (
            selectedTarget: ExistingTargetRef option,
            setSelectedTarget: ExistingTargetRef option -> unit,
            availableTargets: ResizeArray<ExistingTargetRef>,
            isSubmitting: bool
        ) =
        let studyTargets =
            availableTargets
            |> Seq.filter (fun target -> target.Kind = NotesTargetKind.Study)
            |> Seq.sortBy _.Name
            |> Seq.toArray

        let assayTargets =
            availableTargets
            |> Seq.filter (fun target -> target.Kind = NotesTargetKind.Assay)
            |> Seq.sortBy _.Name
            |> Seq.toArray

        let hasTargets = studyTargets.Length > 0 || assayTargets.Length > 0

        let kindToken (kind: NotesTargetKind) =
            match kind with
            | NotesTargetKind.Study -> "study"
            | NotesTargetKind.Assay -> "assay"

        let optionValue (target: ExistingTargetRef) =
            $"{kindToken target.Kind}::{target.Name}"

        let selectedValue =
            selectedTarget |> Option.map optionValue |> Option.defaultValue ""

        Html.div [
            prop.className "swt:space-y-2"
            prop.children [
                Html.select [
                    prop.testId "notes-existing-target-select"
                    prop.className "swt:select swt:select-bordered swt:w-full"
                    prop.disabled (isSubmitting || not hasTargets)
                    prop.valueOrDefault selectedValue
                    prop.onChange (fun (value: string) ->
                        if String.IsNullOrWhiteSpace value then
                            setSelectedTarget None
                        else
                            let selectedParts = value.Split([| "::" |], 2, StringSplitOptions.None)

                            if selectedParts.Length <> 2 then
                                setSelectedTarget None
                            else
                                let selectedKind =
                                    match selectedParts.[0] with
                                    | "study" -> Some NotesTargetKind.Study
                                    | "assay" -> Some NotesTargetKind.Assay
                                    | _ -> None

                                match selectedKind with
                                | None -> setSelectedTarget None
                                | Some kind ->
                                    availableTargets
                                    |> Seq.toArray
                                    |> Array.tryFind (fun target ->
                                        target.Kind = kind && target.Name = selectedParts.[1]
                                    )
                                    |> setSelectedTarget
                    )
                    prop.children [
                        if not hasTargets then
                            Html.option [ prop.value ""; prop.text "No targets available" ]
                        if studyTargets.Length > 0 then
                            Html.optgroup [
                                prop.label "Study"
                                prop.children [
                                    for target in studyTargets do
                                        Html.option [
                                            prop.key $"{target.Kind}-{target.Name}"
                                            prop.value (optionValue target)
                                            prop.text target.Name
                                        ]
                                ]
                            ]
                        if assayTargets.Length > 0 then
                            Html.optgroup [
                                prop.label "Assay"
                                prop.children [
                                    for target in assayTargets do
                                        Html.option [
                                            prop.key $"{target.Kind}-{target.Name}"
                                            prop.value (optionValue target)
                                            prop.text target.Name
                                        ]
                                ]
                            ]
                    ]
                ]
            ]
        ]
