namespace Swate.Components.Page.Changelog

open Fable.Core
open Feliz
open Swate.Components.Api.GitHubReleases
open Swate.Components.Composite.MarkdownText

module private ChangelogTypes =

    type Section = {
        title: string
        count: int
        target: Browser.Types.Element
        level: int
        children: Section[]
    }

module private ChangelogHelpers =

    open ChangelogTypes

    [<Emit("Array.from($0.querySelectorAll($1))")>]
    let queryElements (_root: Browser.Types.Element) (_selector: string) : Browser.Types.Element[] = jsNative

    [<Emit("$0.scrollIntoView({ block: 'start', inline: 'nearest' })")>]
    let scrollToSection (_target: Browser.Types.Element) : unit = jsNative

    // Read the rendered Markdown so code fences, legacy headings and nested lists
    // follow the same parsing rules as the visible release notes.
    let sections (root: Browser.Types.Element) =
        let nodes = queryElements root "h1,h2,h3,h4,h5,h6,li"

        let headingLevel (node: Browser.Types.Element) =
            match node.tagName.ToUpperInvariant() with
            | "H1" -> 1
            | "H2" -> 2
            | "H3" -> 3
            | "H4" -> 4
            | "H5" -> 5
            | "H6" -> 6
            | _ -> 0

        let headings =
            nodes |> Array.indexed |> Array.filter (fun (_, node) -> headingLevel node > 0)

        let countItems nodes =
            nodes
            |> Array.filter (fun (node: Browser.Types.Element) -> node.tagName.ToUpperInvariant() = "LI")
            |> Array.length

        let flatSections = [|
            if Array.isEmpty headings then
                yield {
                    title = "Release notes"
                    count = countItems nodes
                    target = root
                    level = 0
                    children = [||]
                }
            else
                let firstIndex, _ = headings.[0]
                let introCount = nodes |> Array.take firstIndex |> countItems

                if introCount > 0 then
                    yield {
                        title = "Overview"
                        count = introCount
                        target = root
                        level = 0
                        children = [||]
                    }

                for index, heading in headings do
                    let contents =
                        nodes
                        |> Array.skip (index + 1)
                        |> Array.takeWhile (fun node ->
                            headingLevel node = 0 || headingLevel node > headingLevel heading
                        )

                    yield {
                        title =
                            if System.String.IsNullOrWhiteSpace heading.textContent then
                                "Untitled section"
                            else
                                heading.textContent.Trim()
                        count = countItems contents
                        target = heading
                        level = headingLevel heading
                        children = [||]
                    }
        |]

        // Nest under the nearest preceding shallower heading, even when legacy
        // notes skip heading levels. Overview and fallback entries stay separate.
        let mutable cursor = 0

        let rec readChildren parentLevel = [|
            while cursor < flatSections.Length && flatSections.[cursor].level > parentLevel do
                let section = flatSections.[cursor]
                cursor <- cursor + 1

                let children =
                    if section.level = 0 then
                        [||]
                    else
                        readChildren section.level

                yield { section with children = children }
        |]

        readChildren -1

[<Erase; Mangle(false)>]
type Changelog =

    [<ReactComponent>]
    static member private NavigationItem
        (section: ChangelogTypes.Section, navigate: Browser.Types.Element -> unit)
        : ReactElement =
        Html.li [
            prop.children [
                Html.button [
                    prop.type'.button
                    prop.ariaLabel $"{section.title} {section.count}"
                    prop.className "swt:flex swt:justify-between swt:gap-3"
                    prop.onClick (fun _ -> navigate section.target)
                    prop.children [
                        Html.span section.title
                        Html.span [
                            prop.className "swt:badge swt:badge-sm swt:shrink-0"
                            prop.text section.count
                        ]
                    ]
                ]
                if not (Array.isEmpty section.children) then
                    Html.ul [
                        prop.className "swt:ml-3 swt:pl-3 swt:border-l swt:border-base-300"
                        prop.children [
                            for child in section.children do
                                Changelog.NavigationItem(child, navigate)
                        ]
                    ]
            ]
        ]

    [<ReactComponent>]
    static member private ReleaseNotes(body: string option, pagination: ReactElement) =
        let contentRef = React.useElementRef ()
        let sections, setSections = React.useState<ChangelogTypes.Section[]> [||]

        let markdown =
            match body with
            | Some text when not (System.String.IsNullOrWhiteSpace text) -> text
            | _ -> "No release notes provided."

        React.useLayoutEffect (
            (fun () ->
                contentRef.current
                |> Option.iter (fun root ->
                    root.scrollTop <- 0
                    setSections (ChangelogHelpers.sections root)
                )
            ),
            [| box markdown |]
        )

        let navigate (target: Browser.Types.Element) =
            contentRef.current
            |> Option.iter (fun root ->
                if target = root then
                    root.scrollTop <- 0
                else
                    ChangelogHelpers.scrollToSection target
            )

        Html.div [
            prop.className "swt:flex swt:flex-col swt:md:flex-row swt:gap-4 swt:grow swt:min-h-0"
            prop.children [
                // navigation sidebar (on small screens it is placed above the content)
                Html.nav [
                    prop.ariaLabel "Release note sections"
                    prop.className "swt:md:w-56 swt:shrink-0 swt:overflow-auto swt:max-h-48 swt:md:max-h-none"
                    prop.children [
                        Html.ul [
                            prop.className "swt:menu swt:w-full swt:bg-base-200 swt:rounded-box"
                            prop.children [
                                if sections |> Array.exists (fun section -> not (Array.isEmpty section.children)) then
                                    Html.li [
                                        prop.className "swt:text-xs swt:opacity-70 swt:menu-title"
                                        prop.text "Counts include subsections."
                                    ]
                                for section in sections do
                                    Changelog.NavigationItem(section, navigate)
                            ]
                        ]
                    ]
                ]
                // content area
                Html.div [
                    prop.className
                        "swt:flex swt:flex-col swt:grow swt:min-w-0 swt:min-h-0 swt:w-fit swt:gap-2 swt:overflow-hidden"
                    prop.children [
                        // scrollable markdown content
                        Html.div [
                            prop.ref contentRef
                            prop.role "region"
                            prop.ariaLabel "Release notes"
                            prop.className "swt:flex-1 swt:min-h-0 swt:overflow-auto swt:p-4 swt:rounded-box"
                            prop.children [ MarkdownTextViewer.MarkdownTextViewer(markdown) ]
                        ]
                        // pagination controls
                        pagination
                    ]
                ]
            ]
        ]

    /// currentRelease accepts an exact tag or a semantic version (with or without v).
    /// Starts at the matching release, or the first release when no match exists.
    /// Navigation preserves the order returned by fetchReleases.
    [<ReactComponent(true)>]
    static member Changelog(fetchReleases: unit -> JS.Promise<Release[]>, currentRelease: string) =
        let releases, setReleases = React.useState<Release[]> ([||])
        let selectedIndex, setSelectedIndex = React.useState 0
        let loading, setLoading = React.useState true
        let error, setError = React.useState<string option> None
        let retry, setRetry = React.useState 0

        React.useEffect (
            (fun () ->
                let mutable active = true
                setLoading true
                setError None
                setReleases [||]
                setSelectedIndex 0

                promise {
                    try
                        let! loaded = fetchReleases ()

                        if active then
                            setReleases loaded

                            loaded
                            |> Array.tryFindIndex (matchesVersion currentRelease)
                            |> Option.defaultValue 0
                            |> setSelectedIndex

                            setLoading false
                    with ex ->
                        if active then
                            setError (Some ex.Message)
                            setLoading false
                }
                |> Promise.start

                FsReact.createDisposable (fun () -> active <- false)
            ),
            [| box fetchReleases; box currentRelease; box retry |]
        )

        let current = releases |> Array.tryItem selectedIndex

        Html.section [
            prop.className "swt:w-full swt:overflow-auto swt:p-6 swt:flex swt:flex-col swt:grow swt:gap-4"
            prop.ariaLabel "Changelog"
            prop.children [
                Html.h1 [
                    prop.className "swt:text-2xl swt:font-bold swt:mb-4"
                    prop.text "Changelog"
                ]
                if loading then
                    Html.p [ prop.role "status"; prop.text "Loading release notes..." ]
                else
                    match error with
                    | Some message ->
                        Html.div [
                            prop.role.alert
                            prop.className "swt:alert swt:alert-error swt:alert-vertical swt:sm:alert-horizontal"
                            prop.children [
                                Html.i [
                                    prop.className "swt:iconify swt:fluent--error-circle-20-regular swt:size-8"
                                ]
                                Html.div [
                                    Html.h3 [ prop.className "swt:font-bold"; prop.text "Error" ]
                                    Html.div [ prop.className "swt:text-xs"; prop.text message ]
                                ]
                                Html.button [
                                    prop.className "swt:btn swt:btn-sm"
                                    prop.text "Retry"
                                    prop.onClick (fun _ -> setRetry (retry + 1))
                                ]
                            ]
                        ]
                    | None ->
                        match current with
                        | None -> Html.p "No published releases found."
                        | Some release ->
                            Html.select [
                                prop.name "release-version-selector"
                                prop.className "swt:select swt:select-bordered swt:shrink-0"
                                prop.ariaLabel "Release version"
                                prop.value (string selectedIndex)
                                prop.onChange (fun (value: string) -> setSelectedIndex (int value))
                                prop.children [
                                    for index, release in Array.indexed releases do
                                        Html.option [
                                            prop.key index
                                            prop.value (string index)
                                            prop.text release.tag_name
                                        ]
                                ]
                            ]

                            Changelog.ReleaseNotes(
                                release.body,
                                Html.nav [
                                    prop.className "swt:flex swt:gap-2 swt:items-center"
                                    prop.ariaLabel "Changelog pagination"
                                    prop.children [
                                        Html.button [
                                            prop.type'.button
                                            prop.className "swt:btn swt:btn-sm"
                                            prop.text "Previous"
                                            prop.disabled (selectedIndex <= 0)
                                            prop.onClick (fun _ -> setSelectedIndex (max 0 (selectedIndex - 1)))
                                        ]
                                        Html.span $"{selectedIndex + 1} / {releases.Length}"
                                        Html.button [
                                            prop.type'.button
                                            prop.className "swt:btn swt:btn-sm"
                                            prop.text "Next"
                                            prop.disabled (selectedIndex >= releases.Length - 1)
                                            prop.onClick (fun _ ->
                                                setSelectedIndex (min (releases.Length - 1) (selectedIndex + 1))
                                            )
                                        ]
                                    ]
                                ]
                            )
            ]
        ]
