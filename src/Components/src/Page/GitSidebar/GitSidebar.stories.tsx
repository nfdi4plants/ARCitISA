import React from "react";
import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, fireEvent, fn, userEvent, waitFor, within } from "storybook/test";
import { Main as GitSidebarComponent } from "./GitSidebar.fs.js";
import { okResult } from "../../Util/Interop.fs.js";

const noop = () => {};
const noopWithArg = (_arg: unknown) => {};
const noopWithMessage = (_message: string) => {};
const noopWithSelection = (_request: unknown) => {};
const noopWithPaths = (_paths: string[]) => {};
const noopWithBranch = (_branchName: string) => {};
const noopWithThreshold = (_thresholdMb: number) => {};
const noopWithDownloadPreference = (_downloadLargeFiles: boolean) => {};
const noopOpenRemoteRepository = () => {};
const cancelOperationSpy = fn();
const noopSelectChange = (_change: unknown) =>
  Promise.resolve(okResult<void, string>(undefined));

const baseCallbacks = {
  OnRefresh: noop,
  OnFetch: noop,
  OnPush: noop,
  OnUpdateFromOnline: noop,
  OnPrimarySaveSelection: noopWithSelection,
  OnPrimarySaveAll: noopWithMessage,
  OnCommitSelection: noopWithSelection,
  OnCommitAll: noopWithMessage,
  OnDiscardSelection: noopWithPaths,
  OnConfirmPendingRemoteAction: noop,
  OnCancelPendingRemoteAction: noop,
  OnSaveDownloadLargeFiles: noopWithDownloadPreference,
  OnSaveLfsAutoTrackThreshold: noopWithThreshold,
  OnSaveDiffIndexingLimit: noopWithThreshold,
  OnCreateBranch: noopWithArg,
  OnSwitchBranch: noopWithBranch,
  OnSelectChange: noopSelectChange,
  OnPruneLfsCache: noop,
  OnDedupLfsStorage: noop,
  OnCancelOperation: noop,
};

const buildCallbacks = (
  overrides: Partial<typeof baseCallbacks> = {},
) => ({
  ...baseCallbacks,
  ...overrides,
});

const buildBusyRunStatus = (notice: string) => ({
  // Fable DU: GitSidebarRunStatus.Busy
  tag: 1,
  fields: [notice],
});

const buildProgressRunStatus = (progress: {
  Method?: string;
  Stage?: string;
  ProgressPercent?: number;
  Output?: string;
}) => ({
  // Fable DU: GitSidebarRunStatus.Progress
  tag: 2,
  fields: [progress],
});

const baseStatus = {
  CurrentBranch: "feature/git-sidebar",
  TrackingBranch: "origin/feature/git-sidebar",
  Ahead: 2,
  Behind: 1,
  IsClean: false,
  IsMergeInProgress: false,
};

const branchOptions = [
  {
    RefName: "feature/git-sidebar",
    DisplayLabel: "feature/git-sidebar",
    Kind: "Local",
    IsCurrent: true,
    IsTracking: false,
  },
  {
    RefName: "main",
    DisplayLabel: "main",
    Kind: "Local",
    IsCurrent: false,
    IsTracking: false,
  },
  {
    RefName: "origin/main",
    DisplayLabel: "origin/main",
    Kind: "Remote",
    IsCurrent: false,
    IsTracking: true,
  },
] as const;

const changedFiles = [
  {
    Path: "README.md",
    OriginalPath: undefined,
    IndexStatus: "M",
    WorkingTreeStatus: " ",
    IsConflicted: false,
  },
  {
    Path: "assays/isa.assay.xlsx",
    OriginalPath: undefined,
    IndexStatus: "?",
    WorkingTreeStatus: "?",
    IsConflicted: false,
  },
  {
    Path: "notes/protocol.md",
    OriginalPath: "notes/protocol-draft.md",
    IndexStatus: "R",
    WorkingTreeStatus: "M",
    IsConflicted: false,
  },
] as const;

const conflictedFiles = [
  ...changedFiles,
  {
    Path: "studies/s-study-01/protocol.md",
    OriginalPath: undefined,
    IndexStatus: "U",
    WorkingTreeStatus: "U",
    IsConflicted: true,
  },
] as const;

const largeChangedFiles = Array.from({ length: 400 }, (_, index) => ({
  Path: `src/file-${String(index).padStart(3, "0")}.txt`,
  OriginalPath: undefined,
  IndexStatus: "M",
  WorkingTreeStatus: " ",
  IsConflicted: false,
}));

function StatefulSidebar(
  props: React.ComponentProps<typeof GitSidebarComponent>,
) {
  const [selectedFile, setSelectedFile] = React.useState<string | undefined>(
    props.selectedFile,
  );

  return (
    <GitSidebarComponent
      {...props}
      selectedFile={selectedFile}
      callbacks={{
        ...props.callbacks,
        OnSelectChange: (change) => {
          setSelectedFile(change.Path);
          return props.callbacks.OnSelectChange(change);
        },
      }}
    />
  );
}

const meta = {
  title: "Page Components/GitSidebar",
  component: GitSidebarComponent,
  tags: ["autodocs"],
  parameters: {
    layout: "fullscreen",
  },
  decorators: [
    (Story) => (
      <div className="swt:h-[760px] swt:w-[340px] swt:bg-base-200 swt:p-4">
        <Story />
      </div>
    ),
  ],
  render: (args) => <StatefulSidebar {...args} />,
} satisfies Meta<typeof GitSidebarComponent>;

export default meta;
type Story = StoryObj<typeof meta>;

export const CleanRepo: Story = {
  args: {
    status: {
      ...baseStatus,
      IsClean: true,
      Ahead: 0,
      Behind: 0,
    },
    changedFiles: [],
    branchOptions: branchOptions.slice(),
    callbacks: buildCallbacks(),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByTestId("GitSidebar")).toHaveTextContent(
      "Your repository is in sync.",
    );
  },
};

export const CleanRepoWithoutUpstream: Story = {
  args: {
    status: {
      ...baseStatus,
      TrackingBranch: undefined,
      IsClean: true,
      Ahead: 0,
      Behind: 0,
    },
    changedFiles: [],
    branchOptions: branchOptions.slice(),
    callbacks: buildCallbacks(),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByTestId("GitSidebar")).toHaveTextContent(
      "No changed files.",
    );
    await expect(canvas.getByTestId("GitSidebar")).not.toHaveTextContent(
      "Your repository is in sync.",
    );
  },
};

export const NotPublishedYet: Story = {
  args: {
    status: {
      ...baseStatus,
      TrackingBranch: undefined,
      IsClean: true,
      Ahead: 0,
      Behind: 0,
    },
    changedFiles: [],
    branchOptions: branchOptions.slice(),
    callbacks: buildCallbacks(),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
    hasRemote: false,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByTestId("GitSidebar")).toHaveTextContent(
      "Not published yet. Save to create the online ARC.",
    );
  },
};

export const ChangedFiles: Story = {
  args: {
    status: baseStatus,
    changedFiles: changedFiles.slice(),
    branchOptions: branchOptions.slice(),
    selectedFile: "README.md",
    callbacks: buildCallbacks(),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByTestId("GitSidebarChangeName-1")).toHaveTextContent("isa.assay.xlsx");
    await expect(canvas.getByTestId("GitSidebarChangeDirectory-1")).toHaveTextContent("assays");
    await expect(canvas.queryByTestId("GitSidebarChangeDirectory-0")).toBeNull();
    await expect(canvas.getByTestId("GitSidebarChangeRow-1").offsetHeight).toBe(
      canvas.getByTestId("GitSidebarChangeRow-0").offsetHeight,
    );
    await expect(canvas.getByTestId("GitSidebarChangeRenamedFrom-2")).toHaveAttribute(
      "title",
      "notes/protocol-draft.md",
    );
    await expect(canvas.getByTestId("GitSidebarAheadCount")).toHaveTextContent(/^2$/);
    await expect(canvas.getByTestId("GitSidebarBehindCount")).toHaveTextContent(/^1$/);
    await expect(
      canvas.getByTestId("GitSidebarChangedFilesVirtualContent"),
    ).toBeInTheDocument();
  },
};

export const MarkedSelectionWithoutLastClickedHighlight: Story = {
  render: (args) => <StatefulSidebar {...args} />,
  args: {
    status: baseStatus,
    changedFiles: changedFiles.slice(),
    selectedFile: "README.md",
    branchOptions: branchOptions.slice(),
    callbacks: buildCallbacks(),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await expect(canvas.getByTestId("GitSidebarChangeRow-0")).not.toHaveClass("swt:border-primary/40");
    await expect(canvas.getByTestId("GitSidebarChangeRow-0")).not.toHaveClass("swt:bg-primary/5");

    await userEvent.click(canvas.getByTestId("GitSidebarChangeRow-1"));
    await expect(canvas.getByTestId("GitSidebarChangeRow-1")).toHaveClass("swt:border-success/40");
    await expect(canvas.getByTestId("GitSidebarChangeRow-1")).toHaveClass("swt:bg-success/10");

    await userEvent.click(canvas.getByTestId("GitSidebarChangeRow-0"));
    await expect(canvas.getByTestId("GitSidebarChangeRow-0")).toHaveClass("swt:border-success/40");
    await expect(canvas.getByTestId("GitSidebarChangeRow-1")).not.toHaveClass("swt:border-success/40");
    await expect(canvas.getByTestId("GitSidebarChangeRow-1")).not.toHaveClass("swt:bg-success/10");
  },
};

export const SlowOpenDoesNotGreyRows: Story = {
  args: {
    status: baseStatus,
    changedFiles: changedFiles.slice(),
    branchOptions: branchOptions.slice(),
    callbacks: buildCallbacks({
      OnSelectChange: () =>
        new Promise((resolve) => {
          window.setTimeout(() => resolve(okResult<void, string>(undefined)), 250);
        }),
    }),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await userEvent.click(canvas.getByTestId("GitSidebarChangeRow-0"));
    await expect(canvas.getByTestId("GitSidebarChangeRow-0")).not.toHaveClass("swt:opacity-60");
    await expect(canvas.getByTestId("GitSidebarChangeRow-1")).not.toHaveClass("swt:opacity-60");
  },
};

export const AdvancedActions: Story = {
  args: {
    status: baseStatus,
    changedFiles: changedFiles.slice(),
    branchOptions: branchOptions.slice(),
    callbacks: buildCallbacks(),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
    canOpenRemoteRepository: true,
    onOpenRemoteRepository: noopOpenRemoteRepository,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByTestId("GitSidebarOpenRemoteRepositoryButton")).toBeEnabled();
    await expect(canvas.getByTestId("GitSidebarPullButton")).toBeEnabled();
    await expect(canvas.getByTestId("GitSidebarPushButton")).toBeEnabled();
    await userEvent.click(canvas.getByTestId("GitSidebarAdvancedActionsButton"));
    await expect(canvas.getByTestId("GitSidebarAdvancedActionsButton")).toHaveClass("swt:btn-primary");
    await expect(canvas.getByTestId("GitSidebarAdvancedActionsDivider")).toBeInTheDocument();
    await expect(canvas.queryByTestId("GitSidebarSyncButton")).toBeNull();
    await expect(canvas.getByTestId("GitSidebarFetchButton")).toBeInTheDocument();
    await expect(canvas.getByTestId("GitSidebarLfsPruneButton")).toBeVisible();
    await expect(canvas.getByTestId("GitSidebarLfsDedupButton")).toBeVisible();
    const downloadLargeFilesCheckbox = canvas.getByTestId("GitSidebarDownloadLargeFilesCheckbox");
    const lfsThresholdInput = canvas.getByTestId("GitSidebarLfsThresholdInput");

    await expect(downloadLargeFilesCheckbox).toBeChecked();
    await expect(lfsThresholdInput).toHaveValue(1);
    await expect(
      Boolean(
        downloadLargeFilesCheckbox.compareDocumentPosition(lfsThresholdInput) &
          Node.DOCUMENT_POSITION_FOLLOWING,
      ),
    ).toBe(true);
  },
};

export const ResponsiveActionLabels: Story = {
  args: {
    status: baseStatus,
    changedFiles: changedFiles.slice(),
    branchOptions: branchOptions.slice(),
    callbacks: buildCallbacks(),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await expect(canvas.getByTestId("GitSidebarPullButtonLabel")).toHaveClass("swt:truncate");
    await expect(canvas.getByTestId("GitSidebarPullButtonLabel")).toHaveClass(
      "swt:@max-3xs/gitSidebar:sr-only",
    );
  },
};

export const ConflictsPresent: Story = {
  args: {
    status: {
      ...baseStatus,
      IsMergeInProgress: true,
    },
    changedFiles: [
      conflictedFiles[3],
      conflictedFiles[0],
      conflictedFiles[1],
      conflictedFiles[2],
      {
        Path: "README.md",
        OriginalPath: undefined,
        IndexStatus: "M",
        WorkingTreeStatus: " ",
        IsConflicted: false,
      },
    ],
    branchOptions: branchOptions.slice(),
    callbacks: buildCallbacks(),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByTestId("GitSidebarMergeBanner")).toHaveTextContent(
      "Resolve all conflicted files before pushing.",
    );
    await expect(canvasElement.querySelectorAll("[data-testid^='GitSidebarChangeRow-']")).toHaveLength(5);
    await expect(canvas.getByTestId("GitSidebarChangeStatusIcon-0")).toBeInTheDocument();
    await expect(canvas.queryByTestId("GitSidebarDiscardChangeButton-0")).toBeNull();
    await expect(canvas.getByTestId("GitSidebarSaveAllButton")).toBeDisabled();
    await expect(canvas.getByTestId("GitSidebarUploadRightAwayCheckbox")).toBeDisabled();
    await userEvent.click(canvas.getByTestId("GitSidebarChangeRow-0"));
    fireEvent.click(canvas.getByTestId("GitSidebarChangeRow-4"), { ctrlKey: true });
    await expect(canvas.getByTestId("GitSidebarDiscardChangeButton-4")).toHaveAttribute(
      "aria-label",
      "Discard change",
    );
  },
};

export const LargeChangedSet: Story = {
  args: {
    status: baseStatus,
    changedFiles: largeChangedFiles,
    branchOptions: branchOptions.slice(),
    callbacks: buildCallbacks(),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByTestId("GitSidebar")).toHaveTextContent("400 files");
    await expect(
      canvas.getByTestId("GitSidebarChangedFilesVirtualContent"),
    ).toBeInTheDocument();
    await expect(canvas.getByTestId("GitSidebarChangeRow-0")).toBeInTheDocument();
    await expect(canvas.queryByTestId("GitSidebarChangeRow-399")).toBeNull();
    const scrollContainer = canvas.getByTestId(
      "GitSidebarChangedFilesScrollContainer",
    ) as HTMLElement;
    const maxScrollTop =
      scrollContainer.scrollHeight - scrollContainer.clientHeight;
    scrollContainer.scrollTop = maxScrollTop;
    await fireEvent.scroll(scrollContainer, {
      target: { scrollTop: maxScrollTop },
    });
    await expect(
      await canvas.findByTestId("GitSidebarChangeRow-399"),
    ).toBeInTheDocument();
  },
};

export const DeletedFile: Story = {
  args: {
    status: baseStatus,
    changedFiles: [
      {
        Path: "obsolete.md",
        OriginalPath: undefined,
        IndexStatus: "D",
        WorkingTreeStatus: " ",
        IsConflicted: false,
      },
    ],
    branchOptions: branchOptions.slice(),
    callbacks: buildCallbacks(),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByTestId("GitSidebar")).not.toHaveTextContent("git: D.");
    await expect(canvas.getByTestId("GitSidebar")).not.toHaveTextContent("Deleted");
    await expect(canvas.getByTestId("GitSidebarChangeStatusIcon-0")).toHaveClass("swt:text-error");
    await expect(canvas.getByTestId("GitSidebarChangeStatusIcon-0")).toHaveAttribute(
      "title",
      "Deleted:\n- git: D.",
    );
  },
};

export const ChangeStatusIconColors: Story = {
  args: {
    status: baseStatus,
    changedFiles: [
      {
        Path: "new-file.md",
        OriginalPath: undefined,
        IndexStatus: "A",
        WorkingTreeStatus: " ",
        IsConflicted: false,
      },
      {
        Path: "changed-file.md",
        OriginalPath: undefined,
        IndexStatus: "M",
        WorkingTreeStatus: " ",
        IsConflicted: false,
      },
      {
        Path: "deleted-file.md",
        OriginalPath: undefined,
        IndexStatus: "D",
        WorkingTreeStatus: " ",
        IsConflicted: false,
      },
    ],
    branchOptions: branchOptions.slice(),
    callbacks: buildCallbacks(),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await expect(canvas.getByTestId("GitSidebarChangeStatusIcon-0")).toHaveClass("swt:text-success");
    await expect(canvas.getByTestId("GitSidebarChangeStatusIcon-1")).toHaveClass("swt:text-warning");
    await expect(canvas.getByTestId("GitSidebarChangeStatusIcon-2")).toHaveClass("swt:text-error");
  },
};

export const LongWrappedFile: Story = {
  args: {
    status: baseStatus,
    changedFiles: [
      {
        Path: "src/very/long/path/that/wraps/in/the/sidebar/and/needs/a/fixed/status/icon.txt",
        OriginalPath: undefined,
        IndexStatus: "M",
        WorkingTreeStatus: " ",
        IsConflicted: false,
      },
    ],
    branchOptions: branchOptions.slice(),
    callbacks: buildCallbacks(),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByTestId("GitSidebarChangeRow-0")).toHaveClass("swt:items-center");
    await expect(canvas.getByTestId("GitSidebarChangeRow-0")).toHaveAttribute(
      "title",
      "src/very/long/path/that/wraps/in/the/sidebar/and/needs/a/fixed/status/icon.txt",
    );
    await expect(canvas.getByTestId("GitSidebarChangeName-0")).toHaveTextContent("icon.txt");
    await expect(canvas.getByTestId("GitSidebarChangeStatusSlot-0")).toHaveClass("swt:shrink-0");
    await expect(canvas.getByTestId("GitSidebarChangeStatusSlot-0")).toHaveClass("swt:self-center");
  },
};

const discardSelectionSpy = fn();

export const HoverDiscardChange: Story = {
  args: {
    status: baseStatus,
    changedFiles: changedFiles.slice(),
    branchOptions: branchOptions.slice(),
    callbacks: buildCallbacks({
      OnDiscardSelection: discardSelectionSpy,
    }),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
  },
  play: async ({ canvasElement }) => {
    discardSelectionSpy.mockClear();
    const canvas = within(canvasElement);

    const row = canvas.getByTestId("GitSidebarChangeRow-0");
    await expect(row).toHaveClass("swt:items-center");
    await expect(row).toHaveClass("swt:min-h-6");

    await userEvent.hover(row);
    await expect(canvas.getByTestId("GitSidebarDiscardChangeButton-0")).toBeInTheDocument();
    await expect(canvas.getByTestId("GitSidebarDiscardChangeButton-0")).toHaveClass("swt:opacity-0");
    await expect(canvas.getByTestId("GitSidebarDiscardChangeButton-0")).toHaveClass(
      "swt:group-hover:opacity-100",
    );

    await userEvent.click(canvas.getByTestId("GitSidebarDiscardChangeButton-0"));
    await expect(discardSelectionSpy).toHaveBeenCalledWith(["README.md"]);
  },
};

export const BusyProgressState: Story = {
  args: {
    status: baseStatus,
    changedFiles: changedFiles.slice(),
    branchOptions: branchOptions.slice(),
    runStatus: buildProgressRunStatus({
      Method: "pull",
      Stage: "Receiving objects",
      ProgressPercent: 54,
      Output: "remote: Enumerating objects: 18, done.\nremote: Counting objects: 100% (18/18), done.\n",
    }),
    callbacks: buildCallbacks(),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByTestId("GitSidebarProgressNotice")).toHaveTextContent(
      "pull | Receiving objects | 54%",
    );
    await expect(canvas.getByTestId("GitSidebarProgressBar")).toBeInTheDocument();
    const fillBar = canvas.getByTestId("GitSidebarProgressBar").firstElementChild;
    await expect(fillBar).toHaveAttribute("style", expect.stringContaining("width: 54%"));
    const outputDetails = canvas.getByTestId("GitSidebarProgressOutput");
    await expect(outputDetails).not.toHaveAttribute("open");
    await expect(outputDetails).toHaveTextContent(
      "remote: Enumerating objects: 18, done.",
    );
    const outputConsole = within(outputDetails).getByText(/remote: Enumerating objects/);
    await expect(outputConsole).toHaveClass("swt:bg-base-content");
    await expect(outputConsole).toHaveClass("swt:text-base-100");

    const sourceControlTitle = canvas.getByTestId("GitSidebarTitle");
    const trackingInfo = canvas.getByText("Tracking origin/feature/git-sidebar");
    expect(
      sourceControlTitle.compareDocumentPosition(canvas.getByTestId("GitSidebarProgressNotice")) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
    expect(
      canvas.getByTestId("GitSidebarProgressNotice").compareDocumentPosition(trackingInfo) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  },
};

export const CancelableUploadProgressState: Story = {
  args: {
    status: baseStatus,
    changedFiles: changedFiles.slice(),
    branchOptions: branchOptions.slice(),
    runStatus: buildProgressRunStatus({
      Method: "lfs",
      Stage: "Uploading Git LFS objects",
      Output: "Uploading LFS objects: 0% (0/6), 524 KB | 121 KB/s\n",
    }),
    callbacks: buildCallbacks({
      OnCancelOperation: cancelOperationSpy,
    }),
    canCancelOperation: true,
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
  },
  play: async ({ canvasElement }) => {
    cancelOperationSpy.mockClear();
    const canvas = within(canvasElement);

    const cancelButton = canvas.getByTestId("GitSidebarCancelOperationButton");

    await userEvent.click(cancelButton);
    await expect(cancelOperationSpy).toHaveBeenCalledTimes(1);
  },
};

export const ClonePhaseProgressState: Story = {
  args: {
    status: baseStatus,
    changedFiles: changedFiles.slice(),
    branchOptions: branchOptions.slice(),
    runStatus: buildProgressRunStatus({
      Method: "clone",
      Stage: "Preparing clone",
    }),
    callbacks: buildCallbacks(),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByTestId("GitSidebarProgressNotice")).toHaveTextContent(
      "clone | Preparing clone",
    );
    await expect(canvas.queryByTestId("GitSidebarProgressBar")).not.toBeInTheDocument();
  },
};

export const BusyNoticeState: Story = {
  args: {
    status: baseStatus,
    changedFiles: changedFiles.slice(),
    branchOptions: branchOptions.slice(),
    runStatus: buildBusyRunStatus("Pulling from origin/main"),
    callbacks: buildCallbacks(),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByTestId("GitSidebarProgressNotice")).toHaveTextContent(
      "Pulling from origin/main",
    );
  },
};

export const CreateBranchModal: Story = {
  args: {
    status: baseStatus,
    changedFiles: changedFiles.slice(),
    branchOptions: branchOptions.slice(),
    callbacks: buildCallbacks(),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const modal = within(document.body);
    await userEvent.click(canvas.getByTestId("GitSidebarAdvancedActionsButton"));
    await userEvent.click(canvas.getByTestId("GitSidebarCreateBranchButton"));
    const branchNameInput = await modal.findByTestId(
      "GitSidebarBranchNameInput",
    );
    await userEvent.type(
      branchNameInput,
      "feature/new-branch",
    );
    await expect(modal.getByTestId("GitSidebarStartPointSelect")).toHaveTextContent(
      "origin/main",
    );
  },
};

export const SwitchBranchModal: Story = {
  args: {
    status: baseStatus,
    changedFiles: changedFiles.slice(),
    branchOptions: branchOptions.slice(),
    callbacks: buildCallbacks(),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const modal = within(document.body);
    await userEvent.click(canvas.getByTestId("GitSidebarAdvancedActionsButton"));
    await userEvent.click(canvas.getByTestId("GitSidebarSwitchBranchButton"));
    await expect(modal.getByTestId("GitSidebarSwitchBranchSelect")).toHaveTextContent(
      "main",
    );
    await expect(modal.getByTestId("GitSidebarSwitchBranchSelect")).toHaveTextContent(
      "origin/main",
    );
    await userEvent.click(modal.getByTestId("GitSidebarSwitchBranchSubmit"));
  },
};

const submitPublishRenameSpy = fn();

const publishOwners = [
  { NamespaceId: undefined, Label: "carol (personal)", DisabledReason: undefined },
  { NamespaceId: 11, Label: "nfdi4plants/team-a", DisabledReason: undefined },
  {
    NamespaceId: 12,
    Label: "nfdi4plants/archive",
    DisabledReason: "You may not create projects in this group.",
  },
];

export const PublishOwnerPrompt: Story = {
  args: {
    status: baseStatus,
    changedFiles: [],
    branchOptions: branchOptions.slice(),
    callbacks: buildCallbacks(),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
    hasRemote: false,
    publishRenamePrompt: {
      CurrentName: "My ARC",
      Message: "Choose where to create the DataHUB repository.",
      Owners: publishOwners,
      SelectedNamespaceId: undefined,
    },
    onSubmitPublishRename: submitPublishRenameSpy,
  },
  play: async () => {
    submitPublishRenameSpy.mockClear();
    const modal = within(document.body);

    await expect(await modal.findByTestId("GitSidebarPublishOwner-12")).toBeDisabled();
    await expect(modal.getByTestId("GitSidebarPublishOwnerReason-12")).toBeInTheDocument();
    await expect(modal.getByTestId("GitSidebarPublishOwner-12")).toHaveAccessibleDescription(
      "You may not create projects in this group.",
    );
    await expect(modal.getByTestId("GitSidebarPublishOwner-11")).toBeEnabled();
    await expect(modal.queryByTestId("GitSidebarPublishOwnerReason-11")).not.toBeInTheDocument();

    await expect(modal.getByTestId("GitSidebarPublishOwner-personal")).toBeChecked();
    await userEvent.click(modal.getByTestId("GitSidebarPublishRenameSubmit"));
    await waitFor(() => expect(submitPublishRenameSpy).toHaveBeenCalledTimes(1));
    await expect(submitPublishRenameSpy.mock.calls[0][0].Name).toBe("My ARC");
    await expect(submitPublishRenameSpy.mock.calls[0][0].NamespaceId).toBeUndefined();

    await userEvent.click(modal.getByTestId("GitSidebarPublishOwner-11"));
    await expect(modal.getByTestId("GitSidebarPublishOwner-11")).toBeChecked();
    await userEvent.click(modal.getByTestId("GitSidebarPublishRenameSubmit"));
    await waitFor(() => expect(submitPublishRenameSpy).toHaveBeenCalledTimes(2));
    await expect(submitPublishRenameSpy.mock.calls[1][0].NamespaceId).toBe(11);
  },
};

export const PublishOwnerPromptWithDisabledPersonalOwner: Story = {
  args: {
    ...PublishOwnerPrompt.args,
    publishRenamePrompt: {
      CurrentName: "My ARC",
      Message: "Choose where to create the DataHUB repository.",
      Owners: [
        {
          NamespaceId: undefined,
          Label: "carol (personal)",
          DisabledReason: "DataHUB refused a new repository here.",
        },
        ...publishOwners.slice(1),
      ],
      SelectedNamespaceId: undefined,
    },
  },
  play: async () => {
    submitPublishRenameSpy.mockClear();
    const modal = within(document.body);

    await expect(await modal.findByTestId("GitSidebarPublishOwner-personal")).toBeDisabled();
    await expect(modal.getByTestId("GitSidebarPublishOwner-11")).toBeChecked();
    await userEvent.click(modal.getByTestId("GitSidebarPublishRenameSubmit"));
    await waitFor(() => expect(submitPublishRenameSpy).toHaveBeenCalledTimes(1));
    await expect(submitPublishRenameSpy.mock.calls[0][0].NamespaceId).toBe(11);
  },
};

export const CommitComposer: Story = {
  args: {
    status: baseStatus,
    changedFiles: changedFiles.slice(),
    branchOptions: branchOptions.slice(),
    callbacks: buildCallbacks(),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const legacyCard = canvas.getByTestId("GitSidebarCommitSection").firstElementChild as HTMLElement;
    await expect(legacyCard).not.toHaveClass("swt:rounded-box");
    await expect(legacyCard).not.toHaveClass("swt:border");
    await expect(canvas.queryByTestId("GitSidebarCommitSelectionButton")).toBeNull();
    await expect(canvas.queryByTestId("GitSidebarCommitSelectionCheckbox-README.md")).toBeNull();

    // Nothing marked: one button fills the row.
    await expect(canvas.queryByTestId("GitSidebarSaveSelectedButton")).toBeNull();
    const uploadCheckbox = canvas.getByTestId("GitSidebarUploadRightAwayCheckbox");
    await expect(uploadCheckbox).toBeChecked();
    const loneBounds = canvas.getByTestId("GitSidebarSaveAllButton").getBoundingClientRect();
    const uploadTop = uploadCheckbox.getBoundingClientRect().top;
    const sectionBounds = canvas.getByTestId("GitSidebarCommitSection").getBoundingClientRect();
    expect(loneBounds.width).toBeGreaterThan(sectionBounds.width / 2);

    await expect(canvas.getByTestId("GitSidebarSaveAllButton")).not.toHaveClass("swt:btn-outline");

    // Marked: two equal buttons share one row and nothing below them moves.
    // Save All stays filled on the left, Save Selected is the outlined one on the right.
    await userEvent.click(canvas.getByTestId("GitSidebarChangeRow-0"));
    const saveSelected = await canvas.findByTestId("GitSidebarSaveSelectedButton");
    const selectedBounds = saveSelected.getBoundingClientRect();
    const allBounds = canvas.getByTestId("GitSidebarSaveAllButton").getBoundingClientRect();
    expect(selectedBounds.top).toBe(allBounds.top);
    expect(selectedBounds.height).toBe(loneBounds.height);
    expect(allBounds.height).toBe(loneBounds.height);
    expect(allBounds.top).toBe(loneBounds.top);
    expect(allBounds.left).toBe(loneBounds.left);
    expect(allBounds.right).toBeLessThanOrEqual(selectedBounds.left);
    expect(Math.abs(selectedBounds.width - allBounds.width)).toBeLessThan(1);
    expect(uploadCheckbox.getBoundingClientRect().top).toBe(uploadTop);
    await expect(canvas.getByTestId("GitSidebarSaveAllButton")).not.toHaveClass("swt:btn-outline");
    await expect(saveSelected).toHaveClass("swt:btn-outline");

    // Ctrl+click deselects the row and the single button returns.
    fireEvent.mouseDown(canvas.getByTestId("GitSidebarChangeRow-0"), { ctrlKey: true });
    fireEvent.click(canvas.getByTestId("GitSidebarChangeRow-0"), { ctrlKey: true });
    await waitFor(() => expect(canvas.queryByTestId("GitSidebarSaveSelectedButton")).toBeNull());
    expect(canvas.getByTestId("GitSidebarSaveAllButton").getBoundingClientRect().width).toBe(loneBounds.width);
  },
};

export const RemoteActionsDisabled: Story = {
  args: {
    status: baseStatus,
    changedFiles: changedFiles.slice(),
    branchOptions: branchOptions.slice(),
    callbacks: buildCallbacks(),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
    remoteActionsEnabled: false,
    remoteActionsWarning:
      "Sign in to a DataHub account to use fetch, pull, push, or update.",
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByTestId("GitSidebarOpenRemoteRepositoryButton")).toBeDisabled();
    await expect(canvas.getByTestId("GitSidebarPullButton")).toBeDisabled();
    await expect(canvas.getByTestId("GitSidebarPushButton")).toBeDisabled();
    await expect(
      canvas.getByTestId("GitSidebarRemoteAuthWarning"),
    ).toHaveTextContent(
      "Sign in to a DataHub account to use fetch, pull, push, or update.",
    );
  },
};

const openRemoteRepositorySpy = fn();

export const OpenRemoteRepositoryButton: Story = {
  args: {
    status: baseStatus,
    changedFiles: changedFiles.slice(),
    branchOptions: branchOptions.slice(),
    callbacks: buildCallbacks(),
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
    canOpenRemoteRepository: true,
    onOpenRemoteRepository: openRemoteRepositorySpy,
  },
  play: async ({ canvasElement }) => {
    openRemoteRepositorySpy.mockClear();
    const canvas = within(canvasElement);

    await expect(canvas.getByTestId("GitSidebarOpenRemoteRepositoryButton")).toBeEnabled();
    await userEvent.click(canvas.getByTestId("GitSidebarOpenRemoteRepositoryButton"));
    await expect(openRemoteRepositorySpy).toHaveBeenCalledTimes(1);
  },
};

export const GlobalErrorState: Story = {
  args: {
    status: baseStatus,
    changedFiles: changedFiles.slice(),
    branchOptions: branchOptions.slice(),
    callbacks: buildCallbacks(),
    errorNotice: "Fetch failed because the remote rejected the request.",
    downloadLargeFiles: true,
    lfsAutoTrackThresholdMb: 1,
    diffIndexingLimitMb: 1024,
  },
};

const saveDiffIndexingLimitSpy = fn();

const diffSettingsArgs = () => ({
  status: baseStatus,
  changedFiles: changedFiles.slice(),
  branchOptions: branchOptions.slice(),
  callbacks: buildCallbacks({
    OnSaveDiffIndexingLimit: saveDiffIndexingLimitSpy,
  }),
  downloadLargeFiles: true,
  lfsAutoTrackThresholdMb: 1,
  diffIndexingLimitMb: 1024,
});

const setNumberInput = async (input: HTMLElement, value: string) => {
  await userEvent.clear(input);
  await userEvent.type(input, value);
};

export const SavesADiffIndexingLimitInTheMemoryRange: Story = {
  args: diffSettingsArgs(),
  play: async ({ canvasElement }) => {
    saveDiffIndexingLimitSpy.mockClear();
    const canvas = within(canvasElement);

    await userEvent.click(canvas.getByTestId("GitSidebarAdvancedActionsButton"));
    const input = canvas.getByTestId("GitSidebarDiffIndexingLimitInput");

    await setNumberInput(input, "0");
    await userEvent.click(canvas.getByTestId("GitSidebarDiffIndexingLimitSaveButton"));
    await expect(canvas.getByTestId("GitSidebarErrorNotice")).toBeVisible();
    await expect(saveDiffIndexingLimitSpy).not.toHaveBeenCalled();

    await setNumberInput(input, "8");
    await userEvent.click(canvas.getByTestId("GitSidebarDiffIndexingLimitSaveButton"));
    await expect(saveDiffIndexingLimitSpy).toHaveBeenCalledTimes(1);
    await expect(saveDiffIndexingLimitSpy).toHaveBeenCalledWith(8);
  },
};
