namespace Swate.Components.Composite.Template

open Fable.Core
open Feliz
open ARCtrl
open Swate.Components
open Swate.Components.Composite.Template.Context
open Swate.Components.Primitive
open Swate.Components.Primitive.ComboBox
open Swate.Components.Primitive.Select
open Swate.Components.Primitive.Select.Types
open Swate.Components.Composite.Template.Types

module private TemplateMocks =

    let mkStella () =
        ARCtrl.Person(
            firstName = "Riley",
            lastName = "Morgan",
            email = "riley.morgan@samplemail.com",
            orcid = "0000-0002-1825-0097"
        )

    let mkDominik () =
        ARCtrl.Person(
            firstName = "Jordan",
            lastName = "Avery",
            email = "jordan.avery@webmail.org",
            orcid = "0000-0002-1825-0098"
        )

    let mkMax () =
        ARCtrl.Person(
            firstName = "Max",
            lastName = "Mustermann",
            email = "max.mustermann@example.com",
            orcid = "0000-0002-1825-0099"
        )

    let mkLisa () =
        ARCtrl.Person(
            firstName = "Lisa",
            lastName = "Müller",
            email = "lisa.mueller@example.com",
            orcid = "0000-0002-1825-0100"
        )

    let mkTemplates () = [|

        ARCtrl.Template.create (
            id = System.Guid.NewGuid(),
            name = "Template 1",
            description = "This is the first template.",
            organisation = Organisation.Other "Custom Org",
            table = ARCtrl.ArcTable.init (name = "Table 1"),
            version = "1.0.0",
            tags =
                ResizeArray<OntologyAnnotation>(
                    [
                        OntologyAnnotation.create ("tag1", "t", "t:00001")
                        OntologyAnnotation.create ("tag2", "t", "t:00002")
                        OntologyAnnotation.create ("tag3", "t", "t:00003")
                        OntologyAnnotation.create ("tag4", "t", "t:00004")
                    ]
                ),
            repos =
                ResizeArray<OntologyAnnotation>(
                    [
                        OntologyAnnotation.create ("repo1", "r", "r:00001")
                        OntologyAnnotation.create ("repo2", "r", "r:00002")
                        OntologyAnnotation.create ("repo3", "r", "r:00003")
                        OntologyAnnotation.create ("repo4", "r", "r:00004")
                    ]
                ),
            authors = ResizeArray<Person>([| mkStella (); mkDominik () |])
        )

        ARCtrl.Template.create (
            id = System.Guid.NewGuid(),
            name = "Template 2",
            description = "This is the second template.",
            organisation = Organisation.Other "Another Org",
            table = ARCtrl.ArcTable.init (name = "Table 2"),
            version = "1.0.1",
            tags =
                ResizeArray<OntologyAnnotation>(
                    [
                        OntologyAnnotation.create ("tagA", "t", "t:00005")
                        OntologyAnnotation.create ("tagB", "t", "t:00006")
                    ]
                ),
            repos =
                ResizeArray<OntologyAnnotation>(
                    [
                        OntologyAnnotation.create ("repoA", "r", "r:00005")
                        OntologyAnnotation.create ("repoB", "r", "r:00006")
                    ]
                ),
            authors = ResizeArray<Person>([| mkStella (); mkLisa () |])
        )

        ARCtrl.Template.create (
            id = System.Guid.NewGuid(),
            name = "Template 3",
            description = "This is the third template.",
            organisation = Organisation.DataPLANT,
            table = ARCtrl.ArcTable.init (name = "Table 3"),
            version = "2.0.0",
            tags =
                ResizeArray<OntologyAnnotation>(
                    [
                        OntologyAnnotation.create ("tagX", "t", "t:00007")
                        OntologyAnnotation.create ("tagY", "t", "t:00008")
                    ]
                ),
            repos =
                ResizeArray<OntologyAnnotation>(
                    [
                        OntologyAnnotation.create ("repoX", "r", "r:00007")
                        OntologyAnnotation.create ("repoY", "r", "r:00008")
                    ]
                ),
            authors = ResizeArray<Person>([| mkDominik (); mkLisa (); mkMax () |])
        )

        ARCtrl.Template.create (
            id = System.Guid.NewGuid(),
            name = "Template 4",
            description = "This is the fourth template.",
            organisation = Organisation.Other "Custom Org",
            table = ARCtrl.ArcTable.init (name = "Table 4"),
            version = "1.2.0",
            tags =
                ResizeArray<OntologyAnnotation>(
                    [
                        OntologyAnnotation.create ("tagAlpha", "t", "t:00009")
                        OntologyAnnotation.create ("tagBeta", "t", "t:00010")
                    ]
                ),
            repos =
                ResizeArray<OntologyAnnotation>(
                    [
                        OntologyAnnotation.create ("repoAlpha", "r", "r:00009")
                        OntologyAnnotation.create ("repoBeta", "r", "r:00010")
                    ]
                ),
            authors = ResizeArray<Person>([| mkStella (); mkDominik () |])
        )

        ARCtrl.Template.create (
            id = System.Guid.NewGuid(),
            name = "Template 5",
            description = "This is the fifth template.",
            organisation = Organisation.DataPLANT,
            table = ARCtrl.ArcTable.init (name = "Table 5"),
            version = "3.0.0",
            tags =
                ResizeArray<OntologyAnnotation>(
                    [
                        OntologyAnnotation.create ("tagGamma", "t", "t:00011")
                        OntologyAnnotation.create ("tagDelta", "t", "t:00012")
                    ]
                ),
            repos =
                ResizeArray<OntologyAnnotation>(
                    [
                        OntologyAnnotation.create ("repoGamma", "r", "r:00011")
                        OntologyAnnotation.create ("repoDelta", "r", "r:00012")
                    ]
                ),
            authors = ResizeArray<Person>([| mkDominik () |])
        )

        ARCtrl.Template.create (
            id = System.Guid.NewGuid(),
            name = "Template 6",
            description = "This is the sixth template.",
            organisation = Organisation.Other "Custom Org",
            table = ARCtrl.ArcTable.init (name = "Table 6"),
            version = "1.3.0",
            tags =
                ResizeArray<OntologyAnnotation>(
                    [
                        OntologyAnnotation.create ("tagEpsilon", "t", "t:00013")
                        OntologyAnnotation.create ("tagZeta", "t", "t:00014")
                    ]
                ),
            repos =
                ResizeArray<OntologyAnnotation>(
                    [
                        OntologyAnnotation.create ("repoEpsilon", "r", "r:00013")
                        OntologyAnnotation.create ("repoZeta", "r", "r:00014")
                    ]
                ),
            authors = ResizeArray<Person>([| mkLisa () |])
        )
    |]

module private TemplateFilterAux =

    open System

    let mkFullAuthorName (author: ARCtrl.Person) =
        [ author.FirstName; author.LastName; author.MidInitials ]
        |> List.choose id
        |> String.concat " "

    let mkFilterTokens (templates: Template[]) (organisations: Organisation[]) =
        let ra = ResizeArray<FilterToken>()

        let templates = // shadow templates with only templates filtered for selected orgs
            templates |> Array.filter (fun t -> Array.contains t.Organisation organisations)

        let tempTokens =
            templates
            |> Array.collect (fun template ->
                let authors: FilterToken seq =
                    template.Authors
                    |> Seq.collect (fun author ->
                        let fullname = mkFullAuthorName author

                        [
                            {|
                                Type = FilterTokenType.Author
                                NameText = fullname
                                Id = string FilterTokenType.Author + fullname
                                Payload = Some author
                            |}
                            if author.ORCID.IsSome then
                                {|
                                    Type = FilterTokenType.ORCID
                                    NameText = author.ORCID.Value
                                    Id = string FilterTokenType.ORCID + author.ORCID.Value
                                    Payload = Some author
                                |}
                        ]
                    )

                let tags =
                    templates
                    |> Seq.collect (fun template ->
                        template.Tags
                        |> Seq.map (fun tag ->
                            unbox<FilterToken> {|
                                Type = FilterTokenType.Tag
                                NameText = tag.NameText
                                Id = string FilterTokenType.Tag + tag.NameText
                                Payload = Some tag
                            |}
                        )
                    )

                let erTags =
                    templates
                    |> Seq.collect (fun template ->
                        template.EndpointRepositories
                        |> Seq.map (fun repo ->
                            unbox<FilterToken> {|
                                Type = FilterTokenType.Repository
                                NameText = repo.NameText
                                Id = string FilterTokenType.Repository + repo.NameText
                                Payload = Some repo
                            |}
                        )
                    )

                [|
                    unbox<FilterToken> {|
                        Type = FilterTokenType.Name
                        NameText = template.Name
                        Id = string FilterTokenType.Name + template.Name
                        Payload = Some template.Name
                    |}
                    if Seq.length authors > 0 then
                        yield! authors
                    if Seq.length tags > 0 then
                        yield! tags
                    if Seq.length erTags > 0 then
                        yield! erTags
                |]
            )
            |> Array.distinctBy (fun token -> token.Id)
            |> Array.filter (fun x -> System.String.IsNullOrWhiteSpace x.NameText |> not)

        ra.AddRange(tempTokens)
        ra.Sort(fun a b -> String.Compare(a.NameText, b.NameText, StringComparison.OrdinalIgnoreCase))
        ra

    let filter =
        fun (templates: Template[]) (selectedOrgs: Organisation[]) (filterTokens: ResizeArray<FilterToken>) ->
            templates
            |> Array.filter (fun template ->
                let orgMatch =
                    if selectedOrgs.Length = 0 then
                        false
                    else
                        selectedOrgs |> Array.exists (fun org -> template.Organisation = org)

                let tokenMatch =
                    if filterTokens.Count = 0 then
                        true
                    elif orgMatch = false then
                        false
                    else
                        filterTokens
                        |> Seq.forall (fun token ->
                            match token.Type with
                            | FilterTokenType.Tag ->
                                template.Tags
                                |> Seq.exists (fun tag -> tag.NameText.ToLower().Contains(token.NameText.ToLower()))
                            | FilterTokenType.Repository ->
                                template.EndpointRepositories
                                |> Seq.exists (fun repo -> repo.NameText.ToLower().Contains(token.NameText.ToLower()))
                            | FilterTokenType.Name -> template.Name.ToLower().Contains(token.NameText.ToLower())
                            | FilterTokenType.Author ->
                                template.Authors
                                |> Seq.exists (fun author -> (mkFullAuthorName author) = token.NameText)
                            | FilterTokenType.ORCID ->
                                template.Authors
                                |> Seq.exists (fun author -> author.ORCID = Some token.NameText)
                        )

                orgMatch && tokenMatch
            )

[<Erase; Mangle(false)>]
type TemplateFilter =

    static member TokenBadge(token: FilterToken, remove: FilterToken -> unit, ?key: obj) =
        Html.div [
            prop.className "swt:h-(--size) swt:flex swt:items-center"
            prop.children [
                Html.div [
                    prop.key (token.Id)
                    prop.className [
                        "swt:badge swt:flex swt:items-center swt:gap-2 swt:bg-base-content/50 swt:text-base-300"
                    ]
                    prop.children [
                        Html.div token.NameText
                        Html.button [
                            prop.className "swt:btn swt:btn-ghost swt:btn-circle swt:btn-xs"
                            prop.onClick (fun _ -> remove (token))
                            prop.children [ Icons.Close() ]
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member TemplateSearch
        (
            availableTokens: ResizeArray<FilterToken>,
            tokens: ResizeArray<FilterToken>,
            setTokens: (ResizeArray<FilterToken> -> ResizeArray<FilterToken>) -> unit,
            ?className: string,
            ?key: obj
        ) =

        let inputValue, setInputValue = React.useState ""

        let searchFn =
            fun (props: {| item: FilterToken; search: string |}) ->
                let str = props.search.ToLower()
                props.item.NameText.ToLower().Contains(str)

        let transformFn = fun (item: FilterToken) -> item.NameText

        let onChangeFn =
            fun (_: int) (item: FilterToken) ->

                setInputValue ("")

                setTokens (fun tokens ->
                    if tokens.Contains item then
                        tokens
                    else
                        let next = ResizeArray(tokens)
                        next.Add item
                        next
                )

        let itemRenderFn =
            fun
                (props:
                    {|
                        index: int
                        isActive: bool
                        item: FilterToken
                        props: ResizeArray<IReactProperty>
                    |}) ->
                Html.li [
                    prop.testId $"template-filter-option-{props.item.Id}"
                    prop.key (props.item.Id)
                    prop.className [
                        "swt:border-l-4 swt:border-transparent swt:list-row swt:rounded-none swt:p-1"
                        if props.isActive then
                            "swt:border-primary! swt:bg-base-content/10"
                    ]
                    prop.children [
                        Html.div [
                            prop.className "swt:flex swt:items-center"
                            prop.children [
                                match props.item.Type with
                                | FilterTokenType.Tag -> Icons.Tag("swt:size-4")
                                | FilterTokenType.Repository -> Icons.CloudUpload("swt:size-4")
                                | FilterTokenType.Name -> Icons.Header("swt:size-4")
                                | FilterTokenType.Author -> Icons.User("swt:size-4")
                                | FilterTokenType.ORCID -> Icons.ORCID("swt:size-4")
                            ]
                        ]
                        Html.div props.item.NameText
                        if props.item.Type = FilterTokenType.ORCID then
                            Html.div [
                                prop.text (TemplateFilterAux.mkFullAuthorName (unbox props.item.Payload: ARCtrl.Person))
                                prop.className "swt:ml-2 swt:text-xs swt:opacity-60"
                            ]
                    ]
                    yield! props.props
                ]

        let InputLeadingBadges =
            tokens
            |> Seq.map (fun token ->
                TemplateFilter.TokenBadge(
                    token,
                    (fun t ->
                        setTokens (fun tokens ->
                            let next = ResizeArray(tokens)
                            next.Remove t |> ignore
                            next
                        )
                    ),
                    key = token.Id
                )
            )
            |> React.Fragment

        let onKeyDown =
            fun (ev: Browser.Types.KeyboardEvent) ->
                if ev.code = kbdEventCode.backspace && inputValue = "" then
                    // Remove the last token when backspace is pressed and input is empty
                    setTokens (fun tokens ->
                        if tokens.Count > 0 then
                            let next = ResizeArray(tokens)
                            next.RemoveAt(next.Count - 1) |> ignore
                            next
                        else
                            tokens
                    )
                else
                    ()

        let availableTokens =
            React.useMemo (
                (fun () ->
                    availableTokens
                    |> Array.ofSeq
                    |> Array.filter (fun token -> tokens.Contains token |> not)
                ),
                [| box availableTokens; box tokens |]
            )

        let labelClasses =
            [
                if className.IsSome then
                    className.Value
                "swt:has-[:focus]:input-primary swt:flex-wrap swt:h-fit swt:min-h-(--size) swt:flex-row swt:w-fit swt:*:min-h-(--size) swt:*:w-auto swt:gap-y-0 swt:gap-x-1"
            ]
            |> String.concat " "

        ComboBox.ComboBox<FilterToken>(
            inputValue,
            setInputValue,
            Array.ofSeq availableTokens,
            searchFn,
            transformFn,
            onChange = onChangeFn,
            itemRenderer = itemRenderFn,
            inputLeadingVisual = InputLeadingBadges,
            props = {|
                ``data-testid`` = "template-filter-search"
            |},
            labelClassName = labelClasses,
            onKeyDown = onKeyDown
        )


    [<ReactComponent>]
    static member OrganisationFilter
        (organisations: Organisation[], selectedIndices: Set<int>, setSelectedIndices: Set<int> -> unit, ?key: obj)
        =

        let communities: SelectItem<ARCtrl.Organisation>[] =
            React.useMemo (
                (fun () -> organisations |> Array.map (fun org -> {| label = org.ToString(); item = org |})),
                [| organisations |]
            )

        let TriggerRenderFn =
            fun _ ->
                Html.button [
                    prop.testId "template-community-filter"
                    prop.title "Filter communities"
                    prop.type'.button
                    prop.tabIndex -1
                    prop.className "swt:btn swt:btn-square swt:btn-neutral swt:pointer-events-none"
                    prop.children [
                        Html.div selectedIndices.Count
                        Icons.Institution(className = "swt:size-4")
                    ]
                ]

        MultiSelect.MultiSelect(
            communities,
            selectedIndices,
            setSelectedIndices,
            triggerRenderFn = TriggerRenderFn,
            dropdownPlacement = FloatingUI.Placement.BottomEnd,
            middleware = [|
                FloatingUI.Middleware.flip ()
                FloatingUI.Middleware.offset (10)
            |]
        )

    /// <summary>
    /// This component is used to filter templates by search, community, and tags.
    ///
    /// <param name="templates">The list of templates to filter. This list should not be modified by this component.</param>
    /// <param name="key">An optional key for the component.</param>
    [<ReactComponent(true)>]
    static member TemplateFilter(templates: Template[], ?templateSearchClassName: string, ?key: obj) =

        /// This context is used to provide the filtered templates to the rest of the application
        let filteredTemplatesCtx = useFilteredTemplateCtx ()

        /// This constant is used to display available tags in the combo box
        let availableCommunities: Organisation[] =
            React.useMemo (
                (fun () ->
                    templates
                    |> Array.map (fun template -> template.Organisation)
                    |> Array.distinct
                    |> Array.sortBy (fun org -> org.IsOfficial() |> not, org.ToString())
                ),
                [| templates |]
            )

        let dataplantIndex =
            availableCommunities |> Array.tryFindIndex (fun org -> org.IsOfficial())

        let selectedOrgIndices, setSelectedOrgIndices =
            React.useState (
                Set [
                    if dataplantIndex.IsSome then
                        dataplantIndex.Value
                ]
            )

        let orgs =
            selectedOrgIndices |> Seq.map (fun i -> availableCommunities.[i]) |> Array.ofSeq

        let selectedOrgsString =
            orgs |> Array.map (fun org -> org.ToString()) |> String.concat ", "

        let availableTokens =
            React.useMemo (
                (fun () -> TemplateFilterAux.mkFilterTokens templates orgs),
                [| box templates; box selectedOrgsString |]
            )

        /// This constant is used to display available communities in the community filter

        let tokens, setTokens = React.useStateWithUpdater (ResizeArray<FilterToken>())


        let filter =
            fun (templates: Template[]) ->

                TemplateFilterAux.filter templates orgs tokens

        let hasInilizalized = React.useRef (false)

        //DataPLANT shall be set as the first available organization to select templates from.
        //Problem is, due to lazy loading in the beginning no organizations could be available.
        //In order to prevent an empty template selection, DataPLANT is set when the organization have been loaded.
        //In order to avoid resetting DataPLANT, the variable "hasInitialized" checks whether DataPLANT has been set successfully
        //in the beginning or not and prevent resetting it.
        React.useEffect (
            (fun () ->
                if not hasInilizalized.current && availableCommunities.Length > 0 then
                    let dpIndex =
                        availableCommunities |> Array.tryFindIndex (fun org -> org.IsOfficial())

                    match dpIndex with
                    | Some idx ->
                        setSelectedOrgIndices (selectedOrgIndices.Add idx)
                        hasInilizalized.current <- true
                    | None -> ()
            ),
            [| box availableTokens; box availableCommunities |]
        )

        React.useEffect (
            (fun () ->
                let nextTemplates = filter templates

                filteredTemplatesCtx.setState nextTemplates
            ),
            [| box selectedOrgIndices; box tokens |]
        )

        Html.div [
            prop.className "swt:flex swt:shrink-0 swt:min-w-0 swt:flex-row swt:flex-wrap swt:gap-2"
            prop.children [
                TemplateFilter.TemplateSearch(
                    availableTokens,
                    tokens,
                    setTokens,
                    key = "template-filter",
                    ?className = templateSearchClassName
                )
                TemplateFilter.OrganisationFilter(
                    availableCommunities,
                    selectedOrgIndices,
                    setSelectedOrgIndices,
                    key = "community-filter"
                )
            ]
        ]

    [<ReactComponent>]
    static member TemplateFilterProvider(children: ReactElement) =
        let filteredTemplates, setFilteredTemplatesFn = React.useState ([||])

        FilteredTemplateCtx.Provider(
            {
                state = filteredTemplates
                setState = setFilteredTemplatesFn
            },
            children
        )

    [<ReactComponent>]
    static member FilteredTemplateRenderer(children: Template[] -> ReactElement) =
        let filteredTemplatesCtx = useFilteredTemplateCtx ()

        let templates = filteredTemplatesCtx.state

        children templates

    [<ReactComponent>]
    static member Entry() =

        let templates, _ = React.useState (TemplateMocks.mkTemplates)

        TemplateFilter.TemplateFilterProvider(
            React.Fragment [
                TemplateFilter.TemplateFilter(templates, key = "template-filter-provider")
                TemplateFilter.FilteredTemplateRenderer(fun templates ->
                    Html.div [
                        prop.text (sprintf "%d templates found" templates.Length)
                    ]
                )
            ]
        )
