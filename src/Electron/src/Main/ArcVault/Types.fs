[<AutoOpen>]
module Main.ArcVaultTypes

type ArcVaultFileSystemEvent = {
    EventName: string
    RelativePath: string
    AbsolutePath: string
}
