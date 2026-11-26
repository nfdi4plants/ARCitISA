namespace Arcitisa.Components.Shared.DTOs

open Arcitisa.Components.Shared

type ParentTermQuery = {
    parentTermId: string
    limit: int option
} with

    static member create(parentTermId, ?limit) = {
        parentTermId = parentTermId
        limit = limit
    }

type ParentTermQueryResults = {
    query: ParentTermQuery
    results: Database.Term[]
} with

    static member create(query, results) = { query = query; results = results }
