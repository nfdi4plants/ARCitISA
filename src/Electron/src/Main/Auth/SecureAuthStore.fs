module Main.Auth.SecureAuthStore

open System
open Fable.Core
open Fable.Electron.Main
open Main.Bindings.Filesystem
open Main.Bindings.Path
open Thoth.Json.Core
open Arcitisa.Components.Composite.Authentication.Types

[<Literal>]
let private activeAccountFileName = "active-account.json"

/// JSON key of the account id in the active account file and in the meta files.
[<Literal>]
let private accountIdJsonKey = "localArcitisaAccountId"

/// Non-secret metadata stored in plaintext JSON alongside the encrypted blob.
type AuthMetadata = {
    LocalArcitisaAccountId: string
    Id: int
    Username: string
    Name: string
    Email: string
    CommitEmail: string option
    AvatarUrl: string
    TargetDataHub: string
    DateAdded: string
    TokenStatus: TokenStatus
    TokenExpiresOn: string option
}

/// In-memory representation of a full auth credential set.
type StoredCredential = {
    Metadata: AuthMetadata
    Token: string
}

// ── JS interop helpers for Buffer ────────────────────────────────────

[<Emit("$0.toString('base64')")>]
let private bufferToBase64 (buf: Node.Buffer.Buffer) : string = jsNative

[<Emit("Buffer.from($0, 'base64')")>]
let private bufferFromBase64 (base64: string) : Node.Buffer.Buffer = jsNative

type private CryptoHash =
    abstract update: data: string * inputEncoding: string -> CryptoHash
    abstract digest: encoding: string -> string

[<Import("createHash", "crypto")>]
let private createHash (algorithm: string) : CryptoHash = jsNative

let private sha256Hex (input: string) : string =
    let hash = createHash "sha256"
    hash.update(input, "utf8").digest ("hex")

// ── helpers ──────────────────────────────────────────────────────────

/// Extract host from a normalized base URL for token-provider matching.
let internal extractHost (baseUrl: string) : string =
    let mutable uri = Unchecked.defaultof<Uri>

    if Uri.TryCreate(baseUrl, UriKind.Absolute, &uri) then
        uri.Host.Trim().ToLowerInvariant()
    else
        baseUrl.Trim().ToLowerInvariant()

let private getAuthDir () =
    let settingsRoot = Main.SettingsStore.getSettingsRootPath ()
    let dir = join [| settingsRoot; "Auth" |]
    mkdirSync dir (MkdirOptions(recursive = true))
    dir

let private encryptedFilePath (localArcitisaAccountId: string) =
    join [|
        getAuthDir ()
        $"{localArcitisaAccountId}-credentials.enc"
    |]

let private metaFilePath (localArcitisaAccountId: string) =
    join [| getAuthDir (); $"{localArcitisaAccountId}-meta.json" |]

let private activeAccountFilePath () =
    join [| getAuthDir (); activeAccountFileName |]

let private isSafeLocalArcitisaAccountId (localArcitisaAccountId: string) : bool =
    not (System.String.IsNullOrWhiteSpace localArcitisaAccountId)
    && localArcitisaAccountId.Length <= 256
    && localArcitisaAccountId
       |> Seq.forall (fun c -> System.Char.IsLetterOrDigit c || c = '_' || c = '-')

let private parseTokenStatus (raw: string) : TokenStatus option =
    match raw.Trim().ToLowerInvariant() with
    | "ok" -> Some TokenStatus.Ok
    | "expiring" -> Some TokenStatus.Expiring
    | "invalid" -> Some TokenStatus.Invalid
    | _ -> None

let private tokenStatusToString (status: TokenStatus) : string =
    match status with
    | TokenStatus.Ok -> "ok"
    | TokenStatus.Expiring -> "expiring"
    | TokenStatus.Invalid -> "invalid"

let private authMetadataDecoder (localArcitisaAccountId: string) : Decoder<AuthMetadata> =
    Decode.object (fun get ->
        let tokenStatusFromLegacyFlag =
            get.Optional.Field "tokenInvalid" Decode.bool
            |> Option.map (fun tokenInvalid -> if tokenInvalid then TokenStatus.Invalid else TokenStatus.Ok)

        {
            LocalArcitisaAccountId = localArcitisaAccountId
            Id = get.Required.Field "id" Decode.int
            // Optional: metadata written before commit identities were stored has no username.
            Username = get.Optional.Field "username" Decode.string |> Option.defaultValue ""
            Name = get.Required.Field "name" Decode.string
            Email = get.Required.Field "email" Decode.string
            CommitEmail =
                get.Optional.Field "commitEmail" Decode.string
                |> Option.filter (String.IsNullOrWhiteSpace >> not)
            AvatarUrl = get.Required.Field "avatarUrl" Decode.string
            TargetDataHub = get.Required.Field "targetDataHub" Decode.string
            DateAdded =
                get.Optional.Field "dateAdded" Decode.string
                |> Option.filter (String.IsNullOrWhiteSpace >> not)
                |> Option.defaultWith Arcitisa.Components.DateTimeExtensions.getUtcNowISO
            TokenStatus =
                get.Optional.Field "tokenStatus" Decode.string
                |> Option.bind parseTokenStatus
                |> Option.orElse tokenStatusFromLegacyFlag
                |> Option.defaultValue TokenStatus.Ok
            TokenExpiresOn =
                get.Optional.Field "tokenExpiresOn" Decode.string
                |> Option.filter (String.IsNullOrWhiteSpace >> not)
        }
    )

let private activeLocalArcitisaAccountIdDecoder: Decoder<string option> =
    Decode.object (fun get ->
        get.Optional.Field accountIdJsonKey Decode.string
        |> Option.filter isSafeLocalArcitisaAccountId
    )

let private tryDecodeAuthMetadata (localArcitisaAccountId: string) (raw: string) : AuthMetadata option =
    try
        if not (isSafeLocalArcitisaAccountId localArcitisaAccountId) then
            None
        else
            ARCtrl.Json.Decode.fromJsonString (authMetadataDecoder localArcitisaAccountId) raw
            |> Some
            |> Option.filter (fun metadata -> metadata.Id > 0)
    with _ ->
        None

let private tryDecodeActiveLocalArcitisaAccountId (raw: string) : string option =
    try
        ARCtrl.Json.Decode.fromJsonString activeLocalArcitisaAccountIdDecoder raw
    with _ ->
        None

let private tryGetAccountPaths (localArcitisaAccountId: string) : (string * string) option =
    if isSafeLocalArcitisaAccountId localArcitisaAccountId then
        Some(encryptedFilePath localArcitisaAccountId, metaFilePath localArcitisaAccountId)
    else
        None

let private normalizeAccountIdentity (targetDataHub: string) (email: string) : string =
    let host = extractHost targetDataHub
    let normalizedEmail = email.Trim().ToLowerInvariant()
    $"{host}|{normalizedEmail}"

/// Generate a deterministic filesystem-safe local account key from host and email.
let generateLocalArcitisaAccountId (targetDataHub: string) (email: string) : string =
    normalizeAccountIdentity targetDataHub email |> sha256Hex

// ── public API ───────────────────────────────────────────────────────

let isAvailable () : bool =
    try
        safeStorage.isEncryptionAvailable ()
    with _ ->
        false

let store (credential: StoredCredential) : Result<unit, string> =
    try
        if not (isAvailable ()) then
            Error "ARCitISA safe storage is not available on this system."
        elif not (isSafeLocalArcitisaAccountId credential.Metadata.LocalArcitisaAccountId) then
            Error "Invalid account identifier format."
        else
            let localArcitisaAccountId = credential.Metadata.LocalArcitisaAccountId
            let encrypted: Node.Buffer.Buffer = safeStorage.encryptString credential.Token
            let base64 = bufferToBase64 encrypted
            let tmpEnc = encryptedFilePath localArcitisaAccountId + ".tmp"
            writeFileSync tmpEnc base64 TextEncoding.Utf8
            renameSync tmpEnc (encryptedFilePath localArcitisaAccountId)

            let metaJson =
                JS.JSON.stringify {|
                    localArcitisaAccountId = credential.Metadata.LocalArcitisaAccountId
                    id = credential.Metadata.Id
                    username = credential.Metadata.Username
                    name = credential.Metadata.Name
                    email = credential.Metadata.Email
                    commitEmail = credential.Metadata.CommitEmail
                    avatarUrl = credential.Metadata.AvatarUrl
                    targetDataHub = credential.Metadata.TargetDataHub
                    dateAdded = credential.Metadata.DateAdded
                    tokenStatus = tokenStatusToString credential.Metadata.TokenStatus
                    tokenExpiresOn = credential.Metadata.TokenExpiresOn
                    tokenInvalid = credential.Metadata.TokenStatus = TokenStatus.Invalid
                |}

            let tmpMeta = metaFilePath localArcitisaAccountId + ".tmp"
            writeFileSync tmpMeta metaJson TextEncoding.Utf8
            renameSync tmpMeta (metaFilePath localArcitisaAccountId)
            Ok()
    with ex ->
        Error $"Failed to store auth credentials: {ex.Message}"

let tryLoad (localArcitisaAccountId: string) : StoredCredential option =
    try
        if not (isAvailable ()) then
            None
        else
            match tryGetAccountPaths localArcitisaAccountId with
            | None -> None
            | Some(enc, metaPath) ->
                if not (existsSync enc) then
                    None
                elif not (existsSync metaPath) then
                    None
                else
                    let base64 = readFileSync enc TextEncoding.Utf8
                    let buffer = bufferFromBase64 base64
                    let token = safeStorage.decryptString buffer
                    let metaRaw = readFileSync metaPath TextEncoding.Utf8

                    tryDecodeAuthMetadata localArcitisaAccountId metaRaw
                    |> Option.map (fun metadata -> { Metadata = metadata; Token = token })
    with _ ->
        None

/// Load all stored accounts by scanning the Auth directory for meta files.
let loadAll () : StoredCredential list =
    try
        if not (isAvailable ()) then
            []
        else
            let dir = getAuthDir ()
            let files = readdirSync dir

            files
            |> Array.choose (fun f ->
                if f.EndsWith("-meta.json") then
                    let localArcitisaAccountId = f.Replace("-meta.json", "")
                    tryLoad localArcitisaAccountId
                else
                    None
            )
            |> Array.toList
    with _ -> []

let remove (localArcitisaAccountId: string) : unit =
    try
        match tryGetAccountPaths localArcitisaAccountId with
        | None -> ()
        | Some(enc, metaPath) ->
            if existsSync enc then
                unlinkSync enc

            if existsSync metaPath then
                unlinkSync metaPath
    with _ ->
        ()

let clearAll () : unit =
    try
        let dir = getAuthDir ()
        let files = readdirSync dir

        for f in files do
            let fullPath = join [| dir; f |]
            unlinkSync fullPath
    with _ ->
        ()

let getActiveLocalArcitisaAccountId () : string option =
    try
        let path = activeAccountFilePath ()

        if existsSync path then
            let raw = readFileSync path TextEncoding.Utf8
            tryDecodeActiveLocalArcitisaAccountId raw
        else
            None
    with _ ->
        None

let setActiveLocalArcitisaAccountId (localArcitisaAccountId: string option) : unit =
    try
        let path = activeAccountFilePath ()

        match localArcitisaAccountId with
        | Some id when isSafeLocalArcitisaAccountId id ->
            let json =
                JS.JSON.stringify {| localArcitisaAccountId = id |}

            let tmp = path + ".tmp"
            writeFileSync tmp json TextEncoding.Utf8
            renameSync tmp path
        | Some _ -> ()
        | None ->
            if existsSync path then
                unlinkSync path
    with _ ->
        ()
