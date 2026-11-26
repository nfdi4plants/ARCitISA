namespace Cytoscape

open Arcitisa.Components.Shared
open Arcitisa.Components.Shared.Database
open JS

type Model = {
    TargetAccession: string
    //CyObject: Types.ICytoscape option
    CyTermTree: TreeTypes.Tree option
} with

    static member init(?accession: string) = {
        TargetAccession = if accession.IsSome then accession.Value else ""
        //CyObject = None
        CyTermTree = None
    }

type Msg =
    // Client
    // Server Interop
    | GetTermTree of accession: string
    | GetTermTreeResponse of tree: TreeTypes.Tree
