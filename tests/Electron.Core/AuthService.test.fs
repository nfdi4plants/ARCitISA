module ElectronCore.AuthServiceTests

open Main.Auth
open Swate.Electron.Shared.AuthTypes
open Vitest

Vitest.describe (
    "AuthService token deletion outcome",
    fun () ->
        Vitest.test (
            "a deleted token and one the DataHUB no longer accepts both count as deleted",
            fun () ->
                Vitest.expect(AuthService.tokenDeletionOutcome (Ok())).toEqual TokenDeletionOutcome.Deleted

                Vitest
                    .expect(
                        AuthService.tokenDeletionOutcome (
                            Error {
                                Kind = AuthFailureKind.Unauthorized
                                Message = "expired"
                            }
                        )
                    )
                    .toEqual
                    TokenDeletionOutcome.Deleted
        )

        Vitest.test (
            "any other failure keeps its message for the user",
            fun () ->
                Vitest
                    .expect(
                        AuthService.tokenDeletionOutcome (
                            Error {
                                Kind = AuthFailureKind.Network
                                Message = "The DataHUB could not be reached."
                            }
                        )
                    )
                    .toEqual (TokenDeletionOutcome.Failed "The DataHUB could not be reached.")
        )
)
