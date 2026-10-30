module Renderer.Components.Helper.MetadataFormatting

open System

let format (metadata: string) =
    let lines =
        metadata.Split([| '\n' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun line ->
            line
                .Trim()
                .Replace("{", "")
                .Replace("}", "")
                .Replace("[", "")
                .Replace("]", "")
                .Replace("\"", "")
                .TrimEnd(',')
                .Trim()
        )
        |> Array.filter (fun line -> not (String.IsNullOrWhiteSpace line))

    let _, uniqueLines =
        lines
        |> Array.fold
            (fun (seenDimensions, result) line ->
                let dimensionKey =
                    match line.IndexOf(':') with
                    | index when index >= 0 ->
                        let key = line.Substring(0, index).Trim().ToLowerInvariant()

                        if key = "width" || key = "height" then Some key else None
                    | _ -> None

                match dimensionKey with
                | Some key when Set.contains key seenDimensions -> seenDimensions, result
                | Some key -> Set.add key seenDimensions, Array.append result [| line |]
                | None -> seenDimensions, Array.append result [| line |]
            )
            (Set.empty, [||])

    uniqueLines |> String.concat "\n"
