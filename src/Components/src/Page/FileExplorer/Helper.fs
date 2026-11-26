module Arcitisa.Components.Page.FileExplorer.Helper

open Arcitisa.Components.Page.FileExplorer.Types

let isLfs (item: FileItem) = item.IsLFS = Some true

let needsLfsDownload (item: FileItem) =
    isLfs item && (item.Downloaded = Some false || item.IsLFSPointer = Some true)

let hasLocalLfsCopy (item: FileItem) =
    isLfs item && item.Downloaded = Some true && item.IsLFSPointer = Some false

let iconClassName (baseClasses: string list) (item: FileItem) (getItemIconClass: FileItem -> string option) = [
    yield! baseClasses
    yield item.Icon |> FileItemIcon.className
    yield! item.IconTone |> Option.map FileItemIconTone.className |> Option.toList
    yield! getItemIconClass item |> Option.toList
]
