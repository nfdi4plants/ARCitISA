import React from "react";
import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, fn, userEvent, waitFor, within } from "storybook/test";
import { GitHistory } from "./GitHistory.fs.js";
import { errorResult, okResult } from "../../Util/Interop.fs.js";

const buildPerson = (name: string, hoursAgo: number) => ({
  Name: name,
  Email: `${name.toLowerCase().replace(" ", ".")}@example.org`,
  At: 1_700_000_000 - hoursAgo * 3600,
});

const buildBranch = (
  name: string,
  kind: "Local" | "Remote",
  isCurrent = false,
) => ({
  RefName: name,
  DisplayLabel: name,
  Kind: kind,
  IsCurrent: isCurrent,
  IsTracking: false,
});

type Commit = ReturnType<typeof buildCommit>;

const buildCommit = (
  number: number,
  summary: string,
  overrides: Partial<{
    ParentIds: string[];
    Message: string;
    Author: ReturnType<typeof buildPerson> | undefined;
    Committer: ReturnType<typeof buildPerson>;
    Branches: ReturnType<typeof buildBranch>[];
    IsLocalOnly: boolean;
  }> = {},
) => {
  const person = buildPerson(
    ["Ada Lovelace", "Grace Hopper", "Alan Turing"][number % 3],
    number * 7,
  );
  return {
    Id: `${(0xabc1230 + number).toString(16)}${"0".repeat(33)}`,
    ParentIds: [`${(0xabc1230 + number + 1).toString(16)}${"0".repeat(33)}`],
    Summary: summary,
    Message: summary,
    Committer: person,
    Author: person,
    Branches: [] as ReturnType<typeof buildBranch>[],
    IsLocalOnly: false,
    ...overrides,
  };
};

const commits: Commit[] = [
  buildCommit(0, "Add the sample sheet of the new assay", {
    Branches: [buildBranch("feature/notes", "Local", true)],
    IsLocalOnly: true,
  }),
  buildCommit(1, "Merge branch 'main' into feature/notes", {
    ParentIds: ["abc1231" + "0".repeat(33), "abc1239" + "0".repeat(33)],
    Message:
      "Merge branch 'main' into feature/notes\n\nConflicts were resolved in the protocol notes.",
    Committer: buildPerson("Grace Hopper", 2),
    Author: buildPerson("Ada Lovelace", 3),
    IsLocalOnly: true,
  }),
  buildCommit(2, "Rewrite the protocol notes for the second study", {
    Branches: [
      buildBranch("main", "Local"),
      buildBranch("origin/feature/notes", "Remote"),
      buildBranch("origin/main", "Remote"),
    ],
  }),
  buildCommit(3, "Fix a typo in the assay description"),
  buildCommit(
    4,
    "Import the measurements of the first plate and describe how they were taken in the study notes",
  ),
  buildCommit(5, "Remove the draft of the data map"),
];

const changes = [
  {
    Path: "studies/s-01/protocol.md",
    OriginalPath: undefined,
    Kind: "Modified",
  },
  {
    Path: "assays/a-01/dataset/plate-1.csv",
    OriginalPath: undefined,
    Kind: "Added",
  },
  {
    Path: "README-old.md",
    OriginalPath: undefined,
    Kind: "Deleted",
  },
  {
    Path: "notes/protocol-second-study.md",
    OriginalPath: "notes/draft.md",
    Kind: "Renamed",
  },
];

const buildHistory = (overrides: Record<string, unknown> = {}) => ({
  Commits: commits,
  HasMore: false,
  IsLoadingMore: false,
  LoadError: undefined,
  OnLoadMore: () => {},
  OnLoadCommitChanges: (_commitId: string) =>
    Promise.resolve(okResult<typeof changes, string>(changes)),
  OnSelectCommitChange: (_request: unknown) =>
    Promise.resolve(okResult<void, string>(undefined)),
  ...overrides,
});

const openSection = async (canvas: ReturnType<typeof within>) => {
  await userEvent.click(canvas.getByTestId("GitSidebarHistoryToggle"));
};

const meta = {
  title: "Page Components/GitSidebar/GitHistory",
  component: GitHistory,
  tags: ["autodocs"],
  parameters: {
    layout: "fullscreen",
  },
  decorators: [
    (Story) => (
      <div className="swt:@container/gitSidebar swt:w-[340px] swt:bg-base-100 swt:p-4">
        <Story />
      </div>
    ),
  ],
} satisfies Meta<typeof GitHistory>;

export default meta;
type Story = StoryObj<typeof meta>;

export const ClosedSection: Story = {
  args: { history: buildHistory() },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const toggle = canvas.getByTestId("GitSidebarHistoryToggle");

    await expect(toggle).toHaveAttribute("aria-expanded", "false");
    await expect(canvas.queryByTestId("GitSidebarHistoryPanel")).toBeNull();

    await userEvent.click(toggle);

    await expect(toggle).toHaveAttribute("aria-expanded", "true");
    await expect(canvas.getByTestId("GitSidebarHistoryPanel")).toBeVisible();
    await expect(canvas.getByTestId("GitSidebarHistoryCommit-0")).toHaveTextContent(
      commits[0].Summary,
    );
  },
};

export const ListWithLabels: Story = {
  args: { history: buildHistory() },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await openSection(canvas);

    await expect(canvas.getByTestId("GitSidebarHistoryNode-0")).toHaveAttribute(
      "data-state",
      "local",
    );
    await expect(canvas.getByTestId("GitSidebarHistoryNode-1")).toHaveAttribute(
      "data-state",
      "local",
    );
    await expect(canvas.getByTestId("GitSidebarHistoryNode-2")).toHaveAttribute(
      "data-state",
      "online",
    );
    await expect(canvas.getByTestId("GitSidebarHistoryBranch-0-0")).toHaveAttribute(
      "title",
      "feature/notes",
    );
    // Remote branches come before the other local branches.
    await expect(canvas.getByTestId("GitSidebarHistoryBranch-2-0")).toHaveAttribute(
      "title",
      "origin/feature/notes",
    );
    await expect(canvas.getByTestId("GitSidebarHistoryBranch-2-1")).toHaveAttribute(
      "title",
      "origin/main",
    );
    await expect(canvas.getByTestId("GitSidebarHistoryBranch-2-2")).toHaveAttribute(
      "title",
      "main",
    );
  },
};

export const ExpandedCommit: Story = {
  args: { history: buildHistory() },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await openSection(canvas);

    await userEvent.click(canvas.getByTestId("GitSidebarHistoryCommit-1"));

    await expect(
      await canvas.findByTestId("GitSidebarHistoryFile-1-3-Name"),
    ).toHaveTextContent("protocol-second-study.md");
    await expect(canvas.getByTestId("GitSidebarHistoryFile-1-3-RenamedFrom")).toHaveTextContent(
      "notes/draft.md",
    );
    await expect(canvas.getByTestId("GitSidebarHistoryFile-1-0-Name")).toHaveTextContent(
      "protocol.md",
    );
    await expect(canvas.getByTestId("GitSidebarHistoryCommitDetail-1")).toHaveTextContent(
      "Grace Hopper",
    );
  },
};

const slowLoadSpy = fn();

export const SlowFileLoad: Story = {
  args: {
    history: buildHistory({
      OnLoadCommitChanges: (commitId: string) => {
        slowLoadSpy(commitId);
        return new Promise((resolve) => {
          window.setTimeout(
            () => resolve(okResult<typeof changes, string>(changes)),
            700,
          );
        });
      },
    }),
  },
  play: async ({ canvasElement }) => {
    slowLoadSpy.mockClear();
    const canvas = within(canvasElement);
    await openSection(canvas);

    await userEvent.click(canvas.getByTestId("GitSidebarHistoryCommit-2"));

    await expect(canvas.getByTestId("GitSidebarHistoryFilesState-2")).toBeVisible();
    await expect(canvas.queryByTestId("GitSidebarHistoryFile-2-0")).toBeNull();
    await waitFor(() =>
      expect(canvas.getByTestId("GitSidebarHistoryFile-2-0")).toBeVisible(),
    );
    await expect(canvas.queryByTestId("GitSidebarHistoryFilesState-2")).toBeNull();
    await expect(slowLoadSpy).toHaveBeenCalledTimes(1);
    await expect(slowLoadSpy).toHaveBeenCalledWith(commits[2].Id);
  },
};

export const FailedFileLoad: Story = {
  args: {
    history: buildHistory({
      OnLoadCommitChanges: (_commitId: string) =>
        Promise.resolve(
          errorResult<string, typeof changes>("The commit could not be read."),
        ),
    }),
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await openSection(canvas);

    await userEvent.click(canvas.getByTestId("GitSidebarHistoryCommit-2"));

    await expect(
      await canvas.findByTestId("GitSidebarHistoryFilesState-2"),
    ).toHaveTextContent("The commit could not be read.");
  },
};

const selectChangeSpy = fn();

export const SelectingAFile: Story = {
  args: {
    history: buildHistory({
      OnSelectCommitChange: (request: unknown) => {
        selectChangeSpy(request);
        return Promise.resolve(okResult<void, string>(undefined));
      },
    }),
  },
  play: async ({ canvasElement }) => {
    selectChangeSpy.mockClear();
    const canvas = within(canvasElement);
    await openSection(canvas);
    await userEvent.click(canvas.getByTestId("GitSidebarHistoryCommit-2"));

    await userEvent.click(await canvas.findByTestId("GitSidebarHistoryFile-2-1"));

    await expect(selectChangeSpy).toHaveBeenCalledTimes(1);
    await expect(selectChangeSpy).toHaveBeenCalledWith({
      CommitId: commits[2].Id,
      Change: changes[1],
    });
    await expect(canvas.getByTestId("GitSidebarHistoryFile-2-1")).toHaveClass(
      "swt:border-success/40",
    );
  },
};

const olderCommits = [
  buildCommit(6, "Describe the sample preparation"),
  buildCommit(7, "Create the first study"),
];

function PagedHistory(props: React.ComponentProps<typeof GitHistory>) {
  const [pageCommits, setPageCommits] = React.useState(props.history.Commits);
  const [hasMore, setHasMore] = React.useState(true);
  const [isLoadingMore, setIsLoadingMore] = React.useState(false);

  return (
    <GitHistory
      history={{
        ...props.history,
        Commits: pageCommits,
        HasMore: hasMore,
        IsLoadingMore: isLoadingMore,
        OnLoadMore: () => {
          props.history.OnLoadMore();
          setIsLoadingMore(true);
          window.setTimeout(() => {
            setPageCommits((current) => [...current, ...olderCommits]);
            setHasMore(false);
            setIsLoadingMore(false);
          }, 400);
        },
      }}
    />
  );
}

const loadMoreSpy = fn();

export const LoadMore: Story = {
  render: (args) => <PagedHistory {...args} />,
  args: { history: buildHistory({ OnLoadMore: loadMoreSpy }) },
  play: async ({ canvasElement }) => {
    loadMoreSpy.mockClear();
    const canvas = within(canvasElement);
    await openSection(canvas);

    await userEvent.click(canvas.getByTestId("GitSidebarHistoryLoadMoreButton"));

    await expect(loadMoreSpy).toHaveBeenCalledTimes(1);
    await expect(canvas.getByTestId("GitSidebarHistoryLoadMoreButton")).toBeDisabled();
    await waitFor(() =>
      expect(canvas.queryByTestId("GitSidebarHistoryLoadMoreButton")).toBeNull(),
    );
    await expect(
      canvas.getByTestId(`GitSidebarHistoryCommit-${commits.length}`),
    ).toHaveTextContent(olderCommits[0].Summary);
  },
};

export const LoadFailed: Story = {
  args: {
    history: buildHistory({
      HasMore: true,
      LoadError: "The next commits could not be loaded.",
    }),
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await openSection(canvas);

    await expect(canvas.getByTestId("GitSidebarHistoryError")).toHaveTextContent(
      "The next commits could not be loaded.",
    );
  },
};

export const EmptyHistory: Story = {
  args: { history: buildHistory({ Commits: [] }) },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await openSection(canvas);

    await expect(canvas.getByTestId("GitSidebarHistoryPanel")).toBeVisible();
    await expect(canvas.queryByTestId("GitSidebarHistoryCommit-0")).toBeNull();
    await expect(canvas.queryByTestId("GitSidebarHistoryLoadMoreButton")).toBeNull();
  },
};
