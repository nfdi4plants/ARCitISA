namespace Swate.Components.Composite.TermSearch

open Swate.Components
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Swate.Components.Composite.TermSearch.Context
open Swate.Components.Primitive
open Swate.Components
open Swate.Components.Primitive.Select
open Swate.Components.Primitive.Select.Types

[<Erase; Mangle(false)>]
type TermSearchConfigSetter =

    [<ReactComponent>]
    static member private TriggerRender(activeKeys: string[]) =
        Html.button [
            prop.testId "term-search-config-setter-tib-trigger"
            prop.tabIndex -1
            prop.className [
                "swt:btn swt:w-fit swt:btn-primary swt:pointer-events-none"
            ]
            prop.children [
                Icons.SearchPlus("swt:size-4")
                Html.text (
                    if Array.isEmpty activeKeys then
                        "Select tib queries"
                    else
                        activeKeys |> Array.truncate 3 |> String.concat ", "
                )
                Html.text (if activeKeys.Length > 3 then " ..." else "")
            ]
        ]


    /// The term search settings, each with its title, control and description, and the state they show.
    /// Needs the term search contexts.
    [<Hook>]
    static member useSettings() =
        let activeKeysCtx = useTermSearchActiveKeysCtx ()
        let allKeysCtx = useTermSearchAllKeysCtx ()

        let activeKeysState =
            if isNullOrUndefined (box activeKeysCtx.state) then
                TermSearchActiveKeysContext.init (Set.empty)
            else
                activeKeysCtx.state

        let activeKeys =
            activeKeysState.activeKeys |> Option.ofObj |> Option.defaultValue [||]

        let selectedIndices =
            activeKeys
            |> Array.choose (fun key -> allKeysCtx |> Seq.tryFindIndex (fun activeKey -> activeKey = key))
            |> Set

        let selectItems: SelectItem<string>[] =
            allKeysCtx |> Seq.map (fun key -> {| label = key; item = key |}) |> Array.ofSeq

        let setSelectedIndices =
            fun selectedIndices ->
                let nextActiveKeys = selectedIndices |> Seq.map (fun i -> allKeysCtx |> Seq.item i)

                activeKeysCtx.setState {|
                    activeKeysState with
                        activeKeys = Array.ofSeq nextActiveKeys
                |}

        let defaultSearchActive = not activeKeysState.disableDefault

        let TriggerRender = fun _ -> TermSearchConfigSetter.TriggerRender(activeKeys)

        let settings = [|
            {|
                title = "Enable ARCitISA default search"
                settingElement =
                    Html.input [
                        prop.className [
                            if defaultSearchActive then
                                "swt:toggle-primary"
                            "swt:toggle"
                        ]
                        prop.type'.checkbox
                        prop.isChecked defaultSearchActive
                        prop.onChange (fun (b: bool) ->
                            activeKeysCtx.setState {|
                                activeKeysState with
                                    disableDefault = not b
                            |}
                        )
                    ]
                description = Html.p "When you deactivate this, the default search will not be used."
            |}
            {|
                title = "Configure TIB search"
                settingElement =
                    MultiSelect.MultiSelect<string>(
                        selectItems,
                        selectedIndices,
                        setSelectedIndices,
                        triggerRenderFn = TriggerRender,
                        middleware = [|
                            FloatingUI.Middleware.flip ()
                            FloatingUI.Middleware.shift ()
                            FloatingUI.Middleware.offset (4)
                        |],
                        dropdownPlacement = FloatingUI.Placement.BottomEnd
                    )
                description =
                    React.Fragment [
                        Html.p
                            "Adds support for high performance TIB term search. Choose a collection of terms to search through."
                        Html.p [
                            prop.text "Selecting multiple collections may impact search performance."
                        ]
                    ]
            |}
        |]

        {|
            settings = settings
            activeKeys = activeKeys
            disableDefault = activeKeysState.disableDefault
        |}

    [<ReactComponentAttribute(true)>]
    static member TermSearchConfigSetter
        (renderer:
            {|
                title: string
                settingElement: ReactElement
                description: ReactElement
            |}
                -> ReactElement)
        =
        let termSearch = TermSearchConfigSetter.useSettings ()

        React.Fragment [

            //Debugging component, storing data for storybook tests
            Html.div [
                prop.className "swt:hidden"
                prop.ariaHidden true
                prop.testId "term-search-config-setter"
                prop.custom ("data-activekeyscount", termSearch.activeKeys.Length)
                prop.custom ("data-defaultdisables", termSearch.disableDefault)
                prop.custom ("data-activekeys", termSearch.activeKeys |> Array.sort |> String.concat "; ")
            ]

            for setting in termSearch.settings do
                renderer setting
        ]

    [<ReactComponent>]
    static member Entry() =

        let renderer =
            fun
                (props:
                    {|
                        title: string
                        settingElement: ReactElement
                        description: ReactElement
                    |}) ->
                React.Fragment [
                    Html.label [ prop.className "swt:label"; prop.text props.title ]
                    props.settingElement
                    Html.div [
                        prop.className "swt:text-xs swt:base-content/60 swt:md:col-span-2 swt:prose"
                        prop.children props.description
                    ]
                ]

        Html.fieldSet [
            prop.className "swt:fieldset swt:bg-base-200 swt:border-base-300 swt:rounded-box swt:border swt:p-4"
            prop.children [
                Html.legend [
                    prop.className "swt:fieldset-legend"
                    prop.text "Configure Term Search"
                ]
                TermSearchConfigSetter.TermSearchConfigSetter renderer
            ]
        ]
