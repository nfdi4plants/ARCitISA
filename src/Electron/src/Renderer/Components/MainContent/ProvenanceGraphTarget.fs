module Renderer.Components.MainContent.ProvenanceGraphTarget

open Feliz
open Arcitisa.Components.Primitive
open Arcitisa.Components.Shared.ProvenanceGraph.Types
open Arcitisa.Components.Shared.ProvenanceGraph.BuildIndex

/// What the page shows. The index is built from the IPC data once, when the data arrives, because the IPC
/// copy has no lookups.
[<RequireQualifiedAccess>]
type private LoadState =
    | Loading
    | Failed of string
    | Loaded of ProvenanceIndex

/// Loads the provenance data once per page open and shows the graph. A load error shows its message in the page.
/// The page catches the errors of its views itself. The app around the page keeps working.
[<ReactComponent(true)>]
let ProvenanceGraphTarget () =
    let state, setState = React.useState LoadState.Loading

    React.useEffect (
        (fun () ->
            let mutable isDisposed = false

            promise {
                // The call can reject as well as return an error, and neither may leave the page.
                let! result = Api.ipcArcVaultApi.loadProvenanceGraph () |> Promise.catch Error

                if not isDisposed then
                    match result with
                    | Ok data -> setState (LoadState.Loaded(ProvenanceIndex.ofData data))
                    | Error exn -> setState (LoadState.Failed $"Could not load the provenance graph: {exn.Message}")
            }
            |> Promise.catch (fun exn ->
                if not isDisposed then
                    setState (LoadState.Failed $"Could not load the provenance graph: {exn.Message}")
            )
            |> Promise.start

            fun () -> isDisposed <- true
        ),
        [||]
    )

    let content =
        match state with
        | LoadState.Loading ->
            Html.div [
                prop.className "swt:flex swt:size-full swt:items-center swt:justify-center"
                prop.children [
                    Arcitisa.Components.Primitive.LoadingSpinner.LoadingSpinner.LoadingSpinner(
                        size = DaisyuiSize.XL,
                        color = DaisyuiColors.Primary,
                        text = "Loading provenance graph..."
                    )
                ]
            ]
        | LoadState.Failed message -> ProvenanceGraphArea.ErrorAlert message
        | LoadState.Loaded index -> Arcitisa.Components.Page.ProvenanceGraph.ProvenanceGraph.ProvenanceGraph(index)

    Html.div [
        prop.className "swt:size-full swt:min-w-0 swt:min-h-0 swt:overflow-hidden"
        prop.testId "provenance-graph-page"
        prop.children [ content ]
    ]
