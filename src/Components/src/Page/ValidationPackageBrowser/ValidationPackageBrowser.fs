namespace Arcitisa.Components.Page.ValidationPackageBrowser

open Fable.Core
open Feliz
open ARCtrl.ValidationPackages
open Arcitisa.Components
open Arcitisa.Components.Page.ValidationPackageBrowser.Context
open Types
open Arcitisa.Components.Primitive.Types
open Arcitisa.Components.Primitive.LoadingSpinner

module private ValidationPackageBrowserModel =

    let createLatestPackage (dto: ValidationPackageDTO) =
        ValidationPackage(dto.Name, ?version = Some(Helper.toVersionString dto))

    let createNextConfig
        (currentConfig: ValidationPackagesConfig)
        (packages: ValidationPackageDTO[])
        (edits: Map<string, option<ValidationPackage>>)
        (removedUnlisted: Set<string>)
        =
        let newPackages =
            Helper.computeNewPackages currentConfig packages edits removedUnlisted

        ValidationPackagesConfig.make (ResizeArray newPackages) currentConfig.ARCSpecification


[<Erase; Mangle(false)>]
type ValidationPackageBrowser =

    [<ReactComponent>]
    static member private UnlistedBanner
        (unlistedNames: string[], setRemovedUnlisted: (Set<string> -> Set<string>) -> unit)
        =

        let expanded, setExpanded = React.useState false

        let removeUnlisted (name: string) =
            setRemovedUnlisted (fun set -> Set.add name set)

        if Array.isEmpty unlistedNames then
            Html.none
        else
            Html.div [
                prop.className "swt:alert swt:alert-warning"
                prop.testId "validation-package-selector-unlisted-banner"
                prop.children [
                    Html.div [
                        prop.className "swt:flex swt:flex-col swt:w-full"
                        prop.children [
                            Html.div [
                                prop.className "swt:flex swt:items-center swt:gap-2"
                                prop.children [
                                    Html.span [
                                        prop.className "swt:iconify swt:fluent--warning-20-regular"
                                    ]
                                    Html.span (
                                        sprintf
                                            "%d Packages in current config not available online"
                                            unlistedNames.Length
                                    )
                                    Html.button [
                                        prop.type' "button"
                                        prop.className "swt:btn swt:btn-xs"
                                        prop.text (if expanded then "Hide" else "Show")
                                        prop.onClick (fun _ -> setExpanded (not expanded))
                                    ]
                                ]
                            ]
                            if expanded then
                                Html.ul [
                                    prop.children [
                                        for name in unlistedNames do
                                            Html.li [
                                                prop.className "swt:flex swt:items-center swt:gap-2 swt:py-1"
                                                prop.children [
                                                    Html.span [ prop.text name ]
                                                    Html.button [
                                                        prop.type' "button"
                                                        prop.testId (
                                                            "validation-package-selector-remove-unlisted-" + name
                                                        )
                                                        prop.className "swt:btn swt:btn-xs swt:btn-error"
                                                        prop.text "Remove"
                                                        prop.onClick (fun _ -> removeUnlisted name)
                                                    ]
                                                ]
                                            ]
                                    ]
                                ]
                        ]
                    ]
                ]
            ]

    [<ReactComponent>]
    static member private SubmitBar(isSubmitting: bool, isDirty: bool, isBlocked: bool, submit: unit -> unit) =

        Html.div [
            prop.className "swt:flex swt:justify-end swt:items-center swt:gap-2 swt:p-2 swt:border-t"
            prop.children [
                if isSubmitting then
                    Html.span [
                        prop.key "submitting-spinner"
                        prop.className "swt:loading swt:loading-spinner swt:loading-sm"
                    ]
                Html.button [
                    prop.key "submit-button"
                    prop.type' "button"
                    prop.testId "validation-package-selector-submit"
                    prop.className "swt:btn swt:btn-primary"
                    prop.disabled (not isDirty || isSubmitting || isBlocked)
                    prop.onClick (fun _ -> submit ())
                    prop.text "Submit"
                ]
            ]
        ]

    [<ReactComponent(true)>]
    static member ValidationPackageBrowser
        (
            config: ValidationPackagesConfig,
            writeConfig: ValidationPackagesConfig -> JS.Promise<Result<unit, exn>>,
            // https://avpr.nfdi4plants.org/swagger/index.html#/Validation%20Packages/GetAllPackages
            fetchValidationPackages: unit -> JS.Promise<ValidationPackageDTO[]>,
            ?onError: exn -> unit,
            // Blocks submitting, for example while the host could not read the existing config and a write would overwrite it.
            ?submitDisabled: bool
        ) =
        let submitDisabled = defaultArg submitDisabled false
        let state, setState = React.useState (fun () -> SelectorState.Idle)

        let edits, setEdits =
            React.useStateWithUpdater Map.empty<string, ValidationPackage option>
        // ---------------------------------------------------------------------
        // These are all available filter options. They are required to compute the filtered packages.
        // Moving them into their specific sub component/a context created a lot of code complexity. This is the most simple approach.
        // combining them into a single state is possible but requires a lot of "useCallback" and "useMemo" to avoid unnecessary re-renders. This is the most simple approach.
        let searchQuery, setSearchQuery = React.useState ""
        let searchFields, setSearchFields = React.useState SearchFields.Name

        let selectedAuthorFilter, setSelectedAuthorFilter =
            React.useState (None: string option)

        let selectedTagFilter, setSelectedTagFilter = React.useState (None: string option)
        let checkedSort, setCheckedSort = React.useState CheckedSort.None
        // ---------------------------------------------------------------------

        let removedUnlisted, setRemovedUnlisted =
            React.useStateWithUpdater Set.empty<string>

        let submitting, setSubmitting = React.useState false

        /// This config state is used to compare with the current config state to determine if there are any changes that need to be submitted. It is initialized with the initial config and will only update after the incoming config changes (for example after ``writeConfig``).
        let config_old = React.useMemo ((fun () -> config), [| box config |])

        React.useEffectOnce (fun () ->
            setState SelectorState.Loading

            fetchValidationPackages ()
            |> Promise.map (fun packages -> setState (SelectorState.Loaded packages))
            |> Promise.catch (fun ex ->
                console.error ("Error fetching validation packages:", ex)
                setState (SelectorState.Error ex)
                onError |> Option.iter (fun f -> f ex)
            )
            |> Promise.start
        )

        let packages =
            React.useMemo (
                (fun () ->
                    match state with
                    | SelectorState.Loaded packages -> packages
                    | _ -> [||]
                ),
                [| box state |]
            )

        let authorOptions =
            React.useMemo ((fun () -> Helper.distinctAuthors packages), [| box packages |])

        let tagOptions =
            React.useMemo ((fun () -> Helper.distinctTags packages), [| box packages |])

        let rowStateOf (dto: ValidationPackageDTO) =
            match Map.tryFind dto.Name edits with
            | Some None -> PackageRowState.Unchecked
            | Some(Some p) ->
                if p.Version = Some(Helper.toVersionString dto) then
                    PackageRowState.Checked
                else
                    PackageRowState.HasOlderVersion
            | None -> Helper.rowState config dto

        let RowStateMap =
            React.useMemo (
                (fun () -> packages |> Array.map (fun dto -> dto.Name, rowStateOf dto) |> Map.ofArray),
                [| box packages; box edits; box config |]
            )

        let filteredPackages =
            React.useMemo (
                (fun () ->
                    packages
                    |> Helper.filterBySearch searchFields searchQuery
                    |> Helper.filterByTag selectedTagFilter
                    |> Helper.filterByAuthor selectedAuthorFilter
                    |> Helper.sortByChecked checkedSort RowStateMap
                ),
                [|
                    box packages
                    box searchFields
                    box searchQuery
                    box selectedTagFilter
                    box selectedAuthorFilter
                    box checkedSort
                    box RowStateMap
                |]
            )

        let isDirty =
            React.useMemo (
                (fun () ->

                    let newConfig =
                        ValidationPackageBrowserModel.createNextConfig config packages edits removedUnlisted

                    newConfig <> config_old
                ),
                [| box edits; box removedUnlisted |]
            )

        let toggle (dto: ValidationPackageDTO) =
            let latest = ValidationPackageBrowserModel.createLatestPackage dto

            match rowStateOf dto with
            | PackageRowState.Unchecked
            | PackageRowState.HasOlderVersion -> setEdits (fun edits -> Map.add dto.Name (Some latest) edits)
            | PackageRowState.InvalidVersion -> setEdits (fun edits -> Map.add dto.Name (Some latest) edits)
            | PackageRowState.Checked -> setEdits (fun edits -> Map.add dto.Name None edits)

        let updateToLatest (dto: ValidationPackageDTO) =
            setEdits (fun edits -> Map.add dto.Name (Some(ValidationPackageBrowserModel.createLatestPackage dto)) edits)

        let unlistedNames =
            React.useMemo (
                (fun () ->
                    Helper.unlistedNames config packages
                    |> Array.filter (fun name -> not (removedUnlisted.Contains name))
                ),
                [| box config; box packages; box removedUnlisted |]
            )

        let submit () =
            if isDirty && not submitting && not submitDisabled then
                setSubmitting true

                let newConfig =
                    ValidationPackageBrowserModel.createNextConfig config packages edits removedUnlisted

                writeConfig newConfig
                |> Promise.map (fun result ->
                    match result with
                    | Ok() ->
                        setEdits (fun _ -> Map.empty)
                        setRemovedUnlisted (fun _ -> Set.empty)
                        setSubmitting false
                    | Error ex ->
                        setSubmitting false
                        onError |> Option.iter (fun f -> f ex)
                )
                |> Promise.catch (fun ex ->
                    setSubmitting false
                    onError |> Option.iter (fun f -> f ex)
                )
                |> Promise.start

        let ctxValue =
            React.useMemo (
                (fun () -> {
                    RowStateMap = RowStateMap
                    Toggle = toggle
                    UpdateToLatest = updateToLatest
                }),
                [| box RowStateMap |]
            )

        ValidationPackageBrowserCtx.Provider(
            ctxValue,
            React.Fragment [
                Html.div [
                    prop.className "swt:flex swt:flex-col swt:gap-2 swt:p-2 swt:min-h-0"
                    prop.children [
                        Html.h2 [
                            prop.className "swt:text-2xl swt:font-bold"
                            prop.text "Validation Packages"
                        ]
                        match state with
                        | SelectorState.Loaded _ ->
                            ValidationPackageBrowser.UnlistedBanner(unlistedNames, setRemovedUnlisted)
                        | _ -> Html.none
                        SearchField.SearchField(searchQuery, setSearchQuery, searchFields, setSearchFields)
                        Html.div [
                            prop.className "swt:overflow-y-auto swt:grow"
                            prop.children [
                                Html.table [
                                    prop.className "swt:table swt:md:table-fixed swt:w-full swt:table-pin-rows"
                                    prop.children [
                                        PackageTable.PackageTableHeader(
                                            authors = authorOptions,
                                            tags = tagOptions,
                                            selectedAuthor = selectedAuthorFilter,
                                            setSelectedAuthor = setSelectedAuthorFilter,
                                            selectedTag = selectedTagFilter,
                                            setSelectedTag = setSelectedTagFilter,
                                            checkedSort = checkedSort,
                                            setCheckedSort = setCheckedSort
                                        )
                                        PackageTable.PackageTableBody(state, filteredPackages)
                                    ]
                                ]
                            ]
                        ]
                        ValidationPackageBrowser.SubmitBar(
                            isSubmitting = submitting,
                            isDirty = isDirty,
                            isBlocked = submitDisabled,
                            submit = submit
                        )
                    ]
                ]
            ]
        )
