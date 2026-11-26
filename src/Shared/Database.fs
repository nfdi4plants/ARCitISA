module Arcitisa.Components.Shared.Database

type FullTextSearch =
    | Exact
    | Complete
    | PerformanceComplete
    | Fuzzy

open System

type Ontology = {
    Name: string
    Version: string
    LastUpdated: DateTime
    Author: string
} with

    static member create name version lastUpdated authors = {
        Name = name
        Version = version
        LastUpdated = lastUpdated
        Author = authors
    }

type Term = {
    Accession: string
    Name: string
    Description: string
    IsObsolete: bool
    FK_Ontology: string
} with

    static member createTerm(accession, name, description, isObsolete, ontologyName) = {
        Accession = accession
        Name = name
        Description = description
        IsObsolete = isObsolete
        FK_Ontology = ontologyName
    }

type TermRelationship = {
    TermID: int64
    Relationship: string
    RelatedTermID: int64
}

module TreeTypes =

    type TreeTerm = { NodeId: int64; Term: Term }

    type TreeRelationship = {
        RelationshipId: int64
        StartNodeId: int64
        EndNodeId: int64
        Type: string
    }

    type Tree = {
        Nodes: TreeTerm list
        Relationships: TreeRelationship list
    }
