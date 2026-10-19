namespace Swate.Components.Composite.TermSearch

open Swate.Components.Shared
open Swate.Components
open Swate.Components.Composite.TermSearch.Types
open Swate.Components.Primitive
open Swate.Components.Primitive.ComboBox
open Swate.Components.Primitive.ComboBox.Types
open Swate.Components.Primitive.BaseModal
open Fable.Core
open Feliz
open Swate.Components.Composite.TermSearch.Context.ConfigContext


module private APIExtentions =

    let private optionOfString (str: string) =
        Option.whereNot System.String.IsNullOrWhiteSpace str

    type Swate.Components.Shared.Database.Term with
        member this.ToComponentTerm() =
            Term(
                ?name = optionOfString this.Name,
                ?id = optionOfString this.Accession,
                ?description = optionOfString this.Description,
                ?source = optionOfString this.FK_Ontology,
                isObsolete = this.IsObsolete
            )

open APIExtentions
open Browser.Types


[<StringEnum; RequireQualifiedAccess>]
type ModalPage =
    | Details
    | Config
    | AdvancedSearch

type private TermSearchResult = {
    Term: Term
    IsDirectedSearchResult: bool
} with

    static member addSearchResults
        (prevResults: ResizeArray<TermSearchResult>)
        (newResults: ResizeArray<TermSearchResult>)
        =
        for newResult in newResults do
            // check if new result is already in the list by id
            let index = prevResults.FindIndex(fun x -> x.Term.id = newResult.Term.id)
            // if it exists and the newResult is result of directedSearch, we update the item, priority on the new
            // Directed search normally takes longer to complete but is additional information
            // so we update non-directed search results with the directed search results
            if index >= 0 then
                match prevResults.[index], newResult with
                | {
                      IsDirectedSearchResult = false
                      Term = t1
                  },
                  {
                      IsDirectedSearchResult = true
                      Term = t2
                  } ->
                    prevResults.[index] <- {
                        IsDirectedSearchResult = true
                        Term = Term.joinLeft t2 t1
                    }
                | {
                      IsDirectedSearchResult = true
                      Term = t1
                  },
                  {
                      IsDirectedSearchResult = false
                      Term = t2
                  } ->
                    prevResults.[index] <- {
                        IsDirectedSearchResult = true
                        Term = Term.joinLeft t1 t2
                    }
                | {
                      IsDirectedSearchResult = false
                      Term = t1
                  },
                  {
                      IsDirectedSearchResult = false
                      Term = t2
                  } ->
                    prevResults.[index] <- {
                        IsDirectedSearchResult = false
                        Term = Term.joinLeft t1 t2
                    }
                | {
                      IsDirectedSearchResult = true
                      Term = t1
                  },
                  {
                      IsDirectedSearchResult = true
                      Term = t2
                  } ->
                    prevResults.[index] <- {
                        IsDirectedSearchResult = true
                        Term = Term.joinLeft t1 t2
                    }
            else
                prevResults.Add(newResult)

        ResizeArray(prevResults)


type private SearchState =
    | Idle
    | SearchDone of ResizeArray<TermSearchResult>

    static member init() = SearchState.Idle

    member this.Results =
        match this with
        | SearchDone results -> results
        | _ -> ResizeArray()

module private TermSearchHelper =

    let (|SearchOngoing|SearchIsDoneEmpty|SearchIsDone|) (state: SearchState) =
        match state with
        | SearchState.Idle -> SearchOngoing
        | SearchState.SearchDone res when res.Count = 0 -> SearchIsDoneEmpty
        | SearchState.SearchDone res -> SearchIsDone res

    [<Global>]
    let inline structuredClone (obj: 'a) : 'a = jsNative

open TermSearchHelper

module private API =

    let callSearch =
        fun (query: string) ->
            Api.SwateApi.SwateApi.searchTerm (Swate.Components.Shared.DTOs.TermQuery.create (query, 10))
            |> Async.StartAsPromise
            |> Promise.map (fun results -> results |> Array.map (fun t0 -> t0.ToComponentTerm()) |> ResizeArray)
            |> Promise.catch (fun exn ->
                console.error $"Error in callSearch: {exn.Message}"
                ResizeArray()
            )

    let callParentSearch =
        fun (parent: string, query: string) ->
            Api.SwateApi.SwateApi.searchTerm (
                Swate.Components.Shared.DTOs.TermQuery.create (query, 10, parentTermId = parent)
            )
            |> Async.StartAsPromise
            |> Promise.map (fun results -> results |> Array.map (fun t0 -> t0.ToComponentTerm()) |> ResizeArray)
            |> Promise.catch (fun exn ->
                console.error $"Error in callParentSearch: {exn.Message}"
                ResizeArray()
            )

    let callAllChildSearch =
        fun (parent: string) ->
            Api.SwateApi.SwateApi.searchChildTerms (Swate.Components.Shared.DTOs.ParentTermQuery.create (parent, 300))
            |> Async.StartAsPromise
            |> Promise.map (fun results -> results.results |> Array.map (fun t0 -> t0.ToComponentTerm()) |> ResizeArray)
            |> Promise.catch (fun exn ->
                console.error $"Error in callAllChildSearch: {exn.Message}"
                ResizeArray()
            )

    let callAdvancedSearch =
        fun dto ->
            Api.SwateApi.SwateApi.searchTermAdvanced dto
            |> Async.StartAsPromise
            |> Promise.map (fun results -> results |> Array.map (fun t0 -> t0.ToComponentTerm()) |> ResizeArray)
            |> Promise.catch (fun exn ->
                console.error $"Error in callAdvancedSearch: {exn.Message}"
                ResizeArray()
            )

[<Mangle(false); Erase>]
type TermSearch =

    [<ReactComponent>]
    static member private TermItem
        (item: TermSearchResult, index: int, isActive: bool, props: ResizeArray<IReactProperty>, ?key: obj)
        =

        let isObsolete = item.Term.isObsolete.IsSome && item.Term.isObsolete.Value

        let isDirectedSearch = item.IsDirectedSearchResult

        let predictiveHref =
            match item.Term.href with
            | Some link when not (System.String.IsNullOrWhiteSpace link) -> Some link
            | _ when item.Term.id.IsSome ->
                match ARCtrl.Helper.Regex.tryParseTermAnnotation item.Term.id.Value with
                | Some r -> ARCtrl.OntologyAnnotation.createUriAnnotation r.IDSpace r.LocalID |> Some
                | None -> None
            | _ -> None

        Html.li [
            prop.testId $"term-search-result-{item.Term.id |> Option.defaultValue (string index)}"
            prop.key index
            prop.className [
                "swt:list-row swt:rounded-none swt:p-1 swt:cursor-pointer"
                if isActive then
                    "swt:bg-primary/10"
            ]
            prop.children [

                Html.div [
                    if isObsolete then
                        prop.title "Obsolete"
                    elif isDirectedSearch then
                        prop.title "Directed Search"
                    prop.className [
                        "swt:w-5 swt:flex swt:items-center swt:justify-center"
                        if isObsolete then
                            "swt:text-error"
                        elif isDirectedSearch then
                            "swt:text-primary"
                    ]
                    prop.children [
                        if isObsolete then
                            Icons.LinkSlash()
                        elif isDirectedSearch then
                            Icons.DiagramProject()
                    ]
                ]
                Html.div [
                    prop.className "swt:font-bold"
                    prop.text (item.Term.name |> Option.defaultValue "<no-name>")
                ]
                if item.Term.description.IsSome then
                    Html.div [
                        prop.className "swt:list-col-wrap swt:text-xs swt:text-muted"
                        prop.children [ Html.text (item.Term.description.Value) ]
                    ]

                Html.div [
                    match predictiveHref with
                    | Some href ->
                        Html.a [
                            prop.onClick (fun e -> e.stopPropagation ())
                            prop.onMouseDown (fun e -> e.stopPropagation ())
                            prop.href href
                            prop.target.blank
                            prop.className "swt:link swt:hover:link-accent"
                            prop.text (item.Term.id |> Option.defaultValue "<no-id>")
                        ]
                    | None ->
                        Html.div [
                            prop.className "swt:text-muted"
                            prop.text (item.Term.id |> Option.defaultValue "<no-id>")
                        ]

                ]
            ]
            yield! props
        ]

    static member private NoResultsElement(advancedSearchToggle: (unit -> unit) option) =
        Html.div [
            prop.className "swt:gap-y-2 swt:py-2 swt:px-4"
            prop.children [
                Html.div "No terms found matching your input."
                if advancedSearchToggle.IsSome then
                    Html.div [
                        prop.children [
                            Html.span "Can't find the term you are looking for? "
                            Html.a [
                                prop.className "swt:link swt:link-primary"
                                prop.onClick (fun e ->
                                    e.preventDefault ()
                                    e.stopPropagation ()
                                    advancedSearchToggle.Value()
                                )
                                prop.text "Try Advanced Search!"
                            ]
                        ]
                    ]
                Html.div [
                    Html.span "Can't find what you need? Get in "
                    Html.a [
                        prop.href @"https://github.com/nfdi4plants/nfdi4plants_ontology/issues/new/choose"
                        prop.target.blank
                        prop.text "contact"
                        prop.className "swt:link swt:link-primary"
                    ]
                    Html.span " with us!"
                ]
            ]
        ]

    [<ReactComponent>]
    static member private AdvancedSearchDefault
        (setSearchResults: ResizeArray<Term> -> unit, ref: IRefValue<unit -> JS.Promise<ResizeArray<Term>>>)
        =

        let advancedSearchState, setAdvancedSearchState =
            React.useState (Swate.Components.Shared.DTOs.AdvancedSearchQuery.init)

        let startSearch =
            fun () ->
                API.callAdvancedSearch advancedSearchState
                |> Promise.iter (fun results -> setSearchResults (results))

        React.useImperativeHandle (ref, fun () -> (fun () -> API.callAdvancedSearch advancedSearchState))

        React.Fragment [
            Html.div [
                prop.className "swt:text-xs swt:text-base-content/50"
                prop.children [
                    Html.p "Use the following fields to search for terms."
                    Html.p [
                        Html.text "Name and Description fields follow Apache Lucene query syntax. "
                        Html.a [
                            prop.href @"https://lucene.apache.org/core/2_9_4/queryparsersyntax.html"
                            prop.target.blank
                            prop.text "Learn more!"
                            prop.className "swt:text-xs swt:link-primary"
                        ]
                    ]

                ]
            ]
            Html.fieldSet [
                prop.className "swt:fieldset swt:px-1"
                prop.children [
                    Html.label [ prop.className "swt:label"; prop.text "Term Name" ]
                    Html.input [
                        prop.testid "advanced-search-term-name-input"
                        prop.className "swt:input"
                        prop.type'.text
                        prop.autoFocus true //Due to react strict mode we render double -> could lead to losing focus
                        prop.value advancedSearchState.TermName
                        prop.onChange (fun e ->
                            setAdvancedSearchState {
                                advancedSearchState with
                                    TermName = e
                            }
                        )
                        prop.onKeyDown (key.enter, fun _ -> startSearch ())
                    ]
                    Html.label [ prop.text "Term Description"; prop.className "swt:label" ]
                    Html.input [
                        prop.testid "advanced-search-term-description-input"
                        prop.className "swt:input"
                        prop.type'.text
                        prop.value advancedSearchState.TermDefinition
                        prop.onChange (fun e ->
                            setAdvancedSearchState {
                                advancedSearchState with
                                    TermDefinition = e
                            }
                        )
                        prop.onKeyDown (key.enter, fun _ -> startSearch ())
                    ]
                    Html.label [
                        prop.className "swt:label"
                        prop.children [
                            Html.input [
                                prop.className "swt:checkbox"
                                prop.type'.checkbox
                                prop.isChecked advancedSearchState.KeepObsolete
                                prop.onChange (fun e ->
                                    setAdvancedSearchState {
                                        advancedSearchState with
                                            KeepObsolete = e
                                    }
                                )
                            ]
                            Html.text "Keep Obsolete"
                        ]
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member private ModalAdvancedSearchContent
        (
            advancedSearch: (AdvancedSearchOptions -> ReactElement) option,
            searchResults: SearchState,
            setSearchResults: ResizeArray<Term> -> unit,
            onTermSelect: Term option -> unit,
            formRef
        ) =
        let pagination, setPagination = React.useState (0)


        let AdvancedSearchForm =
            React.useMemo (
                (fun () ->
                    match advancedSearch with
                    | Some custom -> custom (AdvancedSearchOptions(setSearchResults, formRef))
                    | None -> TermSearch.AdvancedSearchDefault(setSearchResults, formRef)

                ),
                [| box advancedSearch |]
            )

        let BinSize = 20

        let BinCount =
            React.useMemo ((fun () -> searchResults.Results.Count / BinSize), [| box searchResults |])

        let inputRef = React.useInputRef ()

        // Ensure that clicking on "Next"/"Previous" button will update the pagination input field
        let setPagination =
            fun (x: int) ->
                setPagination x

                if inputRef.current.IsSome then
                    inputRef.current.Value.valueAsNumber <- float (x + 1)

        let ResultsComponent (results: ResizeArray<TermSearchResult>) =
            let range = results.GetRange(pagination * BinSize, BinSize)

            React.Fragment [
                Html.div [ Html.textf "Results: %i" results.Count ]
                Html.ul [
                    prop.className "swt:max-h-[50%] swt:overflow-y-auto swt:list"
                    prop.children [
                        for i in 0 .. range.Count - 1 do
                            let res = range.[i]
                            let props = ResizeArray([ prop.onClick (fun _ -> onTermSelect (Some res.Term)) ])
                            TermSearch.TermItem(res, i, false, props, key = i)
                    ]
                ]
                if BinCount > 1 then
                    Html.div [
                        prop.className "swt:join"
                        prop.children [
                            Html.label [
                                prop.className "swt:input swt:join-item"
                                prop.children [
                                    Html.input [
                                        prop.ref inputRef
                                        prop.type'.number
                                        prop.min 1
                                        prop.defaultValue (pagination + 1)
                                        prop.max (BinCount + 1)
                                        prop.onChange (fun (e: int) ->
                                            let e = e - 1 //Convert to zero based index
                                            System.Math.Min(System.Math.Max(e, 0), BinCount) |> setPagination
                                        )
                                    ]
                                    Html.span ($"/{BinCount + 1}")
                                ]
                            ]
                            Html.button [
                                let disabled = pagination <= 1
                                prop.className "swt:btn swt:join-item"
                                prop.disabled disabled

                                prop.onClick (fun _ -> pagination - 1 |> setPagination)

                                prop.text "Previous"
                            ]
                            Html.button [
                                let disabled = pagination >= BinCount
                                prop.disabled disabled
                                prop.className "swt:btn swt:join-item"

                                prop.onClick (fun _ -> pagination + 1 |> setPagination)

                                prop.text "Next"
                            ]
                        ]
                    ]
            ]

        match searchResults with
        | SearchDone results -> ResultsComponent results
        | _ -> AdvancedSearchForm

    [<ReactComponent>]
    static member ModalDetails
        (
            tempTerm: Term option,
            setTempTerm,
            term: Term option,
            ?tempValue: string,
            ?setTempValue: string -> unit,
            ?value: string
        ) =
        let Label (str: string) =
            Html.label [ prop.className "swt:label"; prop.text str ]

        let Input (v: string option, source: string option, setter: string -> unit) =
            Html.input [
                prop.className [
                    "swt:input swt:w-full"
                    if source <> v then
                        "swt:input-primary"
                ]
                prop.defaultValue (Option.defaultValue "" v)
                prop.onChange setter
            ]

        let tempTerm = tempTerm |> Option.defaultValue (Term())
        let term = term |> Option.defaultValue (Term())
        let tempValue = tempValue |> Option.defaultValue ("")

        Html.fieldSet [
            prop.className "swt:fieldset swt:p-2"
            prop.children [
                if value.IsSome && setTempValue.IsSome then
                    Label "Value"

                    Input(
                        Some tempValue,
                        value,
                        fun (e: string) ->
                            let nextValue = structuredClone (e)
                            setTempValue.Value nextValue
                    )

                    Label "Unit"

                    Input(
                        tempTerm.name,
                        term.name,
                        fun (e: string) ->
                            let nextTerm = structuredClone (tempTerm)
                            nextTerm.name <- Option.whereNot System.String.IsNullOrWhiteSpace e
                            setTempTerm (Some nextTerm)
                    )
                else
                    Label "Name"

                    Input(
                        tempTerm.name,
                        term.name,
                        fun (e: string) ->
                            let nextTerm = structuredClone (tempTerm)
                            nextTerm.name <- Option.whereNot System.String.IsNullOrWhiteSpace e
                            setTempTerm (Some nextTerm)
                    )
                Label "Id"
                Input(
                    tempTerm.id,
                    term.id,
                    fun (e: string) ->
                        let nextTerm = structuredClone (tempTerm)
                        nextTerm.id <- Option.whereNot System.String.IsNullOrWhiteSpace e
                        setTempTerm (Some nextTerm)
                )
                Label "Description"
                Input(
                    tempTerm.description,
                    term.description,
                    fun (e: string) ->
                        let nextTerm = structuredClone (tempTerm)
                        nextTerm.description <- Option.whereNot System.String.IsNullOrWhiteSpace e
                        setTempTerm (Some nextTerm)
                )
                Label "Source"
                Input(
                    tempTerm.source,
                    term.source,
                    fun (e: string) ->
                        let nextTerm = structuredClone (tempTerm)
                        nextTerm.source <- Option.whereNot System.String.IsNullOrWhiteSpace e
                        setTempTerm (Some nextTerm)
                )
                Label "Source Link"
                Html.div [
                    prop.className "swt:join"
                    prop.children [
                        Html.input [
                            prop.className [
                                "swt:input swt:w-full swt:join-item"
                                if tempTerm.href <> term.href then
                                    "swt:input-primary"
                            ]
                            prop.defaultValue (Option.defaultValue "" tempTerm.href)
                            prop.onChange (fun (e: string) ->
                                let nextTerm = structuredClone (tempTerm)
                                nextTerm.href <- Option.whereNot System.String.IsNullOrWhiteSpace e
                                setTempTerm (Some nextTerm)
                            )
                        ]
                        Html.a [
                            prop.className "swt:btn swt:btn-info swt:join-item"
                            prop.children [ Icons.ExternalLinkAlt(className = "swt:size-4") ]
                            prop.title "Open external link"
                            prop.disabled (tempTerm.href.IsNone)
                            prop.href (Option.defaultValue "#" tempTerm.href)
                            prop.target.blank
                        ]
                    ]
                ]
                if tempTerm.data.IsSome then
                    Label "Data"

                    Html.pre [
                        prop.className "swt:text-xs"
                        prop.children [
                            Html.code (Fable.Core.JS.JSON.stringify (tempTerm.data.Value, space = '\t'))
                        ]
                    ]
                if tempTerm.isObsolete.IsSome && tempTerm.isObsolete.Value then
                    Html.div [ prop.className "swt:text-error"; prop.text "obsolete" ]
            ]
        ]

    [<ReactComponent>]
    static member private ModalConfig(config: (string * string) list) =

        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-4"
            prop.children [
                for (key, value) in config ->
                    Html.div [
                        prop.className "swt:flex swt:flex-row swt:items-start swt:gap-4"
                        prop.children [
                            Html.label [ prop.className "swt:w-80 swt:font-bold"; prop.text key ]
                            Html.div value
                        ]
                    ]
            ]
        ]

    [<ReactComponent>]
    static member private Modal
        (
            isOpen,
            setIsOpen,
            term: Term option,
            setTerm: Term option -> unit,
            config: (string * string) list,
            ?advancedSearch
        ) =
        let page, setPage = React.useState (ModalPage.Details)
        let tempTerm, setTempTerm = React.useState (term)
        let hasChanges = tempTerm <> term

        let advSearchResults, setAdvSearchResults = React.useState (SearchState.init)

        React.useEffect ((fun _ -> setTempTerm term), [| box term |])

        let formRef = React.useRef (fun () -> promise { return ResizeArray<Term>() })

        let setAdvSearchResultsByTerm =
            (fun (results: ResizeArray<Term>) ->
                let r =
                    results.ConvertAll(fun t0 -> {
                        Term = t0
                        IsDirectedSearchResult = false
                    })
                    |> SearchState.SearchDone

                setAdvSearchResults r
            )

        let content =
            match page with
            | ModalPage.Details -> TermSearch.ModalDetails(tempTerm, setTempTerm, term)

            | ModalPage.Config -> TermSearch.ModalConfig(config)

            | ModalPage.AdvancedSearch ->
                TermSearch.ModalAdvancedSearchContent(
                    advancedSearch,
                    advSearchResults,
                    setAdvSearchResultsByTerm,
                    setTerm,
                    formRef
                )

        let PageNavigationBtn (targetPage, title: string, icon) =
            let id = (title.ToLower().Replace(" ", "_") + "_btn")

            Html.div [
                prop.role.tab
                prop.title title
                prop.testId id
                prop.key id
                prop.className [
                    "swt:tab"
                    if page = targetPage then
                        "swt:tab-active"
                ]
                prop.onClick (fun _ -> setPage targetPage)
                prop.children [ icon ]
            ]

        let actions =
            Html.div [
                prop.className "swt:w-full swt:flex swt:justify-center"
                prop.children [
                    Html.div [
                        prop.role.tabList
                        prop.className "swt:tabs swt:tabs-box swt:tabs-sm"
                        prop.children [
                            PageNavigationBtn(ModalPage.Details, "Show term details", Icons.Info("swt:size-4"))
                            PageNavigationBtn(
                                ModalPage.AdvancedSearch,
                                "Advanced Search",
                                Icons.SearchPlus("swt:size-4")
                            )
                            if config.Length > 0 then
                                PageNavigationBtn(
                                    ModalPage.Config,
                                    "Show term search settings",
                                    Icons.Cog("swt:size-4")
                                )
                        ]
                    ]
                ]
            ]

        let footer =
            React.Fragment [
                Html.button [
                    prop.text "Close"
                    prop.className "swt:btn"
                    prop.onClick (fun _ -> setIsOpen false)
                ]
                match page with
                | ModalPage.AdvancedSearch when advSearchResults = SearchState.Idle ->

                    Html.button [
                        prop.className "swt:btn swt:btn-primary"
                        prop.onClick (fun _ -> formRef.current () |> Promise.iter setAdvSearchResultsByTerm)
                        prop.text "Search"
                    ]
                | ModalPage.AdvancedSearch ->
                    Html.button [
                        prop.className "swt:btn swt:btn-primary"
                        prop.onClick (fun _ -> setAdvSearchResults SearchState.Idle)
                        prop.text "Back"
                    ]
                | ModalPage.Details ->
                    Html.button [
                        prop.text "Submit"
                        prop.className [
                            "swt:btn swt:ml-auto"
                            if hasChanges then
                                "swt:btn-primary"
                        ]
                        prop.disabled (not hasChanges)
                        prop.onClick (fun _ ->
                            if hasChanges then
                                setTerm tempTerm
                        )
                    ]
                | _ -> Html.none
            ]

        BaseModal.Modal(
            isOpen,
            setIsOpen,
            Html.div "Details",
            content,
            modalActions = actions,
            footer = footer,
            debug = "termsearch_details_modal"
        )


    ///
    /// Customizable react component for term search. Utilizing SwateDB search by default.
    //
    // #if SWATE_ENVIRONMENT
    // [<ReactComponent>]
    // #else
    // [<ExportDefaultAttribute; NamedParams>]
    // #endif
    [<ReactComponent(true)>]
    static member TermSearch
        (
            term: Term option,
            onTermChange: Term option -> unit,
            ?parentId: string,
            ?termSearchQueries: ResizeArray<string * SearchCall>,
            ?parentSearchQueries: ResizeArray<string * ParentSearchCall>,
            ?allChildrenSearchQueries: ResizeArray<string * AllChildrenSearchCall>,
            ?advancedSearchRenderer: AdvancedSearchOptions -> ReactElement,
            ?onFocus: Browser.Types.FocusEvent -> unit,
            ?onBlur: Browser.Types.FocusEvent -> unit,
            ?onKeyDown: Browser.Types.KeyboardEvent -> unit,
            ?onTermSelect: Term -> unit,
            ?disableDefaultSearch: bool,
            ?disableDefaultParentSearch: bool,
            ?disableDefaultAllChildrenSearch: bool,
            ?autoFocus: bool,
            ?classNames: TermSearchStyle
        ) =

        let autoFocus = defaultArg autoFocus false

        let (searchResults: SearchState), setSearchResults =
            React.useStateWithUpdater (SearchState.init ())

        /// Set of string ids for each action started. As long as one id is still contained, shows loading spinner
        let (loading: Set<string>), setLoading = React.useStateWithUpdater (Set.empty)
        let isLoading = loading.Count > 0
        let inputRef = React.useInputRef ()
        let cancelled = React.useRef (false)

        let (modalOpen: bool), setModalOpen = React.useState false

        let inputText = term |> Option.bind _.name |> Option.defaultValue ""
        let input, setInput = React.useState (inputText)

        let termSearchConfigCtx = useTermSearchConfigCtx ()

        React.useLayoutEffect (
            (fun () -> term |> Option.bind _.name |> Option.defaultValue "" |> setInput),
            [| box term |]
        )

        /// Close term search result window when opening a modal
        let setModalOpen =
            fun (modalOpen: bool) ->
                if modalOpen then
                    cancelled.current <- true
                    setSearchResults (fun _ -> SearchState.init ())

                setModalOpen modalOpen

        let onBlur =
            onBlur |> Option.map (fun x -> if not modalOpen then x else fun _ -> ())

        let onTermChange =
            fun (term: Term option) ->
                setInput (Option.bind (fun (t: Term) -> t.name) term |> Option.defaultValue "")

                setSearchResults (fun _ -> SearchState.init ())
                onTermChange term

        let onTermSelect =
            fun (term: Term) ->
                if onTermSelect.IsSome then
                    setInput (Option.defaultValue "" term.name)
                    setSearchResults (fun _ -> SearchState.init ())
                    onTermSelect |> Option.iter (fun fn -> fn term)
                else
                    onTermChange (Some term)

        let startLoadingBy =
            fun (key: string) ->
                setLoading (fun l ->
                    let key = "L_" + key
                    l.Add key
                )

        let stopLoadingBy =
            fun (key: string) ->
                setLoading (fun l ->
                    let key = "L_" + key
                    l.Remove key
                )

        let createTermSearch =
            fun (id: string) (search: SearchCall) ->
                let id = "T_" + id

                fun (query: string) -> promise {
                    startLoadingBy id

                    let! termSearchResults =
                        search query
                        |> Promise.catch (fun exn ->
                            stopLoadingBy id
                            console.error $"Error in search {exn.Message}"
                            ResizeArray()
                        )

                    let termSearchResults =
                        termSearchResults.ConvertAll(fun t0 -> {
                            Term = t0
                            IsDirectedSearchResult = false
                        })

                    if not cancelled.current then
                        setSearchResults (fun prevResults ->
                            TermSearchResult.addSearchResults prevResults.Results termSearchResults
                            |> SearchState.SearchDone
                        )

                    stopLoadingBy id
                }

        let createParentChildTermSearch =
            fun (id: string) (search: ParentSearchCall) ->
                let id = "PC_" + id

                fun (parentId: string, query: string) -> promise {
                    startLoadingBy id

                    let! termSearchResults =
                        search (parentId, query)
                        |> Promise.catch (fun exn ->
                            stopLoadingBy id
                            console.error $"Error in parent child search {exn.Message}"
                            ResizeArray()
                        )

                    let termSearchResults =
                        termSearchResults.ConvertAll(fun t0 -> {
                            Term = t0
                            IsDirectedSearchResult = true
                        })

                    if not cancelled.current then
                        setSearchResults (fun prevResults ->
                            TermSearchResult.addSearchResults prevResults.Results termSearchResults
                            |> SearchState.SearchDone
                        )

                    stopLoadingBy id
                }

        let createAllChildTermSearch =
            fun (id: string) (search: AllChildrenSearchCall) ->
                let id = "AC_" + id

                fun (parentId: string) -> promise {
                    startLoadingBy id

                    let! termSearchResults =
                        search parentId
                        |> Promise.catch (fun exn ->
                            stopLoadingBy id
                            console.error $"Error in all child search {exn.Message}"
                            ResizeArray()
                        )

                    let termSearchResults =
                        termSearchResults.ConvertAll(fun t0 -> {
                            Term = t0
                            IsDirectedSearchResult = true
                        })

                    if not cancelled.current then
                        setSearchResults (fun prevResults ->
                            TermSearchResult.addSearchResults prevResults.Results termSearchResults
                            |> SearchState.SearchDone
                        )

                    stopLoadingBy id
                }

        let defaultIsDisabled (local: bool option) =
            //If local is set then use its value, else use the value of provider
            if local.IsSome then
                local.Value
            else
                termSearchConfigCtx.hasProvider && termSearchConfigCtx.disableDefault

        /// Collect all given search functions into one combined search
        let termSearchFunc =
            fun (query: string) ->
                [
                    if defaultIsDisabled (disableDefaultSearch) then
                        ()
                    else
                        createTermSearch "DEFAULT_SIMPLE" API.callSearch query
                    if termSearchQueries.IsSome then
                        for id, termSearch in termSearchQueries.Value do
                            createTermSearch id termSearch query
                    if termSearchConfigCtx.hasProvider then
                        for id, termSearch in termSearchConfigCtx.termSearchQueries do
                            createTermSearch id termSearch query
                ]
                |> Promise.all
                |> Promise.catch (fun exn ->
                    console.error $"Error in termSearchFunc: {exn.Message}"
                    [||]
                )
                |> Promise.start

        let parentSearch =
            fun (query: string) ->
                [
                    if parentId.IsSome then
                        if defaultIsDisabled (disableDefaultParentSearch) then
                            ()
                        else
                            createParentChildTermSearch
                                "DEFAULT_PARENTCHILD"
                                API.callParentSearch
                                (parentId.Value, query)

                        if parentSearchQueries.IsSome then
                            for id, parentSearch in parentSearchQueries.Value do
                                createParentChildTermSearch id parentSearch (parentId.Value, query)

                        if termSearchConfigCtx.hasProvider then
                            for id, parentSearch in termSearchConfigCtx.parentSearchQueries do
                                createParentChildTermSearch id parentSearch (parentId.Value, query)
                ]
                |> Promise.all
                |> Promise.catch (fun exn ->
                    console.error $"Error in parentSearch: {exn.Message}"
                    [||]
                )
                |> Promise.start

        let allChildSearch =
            fun () ->
                [
                    if parentId.IsSome then
                        if defaultIsDisabled (disableDefaultAllChildrenSearch) then
                            ()
                        else
                            createAllChildTermSearch "DEFAULT_ALLCHILD" API.callAllChildSearch parentId.Value

                        if allChildrenSearchQueries.IsSome then
                            for id, allChildSearch in allChildrenSearchQueries.Value do
                                createAllChildTermSearch id allChildSearch parentId.Value

                        if termSearchConfigCtx.hasProvider then
                            for id, allChildSearch in termSearchConfigCtx.allChildrenSearchQueries do
                                createAllChildTermSearch id allChildSearch parentId.Value
                ]
                |> Promise.all
                |> Promise.catch (fun exn ->
                    console.error $"Error in allChildSearch: {exn.Message}"
                    [||]
                )
                |> Promise.start

        let cancelSearch, search =
            let id = "DEFAULT_DEBOUNCE_SIMPLE"
            let startDebounceLoading = fun () -> startLoadingBy id
            let stopDebounceLoading = fun () -> stopLoadingBy id

            React.useDebouncedCallbackWithCancel (
                termSearchFunc,
                500,
                stopDebounceLoading,
                startDebounceLoading,
                stopDebounceLoading
            )

        let cancelParentSearch, parentSearch =
            let id = "DEFAULT_DEBOUNCE_PARENT"
            let startDebounceLoading = fun () -> startLoadingBy id
            let stopDebounceLoading = fun () -> stopLoadingBy id

            React.useDebouncedCallbackWithCancel (
                parentSearch,
                500,
                stopDebounceLoading,
                startDebounceLoading,
                stopDebounceLoading
            )

        let cancelAllChildSearch, allChildSearch =
            React.useDebouncedCallbackWithCancel (allChildSearch, 0)

        let cancel () =
            setSearchResults (fun _ -> SearchState.init ())
            cancelled.current <- true
            setLoading (fun _ -> Set.empty) // without this cancel will await finishing the queries before stopping loading spinner
            cancelSearch ()
            cancelParentSearch ()
            cancelAllChildSearch ()

        let startSearch =
            fun (query: string) ->
                cancelled.current <- false
                setSearchResults (fun _ -> SearchState.init ())
                search query
                parentSearch query

        let startAllChildSearch =
            fun () ->
                cancelled.current <- false
                setSearchResults (fun _ -> SearchState.init ())
                allChildSearch ()

        React.useEffectOnce (fun () ->
            if autoFocus && inputRef.current.IsSome then

                let id = Fable.Core.JS.setTimeout (fun () -> inputRef.current.Value.focus ()) 0

                FsReact.createDisposable (fun () -> Fable.Core.JS.clearTimeout id)
            else
                FsReact.createDisposable (fun () -> ())
        )

        let InputLeadingVisual =
            Html.div [
                prop.className "swt:swap swt:group-focus-within:swap-active swt:cursor-text"
                prop.children [
                    Html.div [
                        prop.className "swt:swap-on"
                        prop.children [
                            Html.kbd [ prop.className [ "swt:kbd swt:kbd-sm" ]; prop.text "F2" ]
                        ]
                    ]
                    Html.div [
                        prop.className "swt:swap-off swt:flex swt:items-center swt:justify-center"
                        prop.children [ Icons.MagnifyingClass("swt:text-primary") ]
                    ]
                ]
            ]

        let isFullTerm =
            term.IsSome
            && term.Value.name.IsSome
            && not (System.String.IsNullOrWhiteSpace term.Value.name.Value)
            && term.Value.id.IsSome
            && not (System.String.IsNullOrWhiteSpace term.Value.id.Value)

        let InputTrailingVisual =
            React.Fragment [
                Html.div [
                    if isLoading then
                        Html.span [
                            prop.className "swt:loading swt:loading-spinner swt:loading-sm"
                        ]
                    else
                        Icons.Check(
                            [
                                "swt:text-primary swt:transition-all swt:size-4 swt:overflow-x-hidden swt:opacity-100"
                                if not isFullTerm then
                                    "swt:!w-0 swt:!opacity-0"
                            ]
                            |> String.concat " "
                        )
                ]
            ]

        let itemRendererFn
            (props:
                {|
                    item: TermSearchResult
                    index: int
                    isActive: bool
                    props: ResizeArray<IReactProperty>
                |})
            =

            TermSearch.TermItem(props.item, props.index, props.isActive, props.props, key = props.index)

        let itemContainerRendererFn
            (props:
                {|
                    props: ResizeArray<IReactProperty>
                    children: ReactElement
                |})
            =
            Html.ul [
                prop.className [
                    "swt:list swt:py-2 swt:z-[99999]"
                    "swt:bg-base-100 swt:shadow-sm swt:rounded-xs"
                    "swt:overflow-y-auto swt:max-h-1/2 swt:lg:max-h-1/3 swt:min-w-md swt:max-w-xl"
                    "swt:border-2 swt:border-base-content/50"
                ]
                prop.children props.children
                yield! props.props
            ]

        let onInputChange =
            fun (e: string) ->
                setInput e

                if System.String.IsNullOrEmpty e then
                    onTermChange None
                    cancel ()
                else
                    onTermChange (Some <| Term(e))
                    startSearch e

        let configDetails =
            let mkCustomSearchQueries (local: ResizeArray<(string * 'a)> option, ctx: ResizeArray<string * 'a>) =
                [
                    if local.IsSome then
                        for (k, _) in local.Value do
                            k
                    if termSearchConfigCtx.hasProvider then
                        for (k, _) in ctx do
                            k
                ]
                |> String.concat "; "
                |> Option.whereNot System.String.IsNullOrWhiteSpace

            [
                "Parent Id", parentId
                "Provider", (if termSearchConfigCtx.hasProvider then Some "Yes" else None)
                "Disable Default Search",
                if defaultIsDisabled (disableDefaultSearch) then
                    Some "Yes"
                else
                    None
                "Disable Default Parent Search",
                if defaultIsDisabled (disableDefaultParentSearch) then
                    Some "Yes"
                else
                    None
                "Disable Default All Children Search",
                if defaultIsDisabled (disableDefaultAllChildrenSearch) then
                    Some "Yes"
                else
                    None
                "Custom Term Search Queries",
                mkCustomSearchQueries (termSearchQueries, termSearchConfigCtx.termSearchQueries)
                "Custom Parent Search Queries",
                mkCustomSearchQueries (parentSearchQueries, termSearchConfigCtx.parentSearchQueries)
                "Custom All Children Search Queries",
                mkCustomSearchQueries (allChildrenSearchQueries, termSearchConfigCtx.allChildrenSearchQueries)
                "Advanced Search",
                (if advancedSearchRenderer.IsSome then
                     Some "Custom"
                 else
                     None)
                "Provider Custom All Children Search Queries",
                Option.map (Seq.map fst >> String.concat "; ") allChildrenSearchQueries

            ]
            |> List.fold
                (fun acc (key, value) ->
                    match value with
                    | Some value -> (key, value) :: acc
                    | _ -> acc
                )
                []
            |> List.rev

        let placeholder =
            if parentId.IsSome then
                "Search children of parent..."
            else
                "Search terms..."

        let comboBoxRef = React.useRef<ComboBoxRef> (unbox None)

        let shallSearchChild =
            fun _ ->
                System.String.IsNullOrWhiteSpace input
                && parentId.IsSome
                && comboBoxRef.current.isOpen () |> not

        React.Fragment [
            TermSearch.Modal(
                modalOpen,
                setModalOpen,
                term,
                onTermChange,
                configDetails,
                ?advancedSearch = advancedSearchRenderer
            )
            ComboBox.ComboBox<TermSearchResult>(
                input,
                onInputChange,
                items = Array.ofSeq searchResults.Results,
                filterFn = (fun x -> true),
                itemToString = (fun x -> x.Term.name |> Option.defaultValue ""),
                loading = (isLoading && searchResults.Results.Count = 0),
                placeholder = placeholder,
                inputLeadingVisual = InputLeadingVisual,
                inputTrailingVisual = InputTrailingVisual,
                itemContainerRenderer = itemContainerRendererFn,
                itemRenderer = itemRendererFn,
                ?labelClassName = (classNames |> Option.map (fun s -> style.resolveStyle s.inputLabel)),
                onChange = (fun _ y -> onTermSelect y.Term),
                onFocus = (fun (e: Browser.Types.FocusEvent) -> onFocus |> Option.iter (fun fn -> fn e)),
                onBlur = (fun (e: Browser.Types.FocusEvent) -> onBlur |> Option.iter (fun fn -> fn e)),
                onOpen = (fun o -> if o then startSearch input else cancel ()),
                comboBoxRef = comboBoxRef,
                onKeyDown =
                    (fun kbe ->
                        match kbe.code with
                        | kbdEventCode.f2 -> setModalOpen true
                        | kbdEventCode.arrowDown when shallSearchChild () -> startAllChildSearch ()
                        | _ -> onKeyDown |> Option.iter (fun fn -> fn kbe)
                    ),
                noResultsRenderer =
                    (fun () ->
                        TermSearch.NoResultsElement(
                            advancedSearchRenderer |> Option.map (fun _ -> fun () -> setModalOpen true)
                        )
                    ),
                props = {|
                    autoFocus = autoFocus
                    ``data-testid`` = "term-search-input"
                    ``data-debugresultcount`` = searchResults.Results.Count
                |},
                onDoubleClick =
                    (fun ev data ->
                        match shallSearchChild () with
                        | true ->
                            data.setIsOpen true
                            startAllChildSearch ()
                        | false ->

                            if System.String.IsNullOrWhiteSpace input |> not then
                                data.setIsOpen true
                                startSearch input
                    )
            )
        ]
