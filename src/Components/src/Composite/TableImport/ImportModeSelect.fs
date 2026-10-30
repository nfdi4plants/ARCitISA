namespace Swate.Components.Composite.TableImport

open ARCtrl
open Fable.Core
open Feliz

[<Erase; Mangle(false)>]
type ImportModeSelect =

    [<ReactComponent(true)>]
    static member ImportModeSelect
        (importType: TableJoinOptions, setImportType: TableJoinOptions -> unit, ?disabled: bool)
        =
        let disabled = defaultArg disabled false

        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-2"
            prop.children [
                Html.div [
                    prop.className "swt:text-sm swt:font-medium swt:truncate"
                    prop.text "Import mode"
                ]
                Html.div [
                    prop.className "swt:flex swt:flex-col swt:gap-1"
                    prop.children [
                        for option in Helper.ImportMode.options do
                            let isChecked = (importType = option.Mode)

                            Html.div [
                                prop.key option.Key
                                prop.className "swt:flex swt:flex-col"
                                prop.children [
                                    Html.label [
                                        prop.className "swt:inline-flex swt:items-center swt:gap-2 swt:cursor-pointer"
                                        prop.children [
                                            Html.input [
                                                prop.testId $"table-import-mode-{option.Key}"
                                                prop.type'.radio
                                                prop.name "table-import-mode"
                                                prop.className "swt:radio swt:radio-sm"
                                                prop.isChecked isChecked
                                                prop.disabled disabled
                                                prop.onChange (fun (_: bool) -> setImportType option.Mode)
                                            ]
                                            Html.span [
                                                prop.className "swt:text-sm swt:text-base-content"
                                                prop.text option.Label
                                            ]
                                        ]
                                    ]
                                    Html.span [
                                        prop.className "swt:text-xs swt:opacity-70 swt:ml-6"
                                        prop.text option.Description
                                    ]
                                ]
                            ]
                    ]
                ]
            ]
        ]
