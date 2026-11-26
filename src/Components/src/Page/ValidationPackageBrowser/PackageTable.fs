namespace Arcitisa.Components.Page.ValidationPackageBrowser

open Fable.Core
open Feliz
open Arcitisa.Components
open Arcitisa.Components.Page.ValidationPackageBrowser.Context
open Arcitisa.Components.Primitive
open Arcitisa.Components.Primitive.LoadingSpinner
open Arcitisa.Components.Primitive.Select
open Types

[<Erase; Mangle(false)>]
type PackageTable =

    [<ReactComponent>]
    static member private HeaderSelect
        (label: string, optionNames: string[], selectedIndex: int option, setSelectedIndex: int option -> unit)
        =
        let selected = selectedIndex |> Option.map (fun i -> optionNames.[i])

        SingleSelect.SingleSelect(
            optionNames |> Array.map (fun name -> {| item = name; label = name |}),
            selectedIndex,
            setSelectedIndex,
            triggerRenderFn =
                (fun (_: {| isOpen: bool |}) ->
                    Html.div [
                        prop.testId $"validation-package-selector-{label.ToLower()}-filter"
                        prop.className [
                            "swt:flex swt:items-center swt:gap-1 swt:text-xs swt:font-medium swt:uppercase"
                            if selected.IsSome then
                                "swt:text-accent"
                        ]
                        prop.children [
                            Html.span [ prop.text label ]
                            Html.span [
                                prop.className "swt:iconify swt:fluent--filter-20-regular"
                            ]
                            match selected with
                            | Some name ->
                                Html.span [
                                    prop.className "swt:badge swt:badge-xs swt:badge-primary swt:max-md:hidden"
                                    prop.text name
                                ]
                            | None -> Html.none
                        ]
                    ]
                )
        )

    [<ReactComponent>]
    static member PackageTableHeader
        (
            authors: string[],
            tags: string[],
            selectedAuthor: string option,
            setSelectedAuthor: string option -> unit,
            selectedTag: string option,
            setSelectedTag: string option -> unit,
            checkedSort: CheckedSort,
            setCheckedSort: CheckedSort -> unit
        ) =

        let tagIndex =
            match selectedTag with
            | Some tag -> tags |> Array.tryFindIndex (fun t -> t = tag)
            | None -> None

        let setSelectedTag (index: int option) =
            match index with
            | Some i -> setSelectedTag (tags |> Array.tryItem i)
            | None -> setSelectedTag None

        let authorIndex =
            match selectedAuthor with
            | Some author -> authors |> Array.tryFindIndex (fun a -> a = author)
            | None -> None

        let setSelectedAuthor (index: int option) =
            match index with
            | Some i -> setSelectedAuthor (authors |> Array.tryItem i)
            | None -> setSelectedAuthor None

        Html.thead [
            Html.tr [
                Html.th [
                    prop.className "swt:w-12"
                    prop.children [
                        Html.button [
                            prop.type' "button"
                            prop.testId "validation-package-selector-sort-checked"
                            prop.title (
                                match checkedSort with
                                | CheckedSort.None -> "Sort: checked state"
                                | CheckedSort.CheckedFirst -> "Sort: checked first"
                                | CheckedSort.CheckedLast -> "Sort: checked last"
                            )
                            prop.className [
                                "swt:btn swt:btn-xs swt:btn-ghost swt:shrink-0"
                                if checkedSort <> CheckedSort.None then
                                    "swt:text-accent"
                            ]
                            prop.onClick (fun _ -> setCheckedSort (Helper.nextCheckedSort checkedSort))
                            prop.children [
                                Html.i [
                                    prop.className [
                                        "swt:iconify"
                                        match checkedSort with
                                        | CheckedSort.None -> "swt:fluent--filter-20-regular"
                                        | CheckedSort.CheckedFirst ->
                                            "swt:fluent--arrow-sort-up-lines-16-regular swt:size-4"
                                        | CheckedSort.CheckedLast ->
                                            "swt:fluent--arrow-sort-down-lines-16-regular swt:size-4"
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
                Html.th "Name"
                Html.th [ prop.className "swt:max-md:hidden"; prop.text "Summary" ]
                Html.th "Version"
                Html.th [
                    PackageTable.HeaderSelect("Tags", tags, tagIndex, setSelectedTag)
                ]
                Html.th [
                    PackageTable.HeaderSelect("Authors", authors, authorIndex, setSelectedAuthor)
                ]
                Html.th [ prop.className "swt:max-md:hidden"; prop.text "Released" ]
                Html.th "Info"
            ]
        ]

    [<ReactComponent>]
    static member PackageTableBody(state: SelectorState, filteredPackages: ValidationPackageDTO[]) =
        match state with
        | SelectorState.Loading
        | SelectorState.Idle ->
            Html.tbody [
                Html.tr [
                    Html.td [
                        prop.colSpan 8
                        prop.style [ style.textAlign.center ]
                        prop.children [
                            LoadingSpinner.LoadingSpinner("Loading validation packages...", size = DaisyuiSize.XL)
                        ]
                    ]
                ]
            ]
        | SelectorState.Error e ->
            Html.tbody [
                Html.tr [
                    Html.td [
                        prop.colSpan 8
                        prop.children [
                            Html.div [
                                prop.className
                                    "swt:text-error swt:text-sm swt:font-semibold swt:flex swt:flex-col swt:gap-2"
                                prop.children [
                                    Html.h1 "Error loading packages :("
                                    Html.details [
                                        prop.children [ Html.summary "Details"; Html.pre [ prop.text e.Message ] ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        | SelectorState.Loaded _ when Array.isEmpty filteredPackages ->
            Html.tbody [
                Html.tr [
                    Html.td [
                        prop.colSpan 8
                        prop.style [ style.textAlign.center ]
                        prop.text "No packages found."
                    ]
                ]
            ]
        | SelectorState.Loaded _ -> PackagePagination.PackagePagination(filteredPackages)
