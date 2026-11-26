namespace Arcitisa.Components.Composite.Workspace


open System
open Fable.Core
open Feliz
// Workspace
open Types
open Context

[<Erase; Mangle(false)>]
type WorkspaceFixture =


    [<ReactComponent>]
    static member private Toolbar() =
        let dispatchCtx = useWorkspaceDispatchCtx<string> ()
        let tabCounter = React.useRef 4

        let addTab _ =
            let n = tabCounter.current
            tabCounter.current <- n + 1

            let tab = {
                Id = TabId(sprintf "tab-%d" n)
                Label = sprintf "NewFile%d.tsx" n
                Payload = sprintf "Payload %s" (Guid.NewGuid().ToString("N").Substring(0, 8))
            }

            dispatchCtx.dispatch (AddTab tab)

        Html.div [
            prop.className "swt:flex swt:gap-2 swt:p-2 swt:bg-base-200"
            prop.children [
                Html.button [
                    prop.className "swt:btn swt:btn-sm swt:btn-primary"
                    prop.text "Add Tab"
                    prop.onClick addTab
                ]
                Html.button [
                    prop.className "swt:btn swt:btn-sm swt:btn-ghost"
                    prop.text "Close All"
                    prop.onClick (fun _ -> dispatchCtx.dispatch RemoveAllTabs)
                ]
                Html.span [
                    prop.className "swt:text-xs swt:text-base-content/50 swt:self-center swt:ml-auto"
                    prop.text "Drag tab to pane edge to split"
                ]
            ]
        ]

    [<ReactMemoComponent(AreEqualFn.FsEqualsButFunctions)>]
    static member private RenderContent(tab: Tab<string>) =
        Html.div [
            prop.className "swt:p-4 swt:flex swt:flex-col swt:gap-2"
            prop.children [
                Html.h3 [
                    prop.className "swt:text-lg swt:font-semibold"
                    prop.text tab.Label
                ]
                Html.p [
                    prop.className "swt:text-sm swt:text-base-content/60"
                    prop.text (sprintf "Tab ID: %s" tab.Id.Value)
                ]
                Html.p [
                    prop.className "swt:text-sm swt:text-base-content/40"
                    prop.text tab.Payload
                ]
            ]
        ]

    [<ReactMemoComponent(AreEqualFn.FsEqualsButFunctions)>]
    static member private RenderTab(tab: Tab<string>) = Html.span tab.Label

    [<ReactComponent(true)>]
    static member WorkspaceFixture() : ReactElement =

        let genPayload () =
            let short = Guid.NewGuid().ToString("N").Substring(0, 8)
            $"Payload {short}"

        let initialTabs = [|
            {
                Id = TabId "tab-1"
                Label = "Main.tsx"
                Payload = genPayload ()
            }
            {
                Id = TabId "tab-2"
                Label = "utils.ts"
                Payload = genPayload ()
            }
            {
                Id = TabId "tab-3"
                Label = "styles.css"
                Payload = genPayload ()
            }
        |]

        Workspace.WorkspaceProvider(
            renderTabContent = WorkspaceFixture.RenderContent,
            renderTab = WorkspaceFixture.RenderTab,
            initialTabs = initialTabs,
            debug = true,
            children =
                Html.div [
                    prop.className "swt:flex swt:flex-col swt:size-full swt:overflow-hidden"
                    prop.children [
                        WorkspaceFixture.Toolbar()
                        Workspace.Workspace<string>(
                            className = "swt:flex-1 swt:min-h-0 swt:border swt:border-base-content/20"
                        )
                    ]
                ]
        )
