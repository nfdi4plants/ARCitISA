module ARCtrl.ArcIO

open Arcitisa.Components.Shared

let getArcRootPath (rootPath: string) =
    let normalizedPath = PathHelpers.normalizePath rootPath

    ARCtrl.ArcPathHelper.getFileName normalizedPath
