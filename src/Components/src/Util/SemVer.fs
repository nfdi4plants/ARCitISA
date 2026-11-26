module Arcitisa.Components.Util.SemVer

open ARCtrl.Helper.Regex
open ARCtrl.Helper.Regex.ActivePatterns
open ARCtrl.Helper.SemVer
open System.Text.RegularExpressions

type SemVer with

    /// Compares two SemVer instances based on their pre-release identifiers.
    /// Returns:
    /// - `-1` if v1 < v2
    /// - `1` if v1 > v2
    /// - `0` if they are equal.
    static member comparePreRelease (v1: ARCtrl.Helper.SemVer.SemVer) (v2: ARCtrl.Helper.SemVer.SemVer) =
        match v1.PreRelease, v2.PreRelease with
        | None, None -> 0
        | Some _, None -> -1
        | None, Some _ -> 1
        | Some pre1, Some pre2 ->
            let pre1Parts = pre1.Split('.')
            let pre2Parts = pre2.Split('.')

            let rec compareParts (parts1: string list) (parts2: string list) =
                match parts1, parts2 with
                | [], [] -> 0
                | [], _ -> -1
                | _, [] -> 1
                | p1 :: rest1, p2 :: rest2 ->
                    match System.Int32.TryParse(p1), System.Int32.TryParse(p2) with
                    | (true, int1), (true, int2) ->
                        if int1 < int2 then -1
                        elif int1 > int2 then 1
                        else compareParts rest1 rest2
                    | (false, _), (false, _) ->
                        if p1 < p2 then -1
                        elif p1 > p2 then 1
                        else compareParts rest1 rest2
                    | (true, _), (false, _) -> -1
                    | (false, _), (true, _) -> 1

            compareParts (List.ofArray pre1Parts) (List.ofArray pre2Parts)

    /// Compares two SemVer instances to determine if v1 is older than v2.
    /// Required until actual IComparable implementation is done for SemVer (https://github.com/nfdi4plants/ARCtrl/issues/639)
    static member isOlder (v1: ARCtrl.Helper.SemVer.SemVer) (v2: ARCtrl.Helper.SemVer.SemVer) =
        match v1, v2 with
        | v1, v2 when v1.Major < v2.Major -> true
        | v1, v2 when v1.Major > v2.Major -> false
        | v1, v2 when v1.Minor < v2.Minor -> true
        | v1, v2 when v1.Minor > v2.Minor -> false
        | v1, v2 when v1.Patch < v2.Patch -> true
        | v1, v2 when v1.Patch > v2.Patch -> false
        | v1, v2 when v1.Major = v2.Major && v1.Minor = v2.Minor && v1.Patch = v2.Patch ->
            let preReleaseComparison = ARCtrl.Helper.SemVer.SemVer.comparePreRelease v1 v2
            preReleaseComparison < 0
        | _ -> false

    static member isEqualWithoutBuild (v1: ARCtrl.Helper.SemVer.SemVer) (v2: ARCtrl.Helper.SemVer.SemVer) =
        v1.Major = v2.Major
        && v1.Minor = v2.Minor
        && v1.Patch = v2.Patch
        && v1.PreRelease = v2.PreRelease


    static member tryOfStringFixed(str: string) =
        match str with
        | Regex (SemVerAux.Pattern) m ->
            let g = m.Groups

            if
                m.Groups.["major"].Success
                && m.Groups.["minor"].Success
                && m.Groups.["patch"].Success
            then
                let major = int g.["major"].Value
                let minor = int g.["minor"].Value
                let patch = int g.["patch"].Value

                let pre =
                    match g.["pre"].Success with
                    | true -> Some g.["pre"].Value
                    | false -> None

                let meta =
                    match g.["build"].Success with
                    | true -> Some g.["build"].Value
                    | false -> None

                Some <| SemVer.create (major, minor, patch, ?pre = pre, ?meta = meta)
            else
                None

        | _ -> None

    static member tryParse(version: string) : SemVer option =
        if System.String.IsNullOrWhiteSpace version then
            None
        else
            let trimmed = version.Trim().TrimStart('v', 'V')
            SemVer.tryOfStringFixed trimmed

    static member isOlderVersion (current: string) (candidate: string) =
        match SemVer.tryParse current, SemVer.tryParse candidate with
        | Some current, Some candidate -> SemVer.isOlder current candidate
        | _ -> false
