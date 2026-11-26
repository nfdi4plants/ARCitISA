namespace Arcitisa.Components.Composite.Workspace

open Fable.Core
open Feliz
open Arcitisa.Components.Composite.Workspace.Types
open Arcitisa.Components.Composite.Workspace.Context

[<Erase; Mangle(false)>]
type ContentArea =

    [<ReactComponent>]
    static member ContentArea(paneId: PaneId) =
        let paneCtx = usePaneCtx ()
        let paneStateCtx = useWorkspacePaneStateCtx ()

        let focusedTab = paneCtx.focusedTab
        let tabs = paneCtx.tabs

        Html.div [
            prop.className "swt:relative swt:min-h-0 swt:flex-1 swt:overflow-hidden"
            prop.children [
                for tab in tabs do
                    Html.div [
                        prop.key tab.Id.Value
                        prop.className "swt:h-full swt:w-full"
                        prop.style [
                            if Some tab.Id <> focusedTab then
                                style.display.none
                        ]
                        prop.children (paneStateCtx.renderTabContent tab)
                    ]
                match focusedTab with
                | None ->
                    Html.div [
                        prop.className
                            "swt:flex swt:size-full swt:items-center swt:justify-center swt:text-base-content/40 swt:text-sm"
                        prop.text "No open editors"
                    ]
                | _ -> ()
            ]
        ]
