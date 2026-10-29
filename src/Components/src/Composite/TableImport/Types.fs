module Swate.Components.Composite.TableImport.Types

type ImportTable = {
    Index: int
    FullImport: bool
    Name: string option
}

type SelectiveImportConfig = {
    ImportType: ARCtrl.TableJoinOptions
    ImportTables: ImportTable list
    DeselectedColumns: Set<int * int>
} with

    static member init() = {
        ImportType = ARCtrl.TableJoinOptions.Headers
        ImportTables = []
        DeselectedColumns = Set.empty
    }
