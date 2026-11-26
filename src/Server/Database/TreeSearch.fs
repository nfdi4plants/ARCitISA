module Database.TreeSearch

open System
open Neo4j.Driver

open Arcitisa.Components.Shared.DTOs
open Arcitisa.Components.Shared.Database
open Arcitisa.Components.Shared.Database.TreeTypes

open Helper

type Term with
    static member ofProperties(dict: System.Collections.Generic.IReadOnlyDictionary<string, obj>) = {
        Accession = dict["accession"].As<string>()
        Name =
            if dict.ContainsKey "name" then
                dict["name"].As<string>()
            else
                ""
        Description =
            if dict.ContainsKey "definition" then
                dict["definition"].As<string>()
            else
                ""
        IsObsolete =
            if dict.ContainsKey "is_obsolete" then
                dict["is_obsolete"].As<bool>()
            else
                false
        FK_Ontology = ""
    }

type TreeTerm with
    static member ofINode(node: INode) = {
        NodeId = node.Id
        Term = Term.ofProperties (node.Properties)
    }

type TreeRelationship with
    static member ofIRelationship(relationship: IRelationship) = {
        RelationshipId = relationship.Id
        StartNodeId = relationship.StartNodeId
        EndNodeId = relationship.EndNodeId
        Type = relationship.Type
    }

type Tree(credentials: Neo4JCredentials) =

    static member private asTreeInfo: IRecord -> INode list * IRelationship list =
        fun (record: IRecord) ->
            let path = record.["path"].As<IPath>()

            let relationship = [
                for i in 0 .. path.Relationships.Count - 1 do
                    yield path.Relationships.Item i
            ]

            let startNode = path.Start
            let endNode = path.End
            [ startNode; endNode ], relationship

    member this.getByAccession(accession: string) =
        let query =
            """MATCH (t:Term {accession: $Accession})
            CALL apoc.path.spanningTree(t, {
                labelFilter: "+Term",
                minLevel: 1,
                maxLevel: 2,
                limit: 20
            })
            YIELD path
            RETURN path;
            """

        let param = Map [ "Accession", accession ] |> Some

        let nodeInfo, rltInfo =
            Neo4j.runQuery (query, param, Tree.asTreeInfo, credentials)
            |> List.ofSeq
            |> List.unzip

        {
            Nodes = nodeInfo |> List.concat |> List.distinct |> List.map TreeTerm.ofINode
            Relationships =
                rltInfo
                |> List.concat
                |> List.distinct
                |> List.map TreeRelationship.ofIRelationship
        }
