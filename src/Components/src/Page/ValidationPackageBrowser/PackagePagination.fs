namespace Arcitisa.Components.Page.ValidationPackageBrowser

open Fable.Core
open Feliz
open Arcitisa.Components
open Types
open Context

[<Erase; Mangle(false)>]
type PackagePagination =

    [<ReactMemoComponent(AreEqualFn.FsEqualsButFunctions)>]
    static member MemoizedPackageRow
        (pkg: ValidationPackageDTO, rowState: PackageRowState, updateToLatest, toggle, ?key: obj)
        =
        PackageRow.PackageRow(pkg, rowState, updateToLatest, toggle, ?key = key)

    [<ReactComponent(true)>]
    static member PackagePagination(items: ValidationPackageDTO[]) =
        let page, setPage = React.useState 0
        let prevItems, setPrevItems = React.useState items
        let ctx = useValidationPackageBrowserCtx ()

        // Render-time state adjustment: reset page whenever the filtered
        // items array changes identity (i.e. any filter changed). No useEffect.
        if not (obj.ReferenceEquals(prevItems, items)) then
            setPrevItems items
            setPage 0

        let pageItems =
            React.useMemo ((fun () -> Helper.slicePage items page), [| box items; box page |])

        let totalPages = Helper.pageCount items

        React.Fragment [
            Html.tbody [
                for pkg in pageItems do
                    let rowState =
                        ctx.RowStateMap
                        |> Map.tryFind pkg.Name
                        |> Option.defaultValue PackageRowState.Unchecked

                    PackagePagination.MemoizedPackageRow(pkg, rowState, ctx.UpdateToLatest, ctx.Toggle, key = pkg.Name)
            ]
            Html.tfoot [
                Html.tr [
                    Html.td [
                        prop.colSpan 8
                        prop.className "swt:bg-base-100"
                        prop.children [
                            Html.div [
                                prop.className "swt:flex swt:items-center swt:justify-center swt:gap-6 swt:py-2"
                                prop.children [
                                    Html.button [
                                        prop.type'.button
                                        prop.testId "validation-package-selector-prev"
                                        prop.className "swt:btn swt:btn-xs"
                                        prop.text "Prev"
                                        prop.disabled (page <= 0)
                                        prop.onClick (fun _ -> setPage (max 0 (page - 1)))
                                    ]
                                    Html.span [
                                        prop.testId "validation-package-selector-page-indicator"
                                        prop.className "swt:text-xs swt:text-base-content/70"
                                        prop.text $"Page {page + 1} of {totalPages}"
                                    ]
                                    Html.button [
                                        prop.type'.button
                                        prop.testId "validation-package-selector-next"
                                        prop.className "swt:btn swt:btn-xs"
                                        prop.text "Next"
                                        prop.disabled (page + 1 >= totalPages)
                                        prop.onClick (fun _ -> setPage (page + 1))
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]
