namespace Arcitisa.Components.Page.ValidationPackageBrowser

open Fable.Core
open Feliz
open Arcitisa.Components
open Arcitisa.Components.Page.ValidationPackageBrowser.Context
open Arcitisa.Components.Primitive.Popover
open Types

[<Erase; Mangle(false)>]
type SearchField =

    [<ReactComponent>]
    static member private ScopeCheckbox(field: SearchFields, isChecked: bool, onToggle: SearchFields -> unit) =
        Html.label [
            prop.className "swt:flex swt:items-center swt:gap-2 swt:cursor-pointer swt:p-1"
            prop.children [
                Html.input [
                    prop.type'.checkbox
                    prop.className "swt:checkbox swt:checkbox-xs"
                    prop.isChecked isChecked
                    prop.testId ("validation-package-selector-scope-" + Helper.searchFieldLabel field)
                    prop.onChange (fun (_: Browser.Types.Event) -> onToggle field)
                ]
                Html.span [ prop.text (Helper.searchFieldLabel field) ]
            ]
        ]

    [<ReactComponent(true)>]
    static member SearchField
        (query: string, setQuery: string -> unit, fields: SearchFields, setFields: SearchFields -> unit)
        =

        React.Fragment [
            Html.div [
                prop.className "swt:flex swt:items-center swt:gap-2"
                prop.children [
                    Html.input [
                        prop.type'.text
                        prop.className "swt:input swt:input-bordered swt:w-full"
                        prop.placeholder "Search validation packages..."
                        prop.testId "validation-package-selector-search"
                        prop.value query
                        prop.onChange (fun (value: string) -> setQuery value)
                    ]
                    Popover.Popover(
                        returnFocus = false,
                        children =
                            React.Fragment [
                                Popover.Trigger(
                                    Html.button [
                                        prop.type' "button"
                                        prop.className "swt:btn swt:btn-outline swt:btn-sm swt:shrink-0"
                                        prop.testId "validation-package-selector-scope"
                                        prop.children [
                                            Html.span [
                                                prop.className "swt:iconify swt:fluent--settings-20-regular"
                                            ]
                                        ]
                                    ]
                                )
                                Popover.Content(
                                    React.Fragment [
                                        for field in Helper.allSearchFields do
                                            SearchField.ScopeCheckbox(
                                                field,
                                                Helper.hasFlag fields field,
                                                fun f -> setFields (Helper.toggleFlag fields f)
                                            )
                                    ]
                                )
                            ]
                    )
                ]
            ]
        ]
