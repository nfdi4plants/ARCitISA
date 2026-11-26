namespace Arcitisa.Components.Composite.Notes.Editor

open System
open ARCtrl
open Arcitisa.Components.Shared

[<RequireQualifiedAccess>]
module NoteConversion =

    let private notesRootFolder = "notes"
    let private frontmatterDelimiter = "---"
    let noteAssetsFolderName = "assets"

    type NoteFrontmatter = {
        Title: string
        Date: DateTime
        Tags: ResizeArray<OntologyAnnotation> option
    }

    let private normalizeNewlines (content: string) =
        content.Replace("\r\n", "\n").Replace("\r", "\n")

    let private trimTrailingNewlines (content: string) = content.TrimEnd([| '\r'; '\n' |])

    let private commentEncoder (comment: Comment) =
        [
            YAMLicious.Encode.tryInclude "name" YAMLicious.Encode.string comment.Name
            YAMLicious.Encode.tryInclude "value" YAMLicious.Encode.string comment.Value
        ]
        |> YAMLicious.Encode.choose
        |> YAMLicious.Encode.object

    let private commentDecoder =
        YAMLicious.Decode.object (fun get ->
            Comment(
                ?name = get.Optional.Field "name" YAMLicious.Decode.string,
                ?value = get.Optional.Field "value" YAMLicious.Decode.string
            )
        )

    let private ontologyAnnotationEncoder (tag: OntologyAnnotation) =
        [
            YAMLicious.Encode.tryInclude "annotationValue" YAMLicious.Encode.string tag.Name
            YAMLicious.Encode.tryInclude "termSource" YAMLicious.Encode.string tag.TermSourceREF
            YAMLicious.Encode.tryInclude "termAccession" YAMLicious.Encode.string tag.TermAccessionNumber
            if tag.Comments.Count > 0 then
                "comments", YAMLicious.Encode.resizearray commentEncoder tag.Comments
        ]
        |> YAMLicious.Encode.choose
        |> YAMLicious.Encode.object

    let private ontologyAnnotationDecoder =
        YAMLicious.Decode.object (fun get ->
            OntologyAnnotation.create (
                ?name = get.Optional.Field "annotationValue" YAMLicious.Decode.string,
                ?tsr = get.Optional.Field "termSource" YAMLicious.Decode.string,
                ?tan = get.Optional.Field "termAccession" YAMLicious.Decode.string,
                ?comments = get.Optional.Field "comments" (YAMLicious.Decode.resizearray commentDecoder)
            )
        )

    let private frontmatterEncoder (frontmatter: NoteFrontmatter) =
        [
            "title", YAMLicious.Encode.string frontmatter.Title
            "date", YAMLicious.Encode.string (frontmatter.Date.ToString("yyyy-MM-dd"))
            match frontmatter.Tags with
            | Some tags when tags.Count > 0 -> "tags", YAMLicious.Encode.resizearray ontologyAnnotationEncoder tags
            | _ -> "tags", YAMLicious.Encode.nil
        ]
        |> YAMLicious.Encode.choose
        |> YAMLicious.Encode.object

    let private frontmatterDecoder =
        YAMLicious.Decode.object (fun get -> {
            Title = get.Required.Field "title" YAMLicious.Decode.string
            Date = get.Required.Field "date" YAMLicious.Decode.datetime
            Tags = get.Optional.Field "tags" (YAMLicious.Decode.resizearray ontologyAnnotationDecoder)
        })

    let encodeFrontmatter (frontmatter: NoteFrontmatter) =
        frontmatter |> frontmatterEncoder |> YAMLicious.Encode.write 2

    let private trySplitMarkdownFrontmatterEnvelope (content: string) =
        // YAMLicious decodes the YAML document after it is isolated; the Markdown body must stay raw text.
        let normalizedContent = normalizeNewlines content
        let lines = normalizedContent.Split('\n')

        if lines.Length = 0 || lines.[0].Trim() <> frontmatterDelimiter then
            None
        else
            lines
            |> Array.skip 1
            |> Array.tryFindIndex (fun line -> line.Trim() = frontmatterDelimiter)
            |> Option.map (fun closingIndexAfterOpening ->
                let closingIndex = closingIndexAfterOpening + 1

                let yamlLines =
                    if closingIndex > 1 then
                        lines.[1 .. closingIndex - 1]
                    else
                        [||]

                let bodyLines =
                    if closingIndex + 1 < lines.Length then
                        lines.[closingIndex + 1 ..]
                    else
                        [||]

                String.concat "\n" yamlLines, String.concat "\n" bodyLines
            )

    let tryDecodeMarkdownFrontmatter (content: string) =
        match trySplitMarkdownFrontmatterEnvelope content with
        | Some(yaml, body) when not (String.IsNullOrWhiteSpace yaml) ->
            try
                let frontmatter = yaml |> YAMLicious.Decode.read |> frontmatterDecoder
                Some(frontmatter, body)
            with _ ->
                None
        | _ -> None

    let formatDateFolder (dateCreated: DateTime) =
        sprintf "%04d-%02d-%02d" dateCreated.Year dateCreated.Month dateCreated.Day

    let resolveProtocolName (draft: NotesDraft) =
        Validation.sanitizeProtocolName draft.Title

    let private mkNoteMarkdownRelativePath (parentPath: string) (protocolName: string) =
        $"{parentPath}/{protocolName}/{protocolName}.md"

    let mkExistingTargetRelativePath (targetRef: ExistingTargetRef) (protocolName: string) =
        let folder =
            match targetRef.Kind with
            | NotesTargetKind.Study -> "studies"
            | NotesTargetKind.Assay -> "assays"

        if
            PathHelpers.isSafePathSegment targetRef.Name
            && PathHelpers.isSafePathSegment protocolName
        then
            Some(mkNoteMarkdownRelativePath $"{folder}/{targetRef.Name}/protocols" protocolName)
        else
            None

    let mkNewRootNoteRelativePath (dateCreated: DateTime) (protocolName: string) =
        let dateFolder = formatDateFolder dateCreated

        if
            PathHelpers.isSafePathSegment dateFolder
            && PathHelpers.isSafePathSegment protocolName
        then
            Some(mkNoteMarkdownRelativePath $"{notesRootFolder}/{dateFolder}" protocolName)
        else
            None

    let tryGetNoteFolderRelativePath (markdownPath: string) =
        let normalizedPath = PathHelpers.normalizePath markdownPath

        if normalizedPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase) then
            PathHelpers.tryGetParentPath normalizedPath
        else
            None

    let formatMarkdown (draft: NotesDraft) =
        let title =
            Validation.toOptionalString draft.Title
            |> Option.defaultWith (fun () -> invalidArg (nameof draft) "Note title is required.")

        let dateCreated = (draft.DateCreated |> Option.defaultValue DateTime.Today).Date

        let body = Validation.toOptionalString draft.MainText |> Option.defaultValue ""

        let frontmatter = {
            Title = title
            Date = dateCreated
            Tags = if draft.Tags.Count = 0 then None else Some draft.Tags
        }

        let yaml = frontmatter |> encodeFrontmatter |> trimTrailingNewlines
        let header = $"{frontmatterDelimiter}\n{yaml}\n{frontmatterDelimiter}"

        if String.IsNullOrWhiteSpace body then
            $"{header}\n"
        else
            $"{header}\n\n{body}\n"
