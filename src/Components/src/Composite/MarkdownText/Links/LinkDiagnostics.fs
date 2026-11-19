[<RequireQualifiedAccess>]
module Swate.Components.Composite.MarkdownText.Links.LinkDiagnostics

open Fable.Core
open Swate.Components.Composite.TextEditor.Types
open Swate.Components.Composite.MarkdownText.Links.Types

[<Literal>]
let Source = "links"

/// The host checks at most this many distinct paths per run. Links to later paths get no diagnostic.
/// A literal, so the Electron main process can use the same value without importing the Markdown parser.
[<Literal>]
let maxCheckedPaths = 500

let private outsideArcMessage = "This link points outside the ARC."

let private diagnostic (link: LinkCollection.CollectedLink) (severity: string) (message: string) : LintDiagnostic = {|
    from = link.From
    ``to`` = link.To
    severity = severity
    message = message
    source = Source
|}

/// Builds a lint source for the links of a Markdown document. checkTargets receives the distinct ARC paths of the
/// links in one call and answers with one state per path, in the same order.
let lint
    (baseFolder: string)
    (checkTargets: string[] -> JS.Promise<LinkTargetState[]>)
    : string -> JS.Promise<LintDiagnostic[]> =
    fun text -> promise {
        let links =
            LinkCollection.collect text
            |> Array.map (fun link -> link, LinkResolution.resolve baseFolder link.Url)

        let immediate =
            links
            |> Array.choose (fun (link, target) ->
                match target with
                | LinkTarget.Unsupported(_, reason) -> Some(diagnostic link "error" reason)
                | LinkTarget.OutsideArc _ -> Some(diagnostic link "error" outsideArcMessage)
                | _ -> None
            )

        let paths =
            links
            |> Array.choose (fun (_, target) ->
                match target with
                | LinkTarget.ArcPath(path, _) -> Some path
                | _ -> None
            )
            |> Array.distinct
            |> Array.truncate maxCheckedPaths

        if paths.Length = 0 then
            return immediate
        else
            let! outcome = promise {
                try
                    let! states = checkTargets paths

                    if states.Length <> paths.Length then
                        return Error "The host answered with a different number of results."
                    else
                        return Ok states
                with error ->
                    return Error(string error)
            }

            match outcome with
            | Error message ->
                let warning: LintDiagnostic = {|
                    from = 0
                    ``to`` = 0
                    severity = "warning"
                    message = $"The links of this document could not be checked: {message}"
                    source = Source
                |}

                return Array.append immediate [| warning |]
            | Ok states ->
                let stateOf = Array.zip paths states |> Map.ofArray

                let checkedDiagnostics =
                    links
                    |> Array.choose (fun (link, target) ->
                        match target with
                        | LinkTarget.ArcPath(path, _) ->
                            match Map.tryFind path stateOf with
                            | Some LinkTargetState.Missing -> Some(diagnostic link "error" $"Not found: {path}")
                            | Some(LinkTargetState.CaseDiffers onDisk) ->
                                Some(
                                    diagnostic
                                        link
                                        "warning"
                                        $"On disk this is \"{onDisk}\". The link breaks on systems that compare case, such as DataHUB."
                                )
                            | Some LinkTargetState.OutsideArc -> Some(diagnostic link "error" outsideArcMessage)
                            | Some(LinkTargetState.Unreadable message) -> Some(diagnostic link "warning" message)
                            | Some LinkTargetState.Exists
                            | None -> None
                        | _ -> None
                    )

                return Array.append immediate checkedDiagnostics
    }
