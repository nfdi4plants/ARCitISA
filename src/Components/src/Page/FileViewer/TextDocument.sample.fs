namespace Arcitisa.Components.Page.TextDocumentSample

open Fable.Core
open Feliz

open Arcitisa.Components.Composite.TextEditor.Types
open Arcitisa.Components.Composite.MarkdownText.Links.Types
open Arcitisa.Components.Page.FileViewer
open Arcitisa.Components.Page.FileViewer.Types

module private TextDocumentSampleHelpers =

    let markdownText =
        """# Protocol

The samples are listed in [samples](./samples.md). The [old plan](./plan.md) is gone.

- Prepare the buffer
- Measure the samples
"""

    let diskText =
        """# Protocol

Another program changed this file.
"""

    let htmlText = "<h1>Report</h1><p>The page shows as it looks.</p>"

    /// The files of a small ARC around docs/protocol.md.
    let private files =
        Map [
            "", [| "docs"; "README.md" |]
            "docs", [| "protocol.md"; "samples.md" |]
        ]

    /// Marks links to files that are not in the small ARC.
    let checkTargets (paths: string[]) : JS.Promise<LinkTargetState[]> = promise {
        let exists (path: string) =
            let folder, name =
                match path.LastIndexOf '/' with
                | -1 -> "", path
                | i -> path.Substring(0, i), path.Substring(i + 1)

            files |> Map.tryFind folder |> Option.exists (Array.contains name)

        return
            paths
            |> Array.map (fun path ->
                if exists path then
                    LinkTargetState.Exists
                else
                    LinkTargetState.Missing
            )
    }

    /// Lists a folder of the small ARC like the app does: the names that start with the prefix, ignoring case.
    let listFolder (request: ListFolderRequest) : JS.Promise<FolderContents option> = promise {
        let prefix = request.namePrefix.ToLowerInvariant()

        return
            files
            |> Map.tryFind request.folder
            |> Option.map (fun names -> {|
                entries =
                    names
                    |> Array.filter (fun name -> name.ToLowerInvariant().StartsWith prefix)
                    |> Array.map (fun name -> {|
                        name = name
                        isDirectory = not (name.Contains ".")
                    |})
                truncated = false
            |})
    }

open TextDocumentSampleHelpers

[<Erase; Mangle(false)>]
type internal TextDocumentSample =

    /// A host that keeps the file in memory and saves after a short wait, like a write to disk. With diskText the
    /// page starts with a change on disk, which "Reload from disk" adopts through the handle.
    [<ReactComponent>]
    static member private Host
        (path: string, text: string, ?rendering: TextRendering, ?readOnlyReason: string, ?diskText: string)
        =
        let handleRef = React.useRef<TextDocumentHandle option> None
        let savedText, setSavedText = React.useState text
        let dirty, setDirty = React.useState false
        let saving, setSaving = React.useState false
        let changedOnDisk, setChangedOnDisk = React.useState diskText.IsSome

        let onSave (snapshot: TextEditorSnapshot) = promise {
            setSaving true
            do! Promise.sleep 300
            setSavedText snapshot.text
            setSaving false
            setChangedOnDisk false
            return true
        }

        let reload () =
            diskText
            |> Option.iter (fun disk ->
                setSavedText disk
                setChangedOnDisk false
                handleRef.current |> Option.iter (fun handle -> handle.replaceText disk)
            )

        let overwrite () =
            handleRef.current |> Option.iter (fun handle -> handle.save () |> ignore)

        let status =
            if readOnlyReason.IsSome then "Read-only"
            elif saving then "Saving..."
            elif dirty then "Unsaved changes"
            else "Saved"

        Html.div [
            prop.style [ style.height 420 ]
            prop.children [
                TextDocument.TextDocument(
                    path,
                    "inmemory://samples/" + path,
                    savedText,
                    status,
                    onSave,
                    ?rendering = rendering,
                    ?readOnlyReason = readOnlyReason,
                    saving = saving,
                    dirty = dirty,
                    notices = [
                        if changedOnDisk then
                            {
                                Key = "disk"
                                Message = "The file changed on disk since you opened it."
                                Actions = [
                                    {
                                        Label = "Overwrite"
                                        Run = overwrite
                                        Disabled = saving
                                    }
                                    {
                                        Label = "Reload from disk"
                                        Run = reload
                                        Disabled = saving
                                    }
                                ]
                            }
                    ],
                    onDirtyChange = setDirty,
                    checkLinkTargets = checkTargets,
                    listFolder = listFolder,
                    handleRef = handleRef
                )
            ]
        ]

    /// A Markdown file with the formatting toolbar, link warnings, path completion and a preview.
    [<ReactComponent>]
    static member Markdown() =
        TextDocumentSample.Host("docs/protocol.md", markdownText, rendering = TextRendering.Markdown)

    /// An HTML file opens as it looks. "Edit" shows its source.
    [<ReactComponent>]
    static member Html() =
        TextDocumentSample.Host("docs/report.html", htmlText, rendering = TextRendering.Html)

    /// A YAML file gets Undo and Redo without the Markdown formatting.
    [<ReactComponent>]
    static member Yaml() =
        TextDocumentSample.Host("workflows/config.yml", "title: Guide\nsteps: 2\n")

    /// A file the host cannot write. Every button that changes the text is disabled.
    [<ReactComponent>]
    static member ReadOnly() =
        TextDocumentSample.Host(
            "docs/protocol.md",
            markdownText,
            rendering = TextRendering.Markdown,
            readOnlyReason = "This file is write-protected. It opens read-only."
        )

    /// Another program changed the file. "Reload from disk" replaces the text in the editor and in an open preview.
    [<ReactComponent>]
    static member ChangedOnDisk() =
        TextDocumentSample.Host(
            "docs/protocol.md",
            markdownText,
            rendering = TextRendering.Markdown,
            diskText = diskText
        )
