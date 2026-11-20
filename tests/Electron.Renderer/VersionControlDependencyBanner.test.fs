module ElectronRenderer.VersionControlDependencyBannerTests

open Fable.Core.JsInterop
open Renderer.Components.VersionControlDependencyBanner
open Renderer.Context.VersionControlDependencyContext
open Swate.Electron.Shared.VersionControlTypes
open Vitest

// The context module reaches the IPC bridges through the Api module. The preload is absent in tests,
// so the bridges are replaced by empty objects that no test calls.
Vitest.vi.mock (
    "./src/Electron/src/Renderer/Api.js",
    box (fun () ->
        createObj [
            "ipcGitLabApi" ==> createObj []
            "ipcVersionControlApi" ==> createObj []
            "ipcArcVaultApi" ==> createObj []
            "ipcAuthApi" ==> createObj []
            "ipcTemplateApi" ==> createObj []
            "ipcValidationPackageApi" ==> createObj []
        ]
    )
)
|> ignore

let private status (name: string) (installed: bool) (compatible: bool) : DependencyStatusDto = {
    Component = name
    Installed = installed
    Version = if installed then Some $"{name}/1.0" else None
    Compatible = compatible
    Remediation = Some $"fix {name}"
    ProbeExecutablePath = None
}

let private working = [| status "git" true true; status "git-lfs" true true |]

let private missingGit = [| status "git" false false; status "git-lfs" true true |]

let private stateOf (statuses: DependencyStatusDto[]) (failure: string option) (isChecking: bool) = {
    Statuses = statuses
    Failure = failure
    IsChecking = isChecking
    CheckedAt = Some 1000.0
}

Vitest.describe (
    "VersionControlDependencyBanner",
    fun () ->
        Vitest.test (
            "shows nothing before the main process has answered",
            fun () -> Vitest.expect(bannerContent None).toEqual None
        )

        Vitest.test (
            "shows nothing while the first check runs",
            fun () ->
                let firstCheck = {
                    Statuses = [||]
                    Failure = None
                    IsChecking = true
                    CheckedAt = None
                }

                Vitest.expect(bannerContent (Some firstCheck)).toEqual None
        )

        Vitest.test (
            "shows nothing while everything works, also during a new check",
            fun () ->
                Vitest.expect(bannerContent (Some(stateOf working None false))).toEqual None
                Vitest.expect(bannerContent (Some(stateOf working None true))).toEqual None
        )

        Vitest.test (
            "names the problem of a component in the message of the library",
            fun () ->
                let content = bannerContent (Some(stateOf missingGit None false))

                Vitest
                    .expect(content)
                    .toEqual (
                        Some {
                            Message = (dependencyProblemMessage missingGit).Value
                            IsChecking = false
                        }
                    )
        )

        Vitest.test (
            "a missing git shows one line, whatever git-lfs reports",
            fun () ->
                let noGit = [| status "git" false false; status "git-lfs" false false |]

                let message = bannerContent (Some(stateOf noGit None false)) |> Option.map _.Message

                Vitest.expect(message.Value.Split('\n').Length).toBe 1
                Vitest.expect(message.Value.Contains "git-lfs").toBe false
        )

        Vitest.test (
            "keeps the warning during a new check and marks that the check runs",
            fun () ->
                let content = bannerContent (Some(stateOf missingGit None true))

                Vitest.expect(content |> Option.map _.IsChecking).toEqual (Some true)
                Vitest.expect(content |> Option.map _.Message).toEqual (dependencyProblemMessage missingGit)
        )

        Vitest.test (
            "shows the failure of the check when no component reports a problem",
            fun () ->
                let content =
                    bannerContent (Some(stateOf [||] (Some "The check could not run.") false))

                Vitest.expect(content |> Option.map _.Message).toEqual (Some "The check could not run.")
        )

        Vitest.test (
            "prefers the component problem over the failure of the check",
            fun () ->
                let content =
                    bannerContent (Some(stateOf missingGit (Some "One provider could not report.") false))

                Vitest.expect(content |> Option.map _.Message).toEqual (dependencyProblemMessage missingGit)
        )

        Vitest.test (
            "shows one line per problem when the remediation carries many lines",
            fun () ->
                // The cause ends with a colon and the raw output of the program follows.
                let cause (name: string) = $"`{name} version` exited with code 1"

                let output (name: string) = [|
                    $"{name} trace one"
                    $"{name} trace two"
                    $"{name} trace three"
                |]

                let withOutput (name: string) (installed: bool) = {
                    status name installed false with
                        Remediation = Some(String.concat "\n" (Array.append [| cause name + ":   " |] (output name)))
                }

                // A git that is too old and a git-lfs that cannot run are two problems.
                let statuses = [| withOutput "git" true; withOutput "git-lfs" false |]

                let message =
                    bannerContent (Some(stateOf statuses None false)) |> Option.map _.Message

                let lines = message.Value.Split('\n')

                Vitest.expect(lines.Length).toBe 2
                Vitest.expect(lines.[0].Contains(cause "git")).toBe true
                Vitest.expect(lines.[1].Contains(cause "git-lfs")).toBe true

                for line in lines do
                    Vitest.expect(line.EndsWith ":").toBe false
                    Vitest.expect(line.EndsWith " ").toBe false

                for name in [ "git"; "git-lfs" ] do
                    for traceLine in output name do
                        Vitest.expect(message.Value.Contains traceLine).toBe false

                // The settings keep every line.
                let full = stateProblemMessage (stateOf statuses None false)

                for name in [ "git"; "git-lfs" ] do
                    for traceLine in output name do
                        Vitest.expect(full.Value.Contains traceLine).toBe true

                Vitest.expect(full.Value.Split('\n').Length).toBe 8
        )

        Vitest.test (
            "cuts the failure of the check to its first line",
            fun () ->
                let first = "The check could not run"
                let failure = $"{first}:\nfirst line of the output\nsecond line of the output"

                let content = bannerContent (Some(stateOf [||] (Some failure) false))

                Vitest.expect(content |> Option.map _.Message).toEqual (Some first)
                Vitest.expect(stateProblemMessage (stateOf [||] (Some failure) false)).toEqual (Some failure)
        )

        Vitest.test (
            "a summary equals the full message when every remediation is one line",
            fun () ->
                let statuses = [| status "git" true false; status "git-lfs" false false |]

                Vitest.expect(dependencyProblemSummary statuses).toEqual (dependencyProblemMessage statuses)
        )

        Vitest.test (
            "a component that is installed but not compatible is a problem",
            fun () ->
                let tooOld = [| status "git" true false; status "git-lfs" true true |]

                Vitest.expect(bannerContent (Some(stateOf tooOld None false))).not.toEqual None
        )
)
