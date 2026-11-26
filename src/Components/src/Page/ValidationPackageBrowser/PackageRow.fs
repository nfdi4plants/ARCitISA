namespace Arcitisa.Components.Page.ValidationPackageBrowser

open Fable.Core
open Feliz
open Arcitisa.Components
open Arcitisa.Components.Page.ValidationPackageBrowser.Context
open Arcitisa.Components.Primitive.Popover
open Types

[<Erase; Mangle(false)>]
type PackageRow =

    [<ReactComponent>]
    static member private InfoCard(pkg: ValidationPackageDTO) =
        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-2"
            prop.children [
                Html.h2 [
                    prop.className "swt:text-lg swt:font-bold"
                    prop.text pkg.Name
                ]
                Html.p [ prop.text pkg.Summary ]
                Html.p [
                    prop.className "swt:whitespace-pre-line swt:text-sm swt:opacity-80"
                    prop.text pkg.Description
                ]
                Html.div [
                    prop.className "swt:flex swt:flex-wrap swt:gap-1"
                    prop.children [
                        Html.span [
                            prop.className "swt:badge swt:badge-outline"
                            prop.text (Helper.toVersionString pkg)
                        ]
                        if pkg.ProgrammingLanguage <> "" then
                            Html.span [
                                prop.className "swt:badge swt:badge-outline"
                                prop.text pkg.ProgrammingLanguage
                            ]
                        for tag in pkg.Tags do
                            match tag.Name with
                            | Some name ->
                                Html.span [
                                    prop.className
                                        "swt:badge swt:badge-accent swt:h-auto swt:min-h-5 swt:whitespace-normal swt:wrap-break-word"
                                    prop.text name
                                ]
                            | None -> Html.none
                    ]
                ]
                if pkg.Authors.Length > 0 then
                    Html.p [
                        prop.className "swt:text-sm"
                        prop.text (
                            pkg.Authors
                            |> Array.choose (fun a -> a.FullName)
                            |> String.concat ", "
                            |> sprintf "Authors: %s"
                        )
                    ]
                let releaseDate = pkg.ReleaseDate.ToString("yyyy-MM-dd")

                Html.span [
                    prop.className "swt:text-sm"
                    prop.text $"Released: {releaseDate}"
                ]

                if pkg.CQCHookEndpoint <> "" then
                    Html.span [
                        prop.className "swt:text-sm swt:break-all"
                        prop.text $"CQC Hook: {pkg.CQCHookEndpoint}"
                    ]

                if pkg.ReleaseNotes <> "" then
                    Html.p [
                        prop.className "swt:text-sm swt:whitespace-pre-line"
                        prop.text pkg.ReleaseNotes
                    ]
            ]
        ]

    [<ReactComponent(true)>]
    static member PackageRow(pkg: ValidationPackageDTO, rowState: PackageRowState, updateToLatest, toggle, ?key: obj) =


        let isChecked = rowState = PackageRowState.Checked

        let isIndeterminate =
            rowState = PackageRowState.HasOlderVersion
            || rowState = PackageRowState.InvalidVersion

        let infoButtonRef = React.useButtonRef ()

        let handleCheckboxChange (_: Browser.Types.Event) =
            if isIndeterminate then updateToLatest pkg else toggle pkg

        let authorsTxt =
            pkg.Authors |> Array.choose (fun a -> a.FullName) |> String.concat ", "

        Html.tr [
            prop.className ""
            prop.children [
                Html.td [
                    Html.input [
                        prop.type' "checkbox"
                        prop.className "swt:checkbox swt:checkbox-sm"
                        prop.testId ("validation-package-selector-checkbox-" + pkg.Name)
                        prop.isChecked isChecked
                        prop.onChange handleCheckboxChange
                        prop.ref (fun (el: Browser.Types.Element) ->
                            if not (isNull el) then
                                (el :?> Browser.Types.HTMLInputElement).indeterminate <- isIndeterminate
                        )
                    ]
                ]
                Html.td [
                    Html.span [ prop.className "swt:font-semibold"; prop.text pkg.Name ]
                ]
                Html.td [
                    prop.className "swt:text-sm swt:opacity-80 swt:truncate swt:max-md:hidden"
                    prop.text pkg.Summary
                    prop.title pkg.Summary
                ]
                Html.td [
                    Html.div [
                        prop.className "swt:flex swt:items-center swt:gap-1"
                        prop.children [
                            Html.span [ prop.text (Helper.toVersionString pkg) ]
                            if isIndeterminate then
                                Html.button [
                                    prop.type'.button
                                    prop.className "swt:btn swt:btn-xs swt:btn-warning"
                                    prop.testId ("validation-package-selector-update-" + pkg.Name)
                                    prop.onClick (fun _ -> updateToLatest pkg)
                                    prop.text "Update"
                                    prop.title
                                        $"Current version: {Helper.toVersionString pkg} - Click to update to latest version"
                                ]
                        ]
                    ]
                ]
                Html.td [
                    Html.div [
                        prop.className "swt:flex swt:flex-wrap swt:gap-1"
                        prop.children [
                            for tag in pkg.Tags do
                                match tag.Name with
                                | Some name ->
                                    Html.span [
                                        prop.className
                                            "swt:badge swt:badge-xs swt:badge-accent swt:h-auto swt:min-h-5 swt:whitespace-normal swt:wrap-break-word"
                                        prop.text name
                                    ]
                                | None -> Html.none
                        ]
                    ]
                ]
                Html.td [
                    prop.className "swt:text-sm swt:truncate swt:max-md:max-w-6"
                    prop.text authorsTxt
                    prop.title authorsTxt
                ]
                Html.td [
                    prop.className "swt:text-sm swt:max-md:hidden"
                    prop.text (pkg.ReleaseDate.ToString("yyyy-MM-dd"))
                ]
                Html.td [
                    Popover.Simple(
                        trigger =
                            Html.button [
                                prop.ref infoButtonRef
                                prop.type'.button
                                prop.className "swt:btn swt:btn-square"
                                prop.testId ("validation-package-selector-info-" + pkg.Name)
                                prop.children [
                                    Html.span [ prop.className "swt:iconify swt:fluent--info-20-regular" ]
                                ]
                            ],
                        triggerClassName = "swt:w-min",
                        content = PackageRow.InfoCard pkg,
                        placement = FloatingUI.Placement.Left
                    )
                ]
            ]
        ]
