module Database.Ontology

open Neo4j.Driver
open Helper
open Arcitisa.Components.Shared.Database

type Ontology(credentials: Neo4JCredentials) =

    static member private asOntology: IRecord -> Arcitisa.Components.Shared.Database.Ontology =
        fun (record: IRecord) ->
            let (ontology: Arcitisa.Components.Shared.Database.Ontology) = {
                Name = record.["o.name"].As<string>()
                Version = record.["o.version"] |> defaultOutputWith<string> ""
                LastUpdated = record.["o.lastUpdated"].As<string>() |> System.DateTime.Parse
                Author = record.["o.version"] |> defaultOutputWith<string> ""
            }

            ontology

    member this.getAll() =
        let query =
            @"MATCH (o:Ontology)
            RETURN o.name, o.lastUpdated, o.version, o.author"

        Neo4j.runQuery (query, None, Ontology.asOntology, credentials)

    member this.getByName(name: string) =
        let query =
            @"MATCH (o:Ontology {name: $Name})
            RETURN o.name, o.lastUpdated, o.version, o.author"

        let param = Map [ "Name", name ] |> Some
        Neo4j.runQuery (query, param, Ontology.asOntology, credentials)
