namespace Arcitisa.Components.Page

open System
open Fable.Core
open Feliz
open Arcitisa.Components
open Arcitisa.Components.Page.GitSidebarTypes
open Arcitisa.Components.Page.GitSidebarHelper

module private GitHistoryTypes =

    /// The files of an expanded commit. Failed holds the message of the failed load.
    [<RequireQualifiedAccess>]
    type CommitFiles =
        | Loading
        | Loaded of GitSidebarChange[]
        | Failed of string

    /// The file the user clicked last. ErrorText is the answer to opening it when that was an error.
    type ClickedFile = {
        CommitId: string
        Path: string
        ErrorText: string option
    }

    type RowKind =
        | CommitRow of commitIndex: int
        | DetailRow of commitIndex: int
        | StateRow of commitIndex: int * files: CommitFiles
        | FileRow of commitIndex: int * fileIndex: int * change: GitSidebarChange
        | LoadMoreRow
        | ErrorRow of message: string

    /// One row of the list. HasUpperLine and HasLowerLine say which halves of the row the graph line runs through.
    type HistoryRow = {
        Key: string
        Kind: RowKind
        IsLocalOnly: bool
        HasUpperLine: bool
        HasLowerLine: bool
    }

    [<NoEquality; NoComparison>]
    type HistoryListProps = {
        History: GitSidebarHistory
        PanelId: string
        Expanded: Set<string>
        Files: Map<string, CommitFiles>
        ClickedFile: ClickedFile option
        ToggleCommit: GitSidebarCommit -> unit
        SelectFile: string -> GitSidebarChange -> unit
    }

    [<NoEquality; NoComparison>]
    type RowShellProps = {
        Index: int
        Start: int
        MeasureElement: VirtualMeasureElementRef
        Row: HistoryRow
        Dot: ReactElement
        Content: ReactElement
    }

module private GitHistoryRows =
    open GitHistoryTypes

    /// The colour of a commit that exists only on this computer and of one that is also online. The graph and
    /// the branch labels take their colours from these classes.
    let localClassName = "swt:text-primary"
    let onlineClassName = "swt:text-base-content/40"
    /// A label needs more contrast than a line, so a remote branch label uses a stronger opacity than the graph.
    let remoteLabelClassName = "swt:text-base-content/60"

    let stateClassName (isLocalOnly: bool) =
        if isLocalOnly then localClassName else onlineClassName

    /// The author, or the committer when the provider records no author.
    let personOf (commit: GitSidebarCommit) : GitSidebarCommitPerson =
        commit.Author |> Option.defaultValue commit.Committer

    let dateOf (person: GitSidebarCommitPerson) : DateTime =
        DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds person.At

    let formatDate (date: DateTime) : string =
        date.ToLocalTime().ToString("yyyy-MM-dd HH:mm")

    let shortId (id: string) : string = id.Substring(0, min 7 id.Length)

    /// The current branch comes first and remote branches come before the other local ones. Each group keeps its order.
    let orderBranches (branches: GitSidebarBranchOption[]) : GitSidebarBranchOption[] =
        Array.concat [
            branches |> Array.filter _.IsCurrent
            branches
            |> Array.filter (fun branch -> not branch.IsCurrent && branch.Kind = GitSidebarBranchKind.Remote)
            branches
            |> Array.filter (fun branch -> not branch.IsCurrent && branch.Kind = GitSidebarBranchKind.Local)
        ]

    /// The rows of the list, top to bottom. The graph line stops at the last commit when nothing more can load.
    let buildRows
        (commits: GitSidebarCommit[])
        (expanded: Set<string>)
        (files: Map<string, CommitFiles>)
        (hasMore: bool)
        (loadError: string option)
        : HistoryRow[] =
        let lastIndex = commits.Length - 1

        let rowsOfCommit (index: int) (commit: GitSidebarCommit) =
            let continues = index < lastIndex || hasMore

            let commitRow = {
                Key = commit.Id
                Kind = CommitRow index
                IsLocalOnly = commit.IsLocalOnly
                HasUpperLine = index > 0
                HasLowerLine = continues
            }

            let rowUnder (suffix: string) (kind: RowKind) = {
                Key = $"{commit.Id}:{suffix}"
                Kind = kind
                IsLocalOnly = commit.IsLocalOnly
                HasUpperLine = continues
                HasLowerLine = continues
            }

            if Set.contains commit.Id expanded then
                let fileRows =
                    match Map.tryFind commit.Id files with
                    | Some(CommitFiles.Loaded changes) ->
                        changes
                        |> Array.mapi (fun fileIndex change ->
                            rowUnder $"file:{change.Path}" (FileRow(index, fileIndex, change))
                        )
                    | Some state -> [| rowUnder "state" (StateRow(index, state)) |]
                    | None -> [||]

                Array.append [| commitRow; rowUnder "detail" (DetailRow index) |] fileRows
            else
                [| commitRow |]

        let rowAfterLastCommit (key: string) (kind: RowKind) = {
            Key = key
            Kind = kind
            IsLocalOnly = commits.Length > 0 && commits.[lastIndex].IsLocalOnly
            HasUpperLine = hasMore
            HasLowerLine = hasMore
        }

        Array.concat [
            commits |> Array.mapi rowsOfCommit |> Array.concat
            (if hasMore then
                 [| rowAfterLastCommit "load-more" LoadMoreRow |]
             else
                 [||])
            (match loadError with
             | Some message -> [| rowAfterLastCommit "error" (ErrorRow message) |]
             | None -> [||])
        ]

    let estimatedSize (row: HistoryRow) : int =
        match row.Kind with
        | CommitRow _
        | FileRow _ -> 28
        | DetailRow _ -> 96
        | StateRow _
        | ErrorRow _ -> 32
        | LoadMoreRow -> 36

    /// Where the graph line runs in the graph column, or None for a row without a line.
    let linePosition (row: HistoryRow) : string option =
        match row.HasUpperLine, row.HasLowerLine with
        | true, true -> Some "swt:inset-y-0"
        | true, false -> Some "swt:top-0 swt:h-1/2"
        | false, true -> Some "swt:bottom-0 swt:h-1/2"
        | false, false -> None

open GitHistoryTypes

[<Erase; Mangle(false)>]
type GitHistory =

    /// The dot of a commit in the graph column. The first commit of the list is the current commit and has a ring.
    /// A merge commit is a ring without fill.
    [<ReactComponent>]
    static member private Dot(index: int, commit: GitSidebarCommit) =
        let shape =
            match index = 0, commit.ParentIds.Length > 1 with
            | false, false -> "swt:size-2 swt:bg-current"
            | true, false -> "swt:size-2.5 swt:bg-current swt:outline-2 swt:outline-offset-1 swt:outline-current"
            | false, true -> "swt:size-2.5 swt:border-2 swt:border-current swt:bg-base-100"
            | true, true -> "swt:size-3.5 swt:border-2 swt:border-current swt:bg-base-100"

        let state, description =
            if commit.IsLocalOnly then
                "local", "Only on this computer"
            else
                "online", "Also online"

        Html.span [
            prop.testId $"GitSidebarHistoryNode-{index}"
            prop.custom ("data-state", state)
            prop.className [
                "swt:absolute swt:top-1/2 swt:left-1/2 swt:-translate-x-1/2 swt:-translate-y-1/2 swt:rounded-full"
                shape
            ]
            prop.children [
                Html.span [ prop.className "swt:sr-only"; prop.text description ]
            ]
        ]

    /// The first label of a commit shows the branch name. The others show the icon only.
    [<ReactComponent>]
    static member private BranchLabel(commitIndex: int, branchIndex: int, branch: GitSidebarBranchOption) =
        let icon, colorClassName =
            match branch.Kind with
            | GitSidebarBranchKind.Local -> "swt:fluent--branch-fork-24-regular", GitHistoryRows.localClassName
            | GitSidebarBranchKind.Remote -> "swt:fluent--cloud-24-regular", GitHistoryRows.remoteLabelClassName

        let name =
            if branchIndex = 0 then
                Html.span [
                    prop.className "swt:max-w-16 swt:truncate"
                    prop.text branch.DisplayLabel
                ]
            else
                Html.span [
                    prop.className "swt:sr-only"
                    prop.text branch.DisplayLabel
                ]

        Html.span [
            prop.testId $"GitSidebarHistoryBranch-{commitIndex}-{branchIndex}"
            prop.className [
                "swt:badge swt:badge-outline swt:badge-sm swt:gap-0.5"
                colorClassName
            ]
            prop.title branch.DisplayLabel
            prop.children [
                Html.span [
                    prop.ariaHidden true
                    prop.className $"swt:iconify {icon} swt:size-3 swt:shrink-0"
                ]
                name
            ]
        ]

    [<ReactComponent>]
    static member private CommitBox
        (index: int, commit: GitSidebarCommit, isExpanded: bool, toggle: GitSidebarCommit -> unit)
        =
        let person = GitHistoryRows.personOf commit

        let title =
            $"{person.Name}, {GitHistoryRows.formatDate (GitHistoryRows.dateOf person)}, {GitHistoryRows.shortId commit.Id}"

        let labels =
            GitHistoryRows.orderBranches commit.Branches
            |> Array.mapi (fun branchIndex branch -> GitHistory.BranchLabel(index, branchIndex, branch))

        let labelList =
            if labels.Length = 0 then
                Html.none
            else
                Html.span [
                    prop.className "swt:ml-auto swt:flex swt:shrink-0 swt:items-center swt:gap-1"
                    prop.children labels
                ]

        Html.button [
            prop.testId $"GitSidebarHistoryCommit-{index}"
            prop.type'.button
            prop.ariaExpanded isExpanded
            prop.title title
            prop.className [ rowBoxClassName; "swt:cursor-pointer"; idleRowClassName ]
            prop.onClick (fun _ -> toggle commit)
            prop.children [
                // One truncating line: the ellipsis cuts the author first and the summary only when it does not fit alone.
                Html.span [
                    prop.className "swt:min-w-0 swt:truncate swt:text-sm swt:@max-xs:text-xs"
                    prop.children [
                        Html.span [ prop.text commit.Summary ]
                        // The space keeps assistive tech from reading summary and author as one word.
                        Html.text " "
                        Html.span [
                            prop.className "swt:ml-1 swt:text-xs swt:text-base-content/60"
                            prop.text person.Name
                        ]
                    ]
                ]
                labelList
            ]
        ]

    [<ReactComponent>]
    static member private DetailBox(index: int, commit: GitSidebarCommit) =
        let date = GitHistoryRows.dateOf (GitHistoryRows.personOf commit)

        let committerLine =
            match commit.Author with
            | Some author when author.Name <> commit.Committer.Name || author.Email <> commit.Committer.Email ->
                Html.div [
                    prop.className "swt:text-base-content/60 swt:wrap-anywhere"
                    prop.text (
                        match commit.Committer.Email with
                        | Some email -> $"Committed by {commit.Committer.Name} <{email}>"
                        | None -> $"Committed by {commit.Committer.Name}"
                    )
                ]
            | _ -> Html.none

        Html.div [
            prop.testId $"GitSidebarHistoryCommitDetail-{index}"
            prop.className
                "swt:flex swt:w-full swt:min-w-0 swt:flex-col swt:gap-1 swt:rounded-box swt:border swt:border-base-content/10 swt:bg-base-200/40 swt:px-2 swt:py-1.5 swt:text-xs"
            prop.children [
                Html.div [
                    prop.className "swt:flex swt:flex-wrap swt:gap-x-2 swt:text-base-content/60"
                    prop.children [
                        Html.span [
                            prop.text (DateTimeExtensions.timeAgo DateTime.UtcNow date)
                        ]
                        Html.span [ prop.text (GitHistoryRows.formatDate date) ]
                        Html.span [
                            prop.className "swt:font-mono"
                            prop.title commit.Id
                            prop.text (GitHistoryRows.shortId commit.Id)
                        ]
                    ]
                ]
                Html.div [
                    prop.className "swt:whitespace-pre-wrap swt:wrap-anywhere swt:text-sm swt:@max-xs:text-xs"
                    prop.text (commit.Message.TrimEnd())
                ]
                committerLine
            ]
        ]

    [<ReactComponent>]
    static member private FileBox
        (
            commitId: string,
            commitIndex: int,
            fileIndex: int,
            change: GitSidebarChange,
            clicked: ClickedFile option,
            select: string -> GitSidebarChange -> unit
        ) =
        let testId = $"GitSidebarHistoryFile-{commitIndex}-{fileIndex}"

        let colorClassName =
            if clicked.IsSome then
                markedRowClassName
            else
                idleRowClassName

        let errorText =
            match clicked |> Option.bind (fun file -> file.ErrorText) with
            | Some message ->
                Html.div [
                    prop.testId $"{testId}-Error"
                    prop.className "swt:mt-0.5 swt:text-xs swt:text-error swt:wrap-anywhere"
                    prop.text message
                ]
            | None -> Html.none

        Html.button [
            prop.testId testId
            prop.type'.button
            prop.title change.Path
            prop.className [ rowBoxClassName; "swt:cursor-pointer"; colorClassName ]
            prop.onClick (fun _ -> select commitId change)
            prop.children [
                Html.div [
                    prop.className "swt:min-w-0 swt:flex-1"
                    prop.children [
                        GitSidebarParts.ChangeLabel(
                            change,
                            $"{testId}-Name",
                            $"{testId}-Directory",
                            $"{testId}-RenamedFrom"
                        )
                        errorText
                    ]
                ]
                GitSidebarParts.ChangeStatusIcon(change, $"{testId}-Status")
            ]
        ]

    /// The row of an expanded commit while its files load or when loading them failed.
    [<ReactComponent>]
    static member private FilesState(commitIndex: int, files: CommitFiles) =
        let content =
            match files with
            | CommitFiles.Failed message ->
                Html.span [
                    prop.className "swt:min-w-0 swt:text-error swt:wrap-anywhere"
                    prop.text message
                ]
            | _ ->
                React.Fragment [
                    Html.span [
                        prop.className "swt:loading swt:loading-spinner swt:loading-xs"
                    ]
                    Html.span [ prop.text "Loading files" ]
                ]

        Html.div [
            prop.testId $"GitSidebarHistoryFilesState-{commitIndex}"
            prop.className
                "swt:flex swt:min-h-6 swt:min-w-0 swt:items-center swt:gap-2 swt:px-2 swt:text-xs swt:text-base-content/60"
            prop.children [ content ]
        ]

    [<ReactComponent>]
    static member private LoadMoreBox(isLoadingMore: bool, onLoadMore: unit -> unit) =
        let spinner =
            if isLoadingMore then
                Html.span [
                    prop.className "swt:loading swt:loading-spinner swt:loading-xs"
                ]
            else
                Html.none

        Html.div [
            prop.className "swt:flex swt:min-h-6 swt:items-center swt:px-2"
            prop.children [
                Html.button [
                    prop.testId "GitSidebarHistoryLoadMoreButton"
                    prop.type'.button
                    prop.className "swt:btn swt:btn-ghost swt:btn-xs swt:gap-1"
                    prop.disabled isLoadingMore
                    prop.onClick (fun _ -> onLoadMore ())
                    prop.children [ spinner; Html.span [ prop.text "Load more" ] ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member private ErrorBox(message: string) =
        Html.div [
            prop.testId "GitSidebarHistoryError"
            prop.className "swt:min-h-6 swt:min-w-0 swt:px-2 swt:text-xs swt:text-error swt:wrap-anywhere"
            prop.text message
        ]

    /// The row wrapper that the virtualizer positions and measures. It holds the graph column and the row box.
    /// The 2px padding around the box gives 4px between two boxes while the graph line stays unbroken.
    [<ReactComponent>]
    static member private RowShell(props: RowShellProps) =
        let row = props.Row

        let line =
            match GitHistoryRows.linePosition row with
            | Some position ->
                Html.span [
                    prop.ariaHidden true
                    prop.className $"swt:absolute swt:left-1/2 swt:w-px swt:-translate-x-1/2 swt:bg-current {position}"
                ]
            | None -> Html.none

        Html.div [
            prop.role "listitem"
            prop.custom ("data-index", props.Index)
            prop.ref (fun element -> props.MeasureElement(Option.ofObj element))
            prop.className "swt:absolute swt:left-0 swt:flex swt:w-full"
            prop.style [
                style.top 0
                style.left 0
                style.width (length.percent 100)
                style.custom ("transform", $"translateY({props.Start}px)")
            ]
            prop.children [
                Html.div [
                    prop.className [
                        "swt:relative swt:w-5 swt:shrink-0 swt:self-stretch"
                        GitHistoryRows.stateClassName row.IsLocalOnly
                    ]
                    prop.children [ line; props.Dot ]
                ]
                Html.div [
                    prop.className "swt:min-w-0 swt:flex-1 swt:py-0.5"
                    prop.children [ props.Content ]
                ]
            ]
        ]

    static member private RenderRow
        (props: HistoryListProps, row: HistoryRow, item: VirtualRow, measureElement: VirtualMeasureElementRef)
        =
        let commits = props.History.Commits

        let dot, content =
            match row.Kind with
            | CommitRow index ->
                let commit = commits.[index]

                GitHistory.Dot(index, commit),
                GitHistory.CommitBox(index, commit, Set.contains commit.Id props.Expanded, props.ToggleCommit)
            | DetailRow index -> Html.none, GitHistory.DetailBox(index, commits.[index])
            | StateRow(index, files) -> Html.none, GitHistory.FilesState(index, files)
            | FileRow(commitIndex, fileIndex, change) ->
                let commitId = commits.[commitIndex].Id

                let clicked =
                    props.ClickedFile
                    |> Option.filter (fun file -> file.CommitId = commitId && file.Path = change.Path)

                Html.none, GitHistory.FileBox(commitId, commitIndex, fileIndex, change, clicked, props.SelectFile)
            | LoadMoreRow -> Html.none, GitHistory.LoadMoreBox(props.History.IsLoadingMore, props.History.OnLoadMore)
            | ErrorRow message -> Html.none, GitHistory.ErrorBox message

        React.KeyedFragment(
            row.Key,
            [
                GitHistory.RowShell(
                    {
                        Index = item.Index
                        Start = item.Start
                        MeasureElement = measureElement
                        Row = row
                        Dot = dot
                        Content = content
                    }
                )
            ]
        )

    /// The panel with the scrolling list. It is rendered only while the section is open, since the virtualizer
    /// needs the scroll element.
    [<ReactComponent>]
    static member private List(props: HistoryListProps) =
        let history = props.History

        let rows =
            React.useMemo (
                (fun () ->
                    GitHistoryRows.buildRows
                        history.Commits
                        props.Expanded
                        props.Files
                        history.HasMore
                        history.LoadError
                ),
                [|
                    box history.Commits
                    box props.Expanded
                    box props.Files
                    box history.HasMore
                    box history.LoadError
                |]
            )

        let getItemKey = React.useCallback ((fun index -> rows.[index].Key), [| box rows |])

        let virtualRows =
            useVirtualRows rows.Length getItemKey (fun index -> GitHistoryRows.estimatedSize rows.[index]) 0 10

        let rowElements =
            virtualRows.Rows
            |> Array.map (fun item -> GitHistory.RenderRow(props, rows.[item.Index], item, virtualRows.MeasureElement))

        let content =
            if rows.Length = 0 then
                Html.div [
                    prop.className emptyListClassName
                    prop.text "No commits yet."
                ]
            else
                Html.div [
                    prop.role "list"
                    prop.className "swt:relative swt:min-w-0"
                    prop.style [ style.height virtualRows.TotalSize ]
                    prop.children rowElements
                ]

        Html.div [
            prop.id props.PanelId
            prop.testId "GitSidebarHistoryPanel"
            prop.role "region"
            prop.ariaLabel "Commit history"
            prop.className "swt:min-w-0 swt:px-2 swt:pb-2 swt:pt-1 swt:@max-xs:px-1"
            prop.children [
                Html.div [
                    prop.testId "GitSidebarHistoryScrollContainer"
                    prop.ref virtualRows.ScrollContainerRef
                    prop.className "swt:max-h-80 swt:min-w-0 swt:overflow-y-auto swt:overflow-x-hidden"
                    prop.children [ content ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member GitHistory(history: GitSidebarHistory) =
        let isOpen, setIsOpen = React.useState false
        let panelId = React.useId ()
        let expanded, setExpanded = React.useStateWithUpdater Set.empty<string>
        let files, setFiles = React.useStateWithUpdater Map.empty<string, CommitFiles>

        let clickedFile, setClickedFile =
            React.useStateWithUpdater (None: ClickedFile option)

        let loadFiles (commitId: string) =
            setFiles (Map.add commitId CommitFiles.Loading)

            history.OnLoadCommitChanges commitId
            |> Promise.iter (fun result ->
                let answer =
                    match result with
                    | Ok changes -> CommitFiles.Loaded changes
                    | Error message -> CommitFiles.Failed message

                setFiles (Map.add commitId answer)
            )

        // A failed load is asked again on the next expansion.
        let toggleCommit (commit: GitSidebarCommit) =
            if Set.contains commit.Id expanded then
                setExpanded (Set.remove commit.Id)
            else
                setExpanded (Set.add commit.Id)

                match Map.tryFind commit.Id files with
                | Some(CommitFiles.Loading)
                | Some(CommitFiles.Loaded _) -> ()
                | Some(CommitFiles.Failed _)
                | None -> loadFiles commit.Id

        let selectFile (commitId: string) (change: GitSidebarChange) =
            let isClicked (file: ClickedFile) =
                file.CommitId = commitId && file.Path = change.Path

            setClickedFile (fun _ ->
                Some {
                    CommitId = commitId
                    Path = change.Path
                    ErrorText = None
                }
            )

            history.OnSelectCommitChange { CommitId = commitId; Change = change }
            |> Promise.iter (fun result ->
                match result with
                | Ok() -> ()
                | Error message ->
                    setClickedFile (
                        Option.map (fun file ->
                            if isClicked file then
                                { file with ErrorText = Some message }
                            else
                                file
                        )
                    )
            )

        let chevron =
            if isOpen then
                "swt:iconify swt:fluent--chevron-down-24-regular swt:size-4 swt:shrink-0"
            else
                "swt:iconify swt:fluent--chevron-right-24-regular swt:size-4 swt:shrink-0"

        let header =
            Html.button [
                prop.testId "GitSidebarHistoryToggle"
                prop.type'.button
                prop.ariaExpanded isOpen
                prop.custom ("aria-controls", panelId)
                prop.className [
                    sectionTitleClassName
                    "swt:flex swt:w-full swt:min-w-0 swt:cursor-pointer swt:items-center swt:gap-1 swt:px-3 swt:pt-3 swt:pb-2 swt:text-left swt:transition-colors swt:hover:text-base-content swt:@max-xs:px-2"
                ]
                prop.onClick (fun _ -> setIsOpen (not isOpen))
                prop.children [
                    Html.span [ prop.ariaHidden true; prop.className chevron ]
                    Html.span [
                        prop.className "swt:min-w-0 swt:truncate"
                        prop.text "History"
                    ]
                ]
            ]

        let panel =
            if isOpen then
                GitHistory.List(
                    {
                        History = history
                        PanelId = panelId
                        Expanded = expanded
                        Files = files
                        ClickedFile = clickedFile
                        ToggleCommit = toggleCommit
                        SelectFile = selectFile
                    }
                )
            else
                Html.none

        Html.div [
            prop.className "swt:flex swt:min-w-0 swt:shrink-0 swt:flex-col"
            prop.children [ header; panel ]
        ]
