module GitHub

open System.IO
open FSharp.Data
open System.Text.Json

type Asset = {
    url: string
    browser_download_url: string
    id: int
    node_id: string
    name: string
    label: string
    content_type: string
    size: int
    download_count: int
    digest: string
    created_at: System.DateTime
    updated_at: System.DateTime
}

type ReleaseResponse = {
    url: string
    assets_url: string
    upload_url: string
    html_url: string
    tarball_url: string
    zipball_url: string
    id: int
    node_id: string
    target_commitish: string
    tag_name: string
    name: string
    body: string
    draft: bool
    prerelease: bool
    assets: Asset[]
}

type UpdateReleaseRequest = {
    tag_name: string option
    target_commitish: string option
    name: string option
    body: string option
    draft: bool option
    prerelease: bool option
    /// "true", "false", "legacy"
    make_latest: string option
} with

    static member Empty = {
        tag_name = None
        target_commitish = None
        name = None
        body = None
        draft = None
        prerelease = None
        make_latest = None
    }

let private mkHeaders token = [
    "Authorization", sprintf "Bearer %s" token
    "Accept", "application/vnd.github+json"
    "X-GitHub-Api-Version", "2022-11-28"
    "user-agent", "Swate build script"
]

let private toJson (data: (string * obj) list) = JsonSerializer.Serialize(dict data)

let mkRelease (token: string) (version: Changelog.Version) =
    let endpoint =
        $"https://api.github.com/repos/{ProjectInfo.gitOwner}/{ProjectInfo.project}/releases"

    let isPrerelease = version.Version.IsPrerelease

    let bodyJson =
        toJson [
            "tag_name", version.Version.ToString() :> obj
            "name", version.Version.ToString() :> obj
            "body", version.Body :> obj
            "draft", true :> obj
            "generate_release_notes", true :> obj
            if isPrerelease then
                "prerelease", true :> obj
        ]


    Http.RequestString(endpoint, httpMethod = "POST", headers = mkHeaders token, body = TextRequest(bodyJson))
    |> JsonSerializer.Deserialize<ReleaseResponse>

let tryGetRelease (token: string) (version: Changelog.Version) =
    let version = version.Version.ToString()

    let endpoint =
        $"https://api.github.com/repos/{ProjectInfo.gitOwner}/{ProjectInfo.project}/releases/tags/{version}"

    let response =
        Http.Request(endpoint, httpMethod = "GET", headers = mkHeaders token, silentHttpErrors = true)

    match response.StatusCode with
    | 404 ->
        printfn "[GitHub] Release %O not found" version
        None
    | 200 ->
        printfn "[GitHub] Found Release %O" version

        let jsonStr =
            match response.Body with
            | Text text -> text
            | _ -> failwith "Unexpected response body"

        let response = JsonSerializer.Deserialize<ReleaseResponse>(jsonStr)
        Some response
    | code -> failwithf "Error: unexpected status code %d" code

let getReleases (token: string) (itemsPerPage: int) =
    let endpoint =
        $"https://api.github.com/repos/{ProjectInfo.gitOwner}/{ProjectInfo.project}/releases"

    Http.RequestString(
        endpoint,
        httpMethod = "GET",
        headers = mkHeaders token,
        query = [ "per_page", string itemsPerPage ]
    )
    |> JsonSerializer.Deserialize<ReleaseResponse[]>

let tryGetLatestRelease (token: string) (version: Changelog.Version) =
    let releases = getReleases token 3

    releases
    |> Array.filter (fun r -> r.tag_name = version.Version.ToString() || r.name = version.Version.ToString())
    |> Array.tryExactlyOne

let updateRelease (token: string) (version: Changelog.Version) (fn: ReleaseResponse -> UpdateReleaseRequest) =

    let response =
        tryGetLatestRelease token version
        |> Option.defaultWith (fun () -> failwithf "Release for version %O not found" version.Version)

    let request = fn response

    let endpoint =
        $"https://api.github.com/repos/{ProjectInfo.gitOwner}/{ProjectInfo.project}/releases/{response.id}"

    let jsonList = [
        if request.tag_name.IsSome then
            "tag_name", request.tag_name.Value :> obj
        if request.name.IsSome then
            "name", request.name.Value :> obj
        if request.body.IsSome then
            "body", request.body.Value :> obj
        if request.draft.IsSome then
            "draft", request.draft.Value :> obj
        if request.prerelease.IsSome then
            "prerelease", request.prerelease.Value :> obj
        if request.make_latest.IsSome then
            "make_latest", request.make_latest.Value :> obj
        if request.target_commitish.IsSome then
            "target_commitish", request.target_commitish.Value :> obj
    ]

    match jsonList with
    | [] ->
        printRedfn "No fields to update in release"
        Error "No fields to update"
    | jsonList ->
        let json = toJson jsonList

        Http.Request(endpoint, httpMethod = "PATCH", headers = mkHeaders token, body = TextRequest json)
        |> Ok

let private uploadAssetToRelease (token: string) (release: ReleaseResponse) (filePath: string) =
    let fileName = Path.GetFileName(filePath)
    let fileBytes = File.ReadAllBytes(filePath)

    let existingAsset = release.assets |> Array.tryFind (fun a -> a.name = fileName)

    match existingAsset with
    | Some asset ->
        printGreenfn "[GitHub] Asset %s already exists, updating..." fileName

        let endpoint =
            $"https://api.github.com/repos/{ProjectInfo.gitOwner}/{ProjectInfo.project}/releases/assets/{asset.id}"

        Http.Request(
            endpoint,
            httpMethod = "DELETE",
            headers = mkHeaders token @ [ "Content-Type", "application/octet-stream" ],
            query = [ "name", fileName; "label", fileName ]
        )
        |> ignore
    | None -> printGreenfn "[GitHub] Uploading asset %s..." fileName

    let endpoint =
        $"https://uploads.github.com/repos/{ProjectInfo.gitOwner}/{ProjectInfo.project}/releases/{release.id}/assets"

    Http.Request(
        endpoint,
        httpMethod = "POST",
        headers = mkHeaders token @ [ "Content-Type", "application/octet-stream" ],
        query = [ "name", fileName; "label", fileName ],
        body = BinaryUpload fileBytes
    )
    |> ignore

/// Replaces if asset of the same name exists
let uploadReleaseAsset (token: string) (version: Changelog.Version) (filePath: string) =

    let release =
        tryGetLatestRelease token version
        |> Option.defaultWith (fun () -> failwithf "Release for version %O not found" version.Version)

    uploadAssetToRelease token release filePath

/// Uploads all files in a directory (recursively) as release assets
let uploadReleaseAssets (token: string) (version: Changelog.Version) (dir: string) =
    if not (Directory.Exists(dir)) then
        printRedfn "[GitHub] Directory not found: %s" dir
        exit 1

    let release =
        tryGetLatestRelease token version
        |> Option.defaultWith (fun () -> failwithf "Release for version %O not found" version.Version)

    let files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)

    if files.Length = 0 then
        printGreenfn "[GitHub] No assets found in %s, skipping upload." dir
    else
        for file in files do
            uploadAssetToRelease token release file |> ignore

        printGreenfn "[GitHub] Uploaded %d assets to release %O" files.Length version.Version
