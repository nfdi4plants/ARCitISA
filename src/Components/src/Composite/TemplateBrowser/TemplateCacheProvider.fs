namespace Arcitisa.Components.Composite.Template

open System
open ARCtrl
open Elmish
open Fable.Core
open Feliz
open Feliz.UseElmish
open Arcitisa.Components
open Arcitisa.Components.Composite.Template.Types

module TemplateHelper = Arcitisa.Components.Composite.Template.Helper
module TemplateCacheCtx = Arcitisa.Components.Composite.Template.TemplateCacheContext

module private TemplateCacheProviderHelper =


    [<Literal>]
    let CacheStorageKey = "Arcitisa.Components.template.cache.v1"

    let DefaultFetchInterval = TimeSpan.FromHours 1.0

    type Msg =
        | LoadTemplatesRequest of forceRefresh: bool
        | LoadTemplatesResponse of requestId: Guid * result: Result<Template[], string>

    type State = {
        LastFetchedUtcTicks: int64 option
        LatestFetchId: Guid option
        IsLoading: bool
        Templates: Template[]
    }

    let private sortTemplates (templates: Template[]) =
        templates |> Array.sortBy (fun template -> template.Name)

    let private getLastFetchedUtc (state: State) =
        state.LastFetchedUtcTicks
        |> Option.map (fun ticks -> DateTime(ticks, DateTimeKind.Utc))

    let private shouldFetchFresh (forceRefresh: bool) (state: State) (nowUtc: DateTime) =
        if forceRefresh then
            true
        else
            state
            |> getLastFetchedUtc
            |> Option.map (fun lastFetchedUtc -> nowUtc - lastFetchedUtc > DefaultFetchInterval)
            |> Option.defaultValue true


    let init () =
        {
            LastFetchedUtcTicks = None
            LatestFetchId = None
            IsLoading = false
            Templates = [||]
        },
        Cmd.ofMsg (LoadTemplatesRequest false)

    let update (loadTemplates: unit -> JS.Promise<Result<Template[], string>>) msg state =
        match msg with
        | LoadTemplatesRequest forceRefresh ->

            let shouldFetchFresh = forceRefresh || shouldFetchFresh false state DateTime.UtcNow

            if shouldFetchFresh then
                let requestId = Guid.NewGuid()

                let nextState = {
                    state with
                        LatestFetchId = Some requestId
                        IsLoading = true
                }

                let cmd =
                    Cmd.OfPromise.either
                        loadTemplates
                        ()
                        (fun result -> LoadTemplatesResponse(requestId, result))
                        (fun error -> LoadTemplatesResponse(requestId, Error error.Message))

                nextState, cmd
            else
                state, Cmd.none
        // Skip if stale request
        | LoadTemplatesResponse(requestId, result) when state.LatestFetchId <> Some requestId -> state, Cmd.none
        | LoadTemplatesResponse(_, result) ->
            match result with
            | Ok templates ->
                let sortedTemplates = templates |> sortTemplates

                {
                    LastFetchedUtcTicks = Some(DateTime.UtcNow.Ticks)
                    IsLoading = false
                    LatestFetchId = None
                    Templates = sortedTemplates
                },
                Cmd.none
            | Error message ->
                // here bind error callbacks if needed, for now we just log to console and update state
                Browser.Dom.console.error ("Failed to load templates:", message)

                {
                    state with
                        IsLoading = false
                        LatestFetchId = None
                },
                Cmd.none

open TemplateCacheProviderHelper

[<Erase; Mangle(false)>]
type TemplateCacheProvider =

    [<ReactComponent(true)>]
    static member TemplateCacheProvider
        (loadTemplates: unit -> JS.Promise<Result<Template[], string>>, children: ReactElement)
        =

        let model, dispatch =
            React.useElmish ((fun () -> init ()), update loadTemplates, [||])

        let contextValue: TemplateCacheCtx.TemplateCacheContext = {
            IsLoading = model.IsLoading
            Templates = model.Templates
            RefreshTemplates = fun () -> dispatch (LoadTemplatesRequest true)
        }

        TemplateCacheCtx.TemplateCacheCtx.Provider(contextValue, children)
