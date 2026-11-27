module internal Arcitisa.Components.Tests.GitHistoryList

open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open Arcitisa.Components.Page
open Arcitisa.Components.Page.GitSidebarTypes
open Vitest

let private person name : GitSidebarCommitPerson = {
    Name = name
    Email = Some $"{name}@example.org"
    At = 1700000000.0
}

let private commit id summary : GitSidebarCommit = {
    Id = id
    ParentIds = [||]
    Summary = summary
    Message = $"{summary}\n"
    Committer = person "ada"
    Author = Some(person "ada")
    Branches = [||]
    IsLocalOnly = false
}

let private branch name kind isCurrent : GitSidebarBranchOption = {
    RefName = name
    DisplayLabel = name
    Kind = kind
    IsCurrent = isCurrent
    IsTracking = false
}

let private change path kind : GitSidebarChange = {
    Path = path
    OriginalPath = None
    Kind = kind
}

let private firstCommit =
    commit "1111111aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" "Add the first protocol"

let private secondCommit =
    commit "2222222bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" "Fix the assay sheet"

let private notesChange = change "studies/notes.md" GitSidebarChangeKind.Modified

let private history (commits: GitSidebarCommit[]) : GitSidebarHistory = {
    Commits = commits
    HasMore = false
    IsLoadingMore = false
    LoadError = None
    OnLoadMore = ignore
    OnLoadCommitChanges = fun _ -> promise { return Ok [| notesChange |] }
    OnSelectCommitChange = fun _ -> promise { return Ok() }
}

/// The history with a loader that records the id of every commit it is asked for.
let private recordingHistory (commits: GitSidebarCommit[]) (loadedIds: ResizeArray<string>) = {
    history commits with
        OnLoadCommitChanges =
            fun id ->
                loadedIds.Add id
                promise { return Ok [| notesChange |] }
}

let private render (history: GitSidebarHistory) =
    RTL.render (GitHistory.GitHistory history) |> ignore

let private openSection () =
    RTL.fireEvent.click (RTL.screen.getByTestId "GitSidebarHistoryToggle")

let private click (testId: string) =
    RTL.fireEvent.click (RTL.screen.getByTestId testId)

let private isRendered (testId: string) =
    RTL.screen.queryByTestId testId |> Option.isSome

/// The element that only assistive technology reads, if the element has one.
[<Emit("($0.querySelector(\"[class*='sr-only']\") ?? undefined)")>]
let private screenReaderOnlyPart (element: HTMLElement) : HTMLElement option = jsNative

Vitest.afterEach (fun () -> RTL.cleanup ())

Vitest.test (
    "the commit list appears when the section opens and disappears when it closes",
    fun () ->
        render (history [| firstCommit |])

        Vitest.expect(isRendered "GitSidebarHistoryPanel").toBe false
        Vitest.expect(isRendered "GitSidebarHistoryCommit-0").toBe false

        openSection ()

        Vitest.expect(isRendered "GitSidebarHistoryPanel").toBe true
        Vitest.expect(isRendered "GitSidebarHistoryCommit-0").toBe true

        openSection ()

        Vitest.expect(isRendered "GitSidebarHistoryPanel").toBe false
)

Vitest.test (
    "each commit row shows the summary of the commit at its index",
    fun () ->
        render (history [| firstCommit; secondCommit |])
        openSection ()

        Vitest
            .expect((RTL.screen.getByTestId "GitSidebarHistoryCommit-0").textContent.Contains firstCommit.Summary)
            .toBe
            true

        Vitest
            .expect((RTL.screen.getByTestId "GitSidebarHistoryCommit-1").textContent.Contains secondCommit.Summary)
            .toBe
            true
)

Vitest.test (
    "the dot of a commit reports whether the commit exists only on this computer",
    fun () ->
        render (history [| { firstCommit with IsLocalOnly = true }; secondCommit |])
        openSection ()

        Vitest.expect((RTL.screen.getByTestId "GitSidebarHistoryNode-0").getAttribute "data-state").toBe "local"
        Vitest.expect((RTL.screen.getByTestId "GitSidebarHistoryNode-1").getAttribute "data-state").toBe "online"
)

Vitest.test (
    "of several branch labels on a commit only the first shows its name",
    fun () ->
        let branches = [|
            branch "feature/notes" GitSidebarBranchKind.Local false
            branch "origin/main" GitSidebarBranchKind.Remote false
            branch "main" GitSidebarBranchKind.Local true
        |]

        render (history [| { firstCommit with Branches = branches } |])
        openSection ()

        // The current branch comes first and remote branches come before the other local ones.
        let main = RTL.screen.getByTestId "GitSidebarHistoryBranch-0-0"
        let remote = RTL.screen.getByTestId "GitSidebarHistoryBranch-0-1"
        let feature = RTL.screen.getByTestId "GitSidebarHistoryBranch-0-2"

        Vitest.expect(main.getAttribute "title").toBe "main"
        Vitest.expect(remote.getAttribute "title").toBe "origin/main"
        Vitest.expect(feature.getAttribute "title").toBe "feature/notes"

        Vitest.expect(screenReaderOnlyPart main |> Option.isNone).toBe true
        Vitest.expect((screenReaderOnlyPart remote).Value.textContent).toBe "origin/main"
        Vitest.expect((screenReaderOnlyPart feature).Value.textContent).toBe "feature/notes"
)

Vitest.test (
    "expanding a commit loads its files once and shows them",
    fun () -> promise {
        let loadedIds = ResizeArray<string>()
        render (recordingHistory [| firstCommit; secondCommit |] loadedIds)
        openSection ()

        click "GitSidebarHistoryCommit-1"

        let! name = RTL.screen.findByTestId "GitSidebarHistoryFile-1-0-Name"

        Vitest.expect(name.textContent).toBe "notes.md"
        Vitest.expect(loadedIds.ToArray()).toEqual [| secondCommit.Id |]
    }
)

Vitest.test (
    "expanding a collapsed commit again uses the stored files",
    fun () -> promise {
        let loadedIds = ResizeArray<string>()
        render (recordingHistory [| firstCommit |] loadedIds)
        openSection ()

        click "GitSidebarHistoryCommit-0"
        let! _ = RTL.screen.findByTestId "GitSidebarHistoryFile-0-0"

        click "GitSidebarHistoryCommit-0"
        Vitest.expect(isRendered "GitSidebarHistoryFile-0-0").toBe false

        click "GitSidebarHistoryCommit-0"
        Vitest.expect(isRendered "GitSidebarHistoryFile-0-0").toBe true
        Vitest.expect(loadedIds.Count).toBe 1
    }
)

Vitest.test (
    "a failed load shows its message and is asked again when the commit is expanded again",
    fun () -> promise {
        let attempts = ref 0

        let failingFirst: GitSidebarHistory = {
            history [| firstCommit |] with
                OnLoadCommitChanges =
                    fun _ ->
                        attempts.Value <- attempts.Value + 1

                        if attempts.Value = 1 then
                            promise { return Error "git could not read the commit" }
                        else
                            promise { return Ok [| notesChange |] }
        }

        render failingFirst
        openSection ()

        click "GitSidebarHistoryCommit-0"
        let! failure = RTL.screen.findByTestId "GitSidebarHistoryFilesState-0"

        Vitest.expect(failure.textContent.Contains "git could not read the commit").toBe true

        click "GitSidebarHistoryCommit-0"
        click "GitSidebarHistoryCommit-0"
        let! _ = RTL.screen.findByTestId "GitSidebarHistoryFile-0-0"

        Vitest.expect(attempts.Value).toBe 2
    }
)

Vitest.test (
    "clicking a file reports the commit id and the change",
    fun () -> promise {
        let selected = ResizeArray<GitSidebarCommitChangeRequest>()

        let withSelection: GitSidebarHistory = {
            history [| firstCommit |] with
                OnSelectCommitChange =
                    fun request ->
                        selected.Add request
                        promise { return Ok() }
        }

        render withSelection
        openSection ()
        click "GitSidebarHistoryCommit-0"
        let! _ = RTL.screen.findByTestId "GitSidebarHistoryFile-0-0"

        click "GitSidebarHistoryFile-0-0"

        Vitest.expect(selected.Count).toBe 1

        Vitest.expect(selected.[0]).toEqual {
            CommitId = firstCommit.Id
            Change = notesChange
        }
    }
)

Vitest.test (
    "a file that cannot be opened shows the error under its name",
    fun () -> promise {
        let failing: GitSidebarHistory = {
            history [| firstCommit |] with
                OnSelectCommitChange = fun _ -> promise { return Error "the file is not available locally" }
        }

        render failing
        openSection ()
        click "GitSidebarHistoryCommit-0"
        let! _ = RTL.screen.findByTestId "GitSidebarHistoryFile-0-0"

        click "GitSidebarHistoryFile-0-0"

        let! error = RTL.screen.findByTestId "GitSidebarHistoryFile-0-0-Error"

        Vitest.expect(error.textContent).toBe "the file is not available locally"
    }
)

Vitest.test (
    "the load more button calls the callback and is gone when nothing more can load",
    fun () ->
        let loadMoreCalls = ref 0

        let withMore: GitSidebarHistory = {
            history [| firstCommit |] with
                HasMore = true
                OnLoadMore = fun () -> loadMoreCalls.Value <- loadMoreCalls.Value + 1
        }

        let result = RTL.render (GitHistory.GitHistory withMore)
        openSection ()

        click "GitSidebarHistoryLoadMoreButton"

        Vitest.expect(loadMoreCalls.Value).toBe 1

        result.rerender (GitHistory.GitHistory { withMore with HasMore = false })

        Vitest.expect(isRendered "GitSidebarHistoryLoadMoreButton").toBe false
)

Vitest.test (
    "a load error shows its message",
    fun () ->
        render {
            history [| firstCommit |] with
                LoadError = Some "the next page failed"
        }

        openSection ()

        Vitest.expect((RTL.screen.getByTestId "GitSidebarHistoryError").textContent).toBe "the next page failed"
)

Vitest.test (
    "the committer line appears only when author and committer differ",
    fun () ->
        let differing = {
            firstCommit with
                Committer = person "grace"
        }

        render (history [| differing; secondCommit |])
        openSection ()
        click "GitSidebarHistoryCommit-0"
        click "GitSidebarHistoryCommit-1"

        Vitest.expect((RTL.screen.getByTestId "GitSidebarHistoryCommitDetail-0").textContent.Contains "grace").toBe true
        Vitest.expect((RTL.screen.getByTestId "GitSidebarHistoryCommitDetail-1").textContent.Contains "ada").toBe false
)

Vitest.test (
    "a new commit on top keeps an expanded commit open without loading its files again",
    fun () -> promise {
        let loadedIds = ResizeArray<string>()

        let newCommit =
            commit "3333333ccccccccccccccccccccccccccccccccc" "Add the new sheet"

        let result =
            RTL.render (GitHistory.GitHistory(recordingHistory [| firstCommit; secondCommit |] loadedIds))

        openSection ()
        click "GitSidebarHistoryCommit-0"
        let! _ = RTL.screen.findByTestId "GitSidebarHistoryFile-0-0"

        result.rerender (GitHistory.GitHistory(recordingHistory [| newCommit; firstCommit; secondCommit |] loadedIds))

        Vitest.expect(isRendered "GitSidebarHistoryFile-1-0").toBe true
        Vitest.expect(loadedIds.ToArray()).toEqual [| firstCommit.Id |]
    }
)
