namespace Arcitisa.Components.Composite.TermSearch

open Arcitisa.Components
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Arcitisa.Components.Primitive
open Arcitisa.Components.Composite.TermSearch.Context
open Arcitisa.Components.Composite.TermSearch.Types

module private TermSearchConfigProviderHelper =

    [<Literal>]
    let TIB_PREFIX = "TIB_"

    [<Literal>]
    let TIB_DATAPLANT_COLLECTION_KEY = "DataPLANT"

    let mkTIBQueries (collections: Set<string>) = {|
        TermSearch =
            ResizeArray [
                for c in collections do
                    let n = TIB_PREFIX + c

                    let query: SearchCall =
                        fun (q: string) ->
                            Arcitisa.Components.Api.TIBApi.TIBApi.defaultSearch (q, 10, c)
                            |> Promise.map (fun searchApi ->
                                match searchApi with
                                | Some api -> api.ToMyTerm() |> ResizeArray
                                | None -> ResizeArray()
                            )

                    yield (n, query)
            ]
        ParentSearch =
            ResizeArray [
                for c in collections do
                    let n = TIB_PREFIX + c

                    let query: ParentSearchCall =
                        fun (parent: string, query: string) ->
                            Arcitisa.Components.Api.TIBApi.TIBApi.searchChildrenOf (query, parent, 10, c)
                            |> Promise.map (fun searchApi ->
                                match searchApi with
                                | Some api -> api.ToMyTerm() |> ResizeArray
                                | None -> ResizeArray()
                            )

                    yield (n, query)
            ]
        AllChildrenSearch =
            ResizeArray [
                for c in collections do
                    let n = TIB_PREFIX + c

                    let query: AllChildrenSearchCall =
                        fun (p: string) ->
                            Arcitisa.Components.Api.TIBApi.TIBApi.searchAllChildrenOf (p, 300, collection = c)
                            |> Promise.map (fun searchApi ->
                                match searchApi with
                                | Some api -> api.ToMyTerm() |> ResizeArray
                                | None -> ResizeArray()
                            )

                    yield (n, query)
            ]
    |}

open TermSearchConfigProviderHelper

[<Erase; Mangle(false)>]
type TermSearchConfigProvider =

    [<ReactComponent>]
    static member TIBQueryProvider(children: ReactElement) =

        let allTermSearchQueries, setAllTermSearchQueries =
            React.useState<ResizeArray<string * SearchCall>> (fun () -> ResizeArray())

        let allParentSearchQueries, setAllParentSearchQueries =
            React.useState<ResizeArray<string * ParentSearchCall>> (fun () -> ResizeArray())

        let allAllChildrenSearchQueries, setAllAllChildrenSearchQueries =
            React.useState<ResizeArray<string * AllChildrenSearchCall>> (fun () -> ResizeArray())

        React.useEffect (
            (fun _ -> // get all currently supported catalogues
                promise {
                    try
                        let! collections =
                            Api.TIBApi.TIBApi.getCollections ()
                            |> Promise.catch (fun ex -> failwithf "Error fetching TIB collections: %s" ex.Message)

                        let collectionSet = Set.ofArray collections.content

                        let tibQueries = TermSearchConfigProviderHelper.mkTIBQueries collectionSet
                        setAllTermSearchQueries (ResizeArray tibQueries.TermSearch)
                        setAllParentSearchQueries (ResizeArray tibQueries.ParentSearch)
                        setAllAllChildrenSearchQueries (ResizeArray tibQueries.AllChildrenSearch)
                    with ex ->
                        console.error ("Error fetching TIB collections:", ex)
                }
                |> Promise.start
            ),
            [||]
        )

        TermSearchConfigProvider.TermSearchConfigProvider(
            children,
            allTermSearchQueries,
            allParentSearchQueries,
            allAllChildrenSearchQueries,
            defaultActive = Set [ TIB_PREFIX + TIB_DATAPLANT_COLLECTION_KEY ]
        )


    [<ReactComponent(true)>]
    static member TermSearchConfigProvider
        (
            children: ReactElement,
            allTermSearchQueries: ResizeArray<string * SearchCall>,
            allParentSearchQueries: ResizeArray<string * ParentSearchCall>,
            allAllChildrenSearchQueries: ResizeArray<string * AllChildrenSearchCall>,
            ?defaultActive: Set<string>,
            ?localStorageKey: string
        ) =
        let localStorageKey = defaultArg localStorageKey "arcitisa-termsearchconfig-ctx"
        let defaultActive = defaultArg defaultActive Set.empty

        let (activeKeys: TermSearchActiveKeysContext), setActiveKeys =
            React.useLocalStorage (localStorageKey, TermSearchActiveKeysContext.init (defaultActive))

        // From v1.0.7 to v2.0.0 the field was renamed from `aktiveKeys` to `activeKeys`. This migration code ensures that users who have the old field in their local storage will have it removed and replaced with the new field.
        match activeKeys.activeKeys with
        | null -> Browser.Dom.window.localStorage.removeItem (localStorageKey)
        | _ -> ()

        let allKeys =
            React.useMemo (
                (fun () ->

                    let allKeys =
                        [
                            yield! allTermSearchQueries |> Seq.map fst
                            yield! allParentSearchQueries |> Seq.map fst
                            yield! allAllChildrenSearchQueries |> Seq.map fst
                        ]
                        |> Set.ofList

                    allKeys
                ),
                [|
                    box allTermSearchQueries
                    box allParentSearchQueries
                    box allAllChildrenSearchQueries
                |]
            )

        /// This is used for memoization
        let activeKeysString = activeKeys.activeKeys |> Array.sort |> String.concat "; "

        let queries =
            React.useMemo (
                (fun () ->

                    let termSearchQueries =
                        allTermSearchQueries
                        |> Seq.filter (fun (key, _) ->
                            let isActive = activeKeys.activeKeys |> Set.ofSeq |> Set.contains key

                            isActive
                        )
                        |> ResizeArray

                    let parentSearchQueries =
                        allParentSearchQueries
                        |> Seq.filter (fun (key, _) -> activeKeys.activeKeys |> Set.ofSeq |> Set.contains key)
                        |> ResizeArray

                    let allChildrenSearchQueries =
                        allAllChildrenSearchQueries
                        |> Seq.filter (fun (key, _) -> activeKeys.activeKeys |> Set.ofSeq |> Set.contains key)
                        |> ResizeArray

                    {
                        hasProvider = true
                        disableDefault = activeKeys.disableDefault
                        termSearchQueries = termSearchQueries
                        parentSearchQueries = parentSearchQueries
                        allChildrenSearchQueries = allChildrenSearchQueries
                    }
                ),
                [|
                    activeKeysString
                    activeKeys.disableDefault
                    allTermSearchQueries
                    allParentSearchQueries
                    allAllChildrenSearchQueries
                |]
            )

        ActiveKeysContext.TermSearchActiveKeysCtx.Provider(
            {
                state = activeKeys
                setState = setActiveKeys
            },
            TermSearchConfigCtx.Provider(queries, TermSearchAllKeysCtx.Provider(allKeys, children))
        )
