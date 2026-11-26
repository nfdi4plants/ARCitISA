namespace Arcitisa.Components.Composite.ArcSelector

open Feliz
open Fable.Core

open Arcitisa.Components
open Arcitisa.Components.Shared
open Arcitisa.Components.Primitive.Dropdown
open Arcitisa.Components.Composite.Actionbar
open Arcitisa.Components.Composite.Actionbar.Types

[<Erase; Mangle(false)>]
type ArcSelector =

    [<ReactMemoComponent(AreEqualFn.FsEqualsButFunctions)>]
    static member private SelectorItem
        (
            arcPointer: ARCPointer,
            onClick: ARCPointer -> unit,
            ?rmvRecentArc: ARCPointer -> unit,
            ?isCurrentlyOpenArcPath: bool,
            ?potMaxWidth,
            ?testId: string
        ) =

        let maxWidth = defaultArg potMaxWidth 48

        Html.li [
            prop.key arcPointer.path
            prop.className [
                "swt:menu-item"
                if arcPointer.isActive then
                    "swt:font-bold"
            ]
            if testId.IsSome then
                prop.testId testId.Value
            prop.children [
                Html.div [
                    prop.className "swt:flex"
                    prop.children [
                        Html.span [
                            prop.className "swt:truncate swt:block swt:min-w-30"
                            prop.style [ style.maxWidth maxWidth ]
                            prop.text arcPointer.name
                            prop.title arcPointer.path
                        ]
                        Html.div [
                            prop.className "swt:ml-auto swt:flex swt:items-center"
                            prop.children [
                                Html.div [
                                    prop.className "swt:divider swt:divider-horizontal swt:mx-0!"
                                ]
                                match rmvRecentArc with
                                | Some rmvRecentArc ->

                                    Html.button [
                                        prop.className [
                                            "swt:btn swt:btn-ghost swt:btn-square swt:btn-xs"
                                            "swt:hover:btn-error"
                                        ]
                                        prop.onClick (fun e ->
                                            e.stopPropagation ()
                                            rmvRecentArc arcPointer
                                        )
                                        prop.children [
                                            Html.i [
                                                prop.className "swt:iconify swt:fluent--delete-12-regular swt:size-4"
                                            ]
                                        ]
                                    ]
                                | None -> Html.none
                                Html.i [
                                    prop.className [
                                        "swt:iconify swt:fluent--checkmark-24-regular swt:size-4"
                                        match isCurrentlyOpenArcPath with
                                        | Some true -> ""
                                        | _ -> "swt:invisible"
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
            prop.onClick (fun _ -> onClick (arcPointer))
        ]

    [<ReactComponent(true)>]
    static member Main
        (
            recentARCs: ARCPointer[],
            onClick: ARCPointer -> unit,
            isOpen: bool,
            setIsOpen: bool -> unit,
            ?rmvRecentArc: ARCPointer -> unit,
            ?actionbar: ReactElement,
            ?potMaxWidth: int,
            ?debug: bool,
            ?isLoading: bool,
            ?currentlyOpenArcPath: string
        ) =

        let debug = defaultArg debug false

        let currentlyOpenArcName =
            currentlyOpenArcPath
            |> Option.map (fun path ->
                let normalized = PathHelpers.normalizePath path

                let name =
                    normalized.Split([| "/" |], System.StringSplitOptions.RemoveEmptyEntries)
                    |> Array.last

                name
            )

        let onClick =
            fun (arcPointer: ARCPointer) ->
                onClick arcPointer
                setIsOpen false

        let recentARCItems =
            recentARCs
            |> Array.mapi (fun i arcPointer ->
                let isCurrentlyOpenArcPath =
                    currentlyOpenArcPath
                    |> Option.exists (fun path ->
                        PathHelpers.normalizePath path = PathHelpers.normalizePath arcPointer.path
                    )

                let testId = if debug then Some $"selector-arc-item-{i}" else None

                ArcSelector.SelectorItem(
                    arcPointer,
                    onClick,
                    ?testId = testId,
                    isCurrentlyOpenArcPath = isCurrentlyOpenArcPath,
                    ?rmvRecentArc = rmvRecentArc
                )
            )

        let toggle =
            match isLoading with
            | Some true ->
                Html.button [
                    prop.className "swt:btn swt:btn-sm swt:btn-outline swt:w-48 swt:flex-nowrap swt:cursor-not-allowed"
                    prop.disabled true
                    prop.children [
                        Html.span [
                            prop.className "swt:loading swt:loading-spinner swt:loading-xs"
                        ]
                        Html.span [ prop.text "Loading..." ]
                    ]
                ]
            | _ ->
                Html.button [
                    prop.onClick (fun _ -> setIsOpen (not isOpen))
                    prop.role.button
                    prop.className "swt:btn swt:btn-sm swt:btn-outline swt:w-48 swt:flex-nowrap"
                    if debug then
                        prop.testId "selector-test"
                    prop.children [
                        Html.div [
                            prop.className "swt:min-w-0 swt:flex-1 swt:truncate swt:text-left"
                            if debug then
                                prop.testId "selector-current-arc-name"
                            match currentlyOpenArcName with
                            | Some name ->
                                prop.text name
                                prop.title name
                            | None -> prop.text "Select an ARC"
                        ]
                        Actionbar.MaterialIcon "swt:fluent--arrow-fit-height-24-regular swt:size-5 swt:shrink-0"
                    ]
                ]

        let content =
            React.Fragment [
                Html.div [
                    if debug then
                        prop.testId "selector-dropdown-content"
                    match recentARCItems with
                    | [||] ->
                        prop.children [
                            Html.li [
                                prop.className "swt:text-sm swt:text-base-content/80 swt:px-8 swt:py-2 swt:text-center"
                                prop.text "No recent ARCs"
                            ]
                        ]
                    | _ -> prop.children recentARCItems
                ]
                match actionbar with
                | Some actionbar ->
                    Html.div [ prop.className "swt:divider swt:m-0! swt:h-3!" ]

                    Html.div [
                        if debug then
                            prop.testId "selector-actionbar"
                        prop.className "swt:w-full"
                        prop.children [
                            Html.div [
                                prop.className "swt:flex swt:justify-center swt:w-full"
                                prop.children actionbar
                            ]
                        ]
                    ]
                | None -> Html.none
            ]

        Dropdown.Main(
            isOpen,
            setIsOpen,
            toggle,
            content,
            contentClassName =
                "swt:w-max swt:max-w-none swt:menu swt:bg-base-200 swt:rounded-box swt:z-99 swt:p-2 swt:shadow-sm swt:top-110% swt:menu-sm",
            closeOnClick = false
        )

    [<ReactComponent>]
    static member Entry(?debug: bool) =
        let debug = defaultArg debug false
        let isOpen, setIsOpen = React.useState false

        let currentlyOpenArcPath, setCurrentlyOpenArcPath =
            React.useState (None: string option)

        let initialRecentARCs = [|
            ARCPointer.create ("Test 1", "/Here/Test 1", false)
            ARCPointer.create ("Test 2", "/Here/Test 2", false)
            ARCPointer.create ("Test 3", "/Here/Test 3", false)
            ARCPointer.create (
                "An ARC name that is much too long to fit inside the selector",
                "/Here/An ARC name that is much too long to fit inside the selector",
                false
            )
        |]

        let recentARCs, setRecentARCs = React.useState initialRecentARCs

        let removeRecentArc (arcPointer: ARCPointer) =
            recentARCs
            |> Array.filter (fun recentArc -> recentArc.path <> arcPointer.path)
            |> setRecentARCs

            if currentlyOpenArcPath = Some arcPointer.path then
                setCurrentlyOpenArcPath None

        let actionbarButtons = [|
            ButtonInfo.create ("swt:fluent--document-add-24-regular", "Create a new ARC", fun _ -> setIsOpen false)
            ButtonInfo.create ("swt:fluent--folder-open-24-regular", "Open an existing ARC", fun _ -> setIsOpen false)
        |]

        ArcSelector.Main(
            recentARCs,
            (fun arcPointer -> setCurrentlyOpenArcPath (Some arcPointer.path)),
            isOpen,
            setIsOpen,
            rmvRecentArc = removeRecentArc,
            actionbar = Actionbar.Main(actionbarButtons, 1, debug = debug, keepContextMenuPortalLocal = true),
            ?currentlyOpenArcPath = currentlyOpenArcPath,
            debug = debug
        )
