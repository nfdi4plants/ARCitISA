module ElectronCore.SecureAuthStoreTests

open System
open System.Text
open Fable.Core
open Fable.Core.JsInterop
open Main.Auth
open Main.Bindings.Path
open Arcitisa.Components.Composite.Authentication.Types
open Vitest
open ElectronCore.TestHelpers

let private fsPromisesDynamic: obj = importAll "fs/promises"

let private isLowerHex (value: string) =
    value |> Seq.forall (fun c -> "0123456789abcdef".IndexOf(c) >= 0)

let private authDirectoryPath rootPath = join [| rootPath; "Settings"; "Auth" |]

let private writeUtf8FileAsync (path: string) (content: string) : JS.Promise<unit> = promise {
    let! _ = fsPromisesDynamic?writeFile (path, content, "utf8") |> unbox<JS.Promise<obj>>

    return ()
}

let private readJsonFileAsync (path: string) : JS.Promise<obj> = promise {
    let! content = fsPromisesDynamic?readFile (path, "utf8") |> unbox<JS.Promise<string>>
    return JS.JSON.parse content
}

let private ensureAuthDirectoryAsync rootPath : JS.Promise<string> = promise {
    let authDirectory = authDirectoryPath rootPath

    let! _ =
        fsPromisesDynamic?mkdir (authDirectory, createObj [ "recursive" ==> true ])
        |> unbox<JS.Promise<obj>>

    return authDirectory
}

let private seedStoredCredentialAsync rootPath localArcitisaAccountId metadataJson (token: string) : JS.Promise<unit> = promise {
    let! authDirectory = ensureAuthDirectoryAsync rootPath
    let encryptedToken = token |> Encoding.UTF8.GetBytes |> Convert.ToBase64String

    do!
        writeUtf8FileAsync
            (join [|
                authDirectory
                $"{localArcitisaAccountId}-credentials.enc"
            |])
            encryptedToken

    do! writeUtf8FileAsync (join [| authDirectory; $"{localArcitisaAccountId}-meta.json" |]) metadataJson
}

let private seedActiveAccountAsync rootPath activeAccountJson : JS.Promise<unit> = promise {
    let! authDirectory = ensureAuthDirectoryAsync rootPath
    do! writeUtf8FileAsync (join [| authDirectory; "active-account.json" |]) activeAccountJson
}

let private withTempAuthStore (testBody: string -> JS.Promise<unit>) : JS.Promise<unit> = promise {
    let! rootPath = createTempDirectoryAsync "arcitisa-auth-store-"
    Vitest.vi.stubEnv ("ARCITISA_TEST_USER_DATA", Some rootPath)

    try
        do! testBody rootPath
        do! removeDirectoryAsync rootPath
        Vitest.vi.unstubAllEnvs ()
    with error ->
        do! removeDirectoryAsync rootPath
        Vitest.vi.unstubAllEnvs ()
        return raise error
}

let private expectStoredCredential (credential: SecureAuthStore.StoredCredential option) =
    match credential with
    | Some value -> value
    | None -> failwith "Expected stored credential to load."

Vitest.describe (
    "SecureAuthStore.generateLocalArcitisaAccountId",
    fun () ->
        Vitest.test (
            "produces distinct hashes for emails that previously collided",
            fun () ->
                let targetDataHub = "https://datahub.example.org"
                let dotted = SecureAuthStore.generateLocalArcitisaAccountId targetDataHub "a.b@x"

                let underscored =
                    SecureAuthStore.generateLocalArcitisaAccountId targetDataHub "a_b@x"

                Vitest.expect(dotted <> underscored).toBe (true)
        )

        Vitest.test (
            "normalizes host casing, trailing slash, and email whitespace",
            fun () ->
                let canonical =
                    SecureAuthStore.generateLocalArcitisaAccountId "https://datahub.example.org" "user@example.org"

                let normalizedVariant =
                    SecureAuthStore.generateLocalArcitisaAccountId "https://DATAHUB.example.org/" " User@Example.org "

                Vitest.expect(normalizedVariant).toBe (canonical)
        )

        Vitest.test (
            "returns a filesystem-safe hex hash",
            fun () ->
                let accountId =
                    SecureAuthStore.generateLocalArcitisaAccountId "https://datahub.example.org" "user@example.org"

                Vitest.expect(accountId.Length).toBe (64)
                Vitest.expect(isLowerHex accountId).toBe (true)
        )
)

Vitest.describe (
    "SecureAuthStore persisted behavior",
    fun () ->
        Vitest.test (
            "stores and restores current credentials",
            fun () ->
                withTempAuthStore (fun _ -> promise {
                    let credential: SecureAuthStore.StoredCredential = {
                        Metadata = {
                            LocalArcitisaAccountId = "current-key"
                            Id = 42
                            Username = "current-user"
                            Name = "Current User"
                            Email = "current@example.org"
                            CommitEmail = Some "42-current-user@users.noreply.datahub.example.org"
                            AvatarUrl = "https://example.org/avatar.png"
                            TargetDataHub = "https://datahub.example.org"
                            DateAdded = "2026-06-09T07:00:00.000Z"
                            TokenStatus = TokenStatus.Invalid
                            TokenExpiresOn = Some "2026-06-30"
                        }
                        Token = "current-token"
                    }

                    match SecureAuthStore.store credential with
                    | Error message -> failwith message
                    | Ok() -> ()

                    let loaded = SecureAuthStore.tryLoad "current-key" |> expectStoredCredential
                    Vitest.expect(loaded).toEqual (credential)
                })
        )

        Vitest.test (
            "rejects credentials without a positive numeric id",
            fun () ->
                withTempAuthStore (fun rootPath -> promise {
                    let metadataWithoutId =
                        """{"name":"User","email":"user@example.org","avatarUrl":"https://example.org/avatar.png","targetDataHub":"https://datahub.example.org"}"""

                    do! seedStoredCredentialAsync rootPath "missing-id" metadataWithoutId "token"
                    Vitest.expect(SecureAuthStore.tryLoad "missing-id").toBe (None)

                    let metadataWithInvalidId =
                        """{"id":0,"name":"User","email":"user@example.org","avatarUrl":"https://example.org/avatar.png","targetDataHub":"https://datahub.example.org"}"""

                    do! seedStoredCredentialAsync rootPath "invalid-id" metadataWithInvalidId "token"
                    Vitest.expect(SecureAuthStore.tryLoad "invalid-id").toBe (None)
                })
        )

        Vitest.test (
            "uses the validated filename-derived key instead of metadata account fields",
            fun () ->
                withTempAuthStore (fun rootPath -> promise {
                    let metadataJson =
                        """{"localArcitisaAccountId":"stored-key","id":42,"name":"Current User","email":"current@example.org","avatarUrl":"https://example.org/avatar.png","targetDataHub":"https://datahub.example.org"}"""

                    do! seedStoredCredentialAsync rootPath "filename-key" metadataJson "token"

                    let loaded = SecureAuthStore.tryLoad "filename-key" |> expectStoredCredential
                    Vitest.expect(loaded.Metadata.LocalArcitisaAccountId).toBe ("filename-key")
                    Vitest.expect(loaded.Metadata.Id).toBe (42)
                })
        )

        Vitest.test (
            "rejects unsafe account keys and malformed stored metadata",
            fun () ->
                withTempAuthStore (fun rootPath -> promise {
                    do! seedStoredCredentialAsync rootPath "valid-key" """{"name":""" "token"

                    Vitest.expect(SecureAuthStore.tryLoad "../unsafe").toBe (None)
                    Vitest.expect(SecureAuthStore.tryLoad "valid-key").toBe (None)
                })
        )

        Vitest.test (
            "restores the current active account selection",
            fun () ->
                withTempAuthStore (fun _ -> promise {
                    SecureAuthStore.setActiveLocalArcitisaAccountId (Some "current-account")
                    Vitest.expect(SecureAuthStore.getActiveLocalArcitisaAccountId ()).toBe (Some "current-account")
                })
        )

        Vitest.test (
            "rejects unsafe and malformed active account selections",
            fun () ->
                withTempAuthStore (fun rootPath -> promise {
                    do! seedActiveAccountAsync rootPath """{"localArcitisaAccountId":"../unsafe"}"""
                    Vitest.expect(SecureAuthStore.getActiveLocalArcitisaAccountId ()).toBe (None)

                    do! seedActiveAccountAsync rootPath """{"accountId":"unsupported-account"}"""
                    Vitest.expect(SecureAuthStore.getActiveLocalArcitisaAccountId ()).toBe (None)

                    do! seedActiveAccountAsync rootPath """{"localArcitisaAccountId":"""
                    Vitest.expect(SecureAuthStore.getActiveLocalArcitisaAccountId ()).toBe (None)
                })
        )

        Vitest.test (
            "restores an active account selection that only has the key of older versions",
            fun () ->
                withTempAuthStore (fun rootPath -> promise {
                    do! seedActiveAccountAsync rootPath """{"localSwateAccountId":"older-account"}"""
                    Vitest.expect(SecureAuthStore.getActiveLocalArcitisaAccountId ()).toBe (Some "older-account")
                })
        )

        Vitest.test (
            "prefers the current account key when both keys are present",
            fun () ->
                withTempAuthStore (fun rootPath -> promise {
                    do!
                        seedActiveAccountAsync
                            rootPath
                            """{"localArcitisaAccountId":"current-account","localSwateAccountId":"older-account"}"""

                    Vitest.expect(SecureAuthStore.getActiveLocalArcitisaAccountId ()).toBe (Some "current-account")
                })
        )

        Vitest.test (
            "writes the active account selection under both keys so older versions can read it",
            fun () ->
                withTempAuthStore (fun rootPath -> promise {
                    SecureAuthStore.setActiveLocalArcitisaAccountId (Some "shared-account")

                    let! written = readJsonFileAsync (join [| authDirectoryPath rootPath; "active-account.json" |])
                    Vitest.expect(written?localArcitisaAccountId).toBe ("shared-account")
                    Vitest.expect(written?localSwateAccountId).toBe ("shared-account")
                })
        )

        Vitest.test (
            "writes the account id under both keys into the meta file",
            fun () ->
                withTempAuthStore (fun rootPath -> promise {
                    let credential: SecureAuthStore.StoredCredential = {
                        Metadata = {
                            LocalArcitisaAccountId = "meta-key"
                            Id = 7
                            Username = "meta-user"
                            Name = "Meta User"
                            Email = "meta@example.org"
                            CommitEmail = None
                            AvatarUrl = "https://example.org/avatar.png"
                            TargetDataHub = "https://datahub.example.org"
                            DateAdded = "2026-06-09T07:00:00.000Z"
                            TokenStatus = TokenStatus.Ok
                            TokenExpiresOn = None
                        }
                        Token = "meta-token"
                    }

                    match SecureAuthStore.store credential with
                    | Error message -> failwith message
                    | Ok() -> ()

                    let! written = readJsonFileAsync (join [| authDirectoryPath rootPath; "meta-key-meta.json" |])
                    Vitest.expect(written?localArcitisaAccountId).toBe ("meta-key")
                    Vitest.expect(written?localSwateAccountId).toBe ("meta-key")
                })
        )
)
