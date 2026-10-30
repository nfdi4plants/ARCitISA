import React from "react";
import type { Meta, StoryObj } from "@storybook/react-vite";
import { screen, within, expect, userEvent, waitFor, fireEvent, fn } from "storybook/test";
import { FileExplorer, FileExplorerExample_Example as FileExplorerExample } from "./FileExplorer.fs.js";
import {
  contextMenuItems as fileExplorerGitLfsContextMenuItems,
  lfsPillAction as fileExplorerGitLfsPillAction,
} from "./FileExplorerGitLfsHelper.fs.js";
import {
  ContextMenuItem,
  type FileItem,
  FileItemIcon_Document$,
  FileItemIcon_Folder,
  FileTree_createFile,
  FileTree_createFolder,
} from "./Types.fs.js";
import { ofArray } from "../../fable_modules/fable-library-ts.5.0.0-alpha.21/List.ts";

const arcCreateItems = [
  { label: "Add Study", path: "studies/NewStudy/isa.study.xlsx" },
  { label: "Add Assay", path: "assays/NewAssay/isa.assay.xlsx" },
  { label: "Add Workflow", path: "workflows/NewWorkflow/isa.workflow.xlsx" },
  { label: "Add Run", path: "runs/NewRun/isa.run.xlsx" },
];

const createStableFile = (name: string, path: string, id: string): FileItem =>
  Object.assign(FileTree_createFile(name, path, FileItemIcon_Document$()), { Id: id });

const createStableFolder = (name: string, path: string, id: string, children?: FileItem[]): FileItem =>
  Object.assign(FileTree_createFolder(name, path, FileItemIcon_Folder()), {
    Id: id,
    Children: children === undefined ? undefined : ofArray(children),
  });

const InMemoryCreateFileExplorer = () => {
  const [pendingPath, setPendingPath] = React.useState<string | null>(null);
  const [savedPaths, setSavedPaths] = React.useState<string[]>([]);
  const rootFolder = React.useMemo(() => FileTree_createFolder("ARC Root", "arc", FileItemIcon_Folder()), []);

  return (
    <div className="swt:p-4 swt:space-y-4">
      <FileExplorer
        initialItems={ofArray([rootFolder])}
        onContextMenu={() =>
          ofArray(
            arcCreateItems.map(
              (item) =>
                new ContextMenuItem(
                  item.label,
                  "swt:fluent--document-add-24-regular",
                  () => setPendingPath(item.path),
                  undefined,
                ),
            ),
          )
        }
      />
      <div data-testid="pending-draft">Pending draft: {pendingPath ?? "none"}</div>
      <div data-testid="saved-count">Saved count: {savedPaths.length}</div>
      <div data-testid="saved-paths">Saved paths: {savedPaths.join(", ") || "none"}</div>
      <button
        type="button"
        disabled={!pendingPath}
        onClick={() => {
          if (pendingPath) {
            setSavedPaths((paths) => [...paths, pendingPath]);
            setPendingPath(null);
          }
        }}
      >
        Save
      </button>
    </div>
  );
};

const DATA_MAP_PATH = "assays/DataMapAssay/isa.datamap.xlsx";

const DataMapLifecycleFileExplorer = () => {
  const [hasDataMap, setHasDataMap] = React.useState(false);
  const [selectedPath, setSelectedPath] = React.useState("none");

  const items = React.useMemo(() => {
    const children = hasDataMap
      ? [createStableFile("isa.datamap.xlsx", DATA_MAP_PATH, "datamap-file")]
      : [];
    const assayFolder = Object.assign(
      createStableFolder("DataMapAssay", "assays/DataMapAssay", "datamap-assay", children),
      { IsExpanded: true },
    );

    return ofArray([assayFolder]);
  }, [hasDataMap]);

  return (
    <div className="swt:p-4 swt:space-y-4">
      <FileExplorer
        initialItems={items}
        onItemClick={(item) => setSelectedPath(item.Path ?? "none")}
        onContextMenu={(item) => {
          if (item.Id === "datamap-assay" && !hasDataMap) {
            return ofArray([
              new ContextMenuItem(
                "Add DataMap",
                "swt:fluent--database-arrow-up-20-regular",
                () => setHasDataMap(true),
                undefined,
              ),
            ]);
          }

          if (item.Id === "datamap-file") {
            return ofArray([
              new ContextMenuItem(
                "Delete DataMap",
                "swt:fluent--delete-24-regular",
                () => {
                  setHasDataMap(false);
                  setSelectedPath("none");
                },
                undefined,
              ),
            ]);
          }

          return ofArray([]);
        }}
      />
      <div data-testid="datamap-selected-path">Selected path: {selectedPath}</div>
    </div>
  );
};

const LazyLoadDirectoryFileExplorer = () => {
  const [lazyFolderLoaded, setLazyFolderLoaded] = React.useState(false);

  const items = React.useMemo(() => {
    const emptyFolder = createStableFolder("Empty Folder", "arc/empty-folder", "empty-folder", []);
    const lazyChild = createStableFile("Lazy Child.txt", "arc/lazy-folder/Lazy Child.txt", "lazy-child");
    const lazyFolder = createStableFolder(
      "Lazy Folder",
      "arc/lazy-folder",
      "lazy-folder",
      lazyFolderLoaded ? [lazyChild] : undefined,
    );

    return ofArray([emptyFolder, lazyFolder]);
  }, [lazyFolderLoaded]);

  return (
    <div className="swt:p-4">
      <FileExplorer
        initialItems={items}
        onDirectoryExpansionChange={(item, willExpand) => {
          if (item.Id === "lazy-folder" && willExpand) {
            setLazyFolderLoaded(true);
          }
        }}
      />
    </div>
  );
};

const SelectedPathFileExplorer = () => {
  const items = React.useMemo(() => {
    const selectedFile = createStableFile("selected-report.txt", "arc/selected-parent/selected-report.txt", "selected-file");
    const parentFolder = createStableFolder("Selected Parent", "arc/selected-parent", "selected-parent", [selectedFile]);

    return ofArray([parentFolder]);
  }, []);

  return (
    <div className="swt:p-4">
      <FileExplorer initialItems={items} selectedItemId="selected-file" />
    </div>
  );
};

const ControlledSelectionFileExplorer = () => {
  const items = React.useMemo(
    () =>
      ofArray([
        createStableFile("first.txt", "arc/first.txt", "first-file"),
        createStableFile("second.txt", "arc/second.txt", "second-file"),
        createStableFile("locked.txt", "arc/locked.txt", "locked-file"),
        createStableFolder("Entity Folder", "arc/entity", "entity-folder", [
          createStableFile("entity-child.txt", "arc/entity/entity-child.txt", "entity-child"),
        ]),
        createStableFolder("studies", "studies", "studies", [
          createStableFolder("selected-study", "studies/selected-study", "selected-study", [
            createStableFile("isa.study.xlsx", "studies/selected-study/isa.study.xlsx", "isa-study"),
            createStableFolder("resources", "studies/selected-study/resources", "resources", [
              createStableFolder("raw-data", "studies/selected-study/resources/raw-data", "raw-data", [
                createStableFolder("batch-01", "studies/selected-study/resources/raw-data/batch-01", "batch-01", [
                  createStableFolder(
                    "replicate-01",
                    "studies/selected-study/resources/raw-data/batch-01/replicate-01",
                    "replicate-01",
                    [
                      createStableFile(
                        "selected-report.txt",
                        "studies/selected-study/resources/raw-data/batch-01/replicate-01/selected-report.txt",
                        "selected-report",
                      ),
                      createStableFile(
                        "sibling-report.txt",
                        "studies/selected-study/resources/raw-data/batch-01/replicate-01/sibling-report.txt",
                        "sibling-report",
                      ),
                    ],
                  ),
                ]),
              ]),
            ]),
          ]),
          createStableFolder("neighbor-study", "studies/neighbor-study", "neighbor-study", [
            createStableFile("neighbor-isa.study.xlsx", "studies/neighbor-study/isa.study.xlsx", "neighbor-isa-study"),
          ]),
        ]),
        createStableFolder("assays", "assays", "assays", [
          createStableFolder("selected-assay", "assays/selected-assay", "selected-assay", [
            createStableFile("isa.assay.xlsx", "assays/selected-assay/isa.assay.xlsx", "isa-assay"),
          ]),
        ]),
        createStableFile("README.md", "README.md", "readme"),
      ]),
    [],
  );

  const [selectedId, setSelectedId] = React.useState("first-file");

  return (
    <div className="swt:h-[34rem] swt:p-4">
      <FileExplorer
        initialItems={items}
        selectedItemId={selectedId}
        onItemClick={(item) => {
          // The parent owns the selection and ignores clicks on the locked file.
          if (item.Id !== "locked-file") {
            setSelectedId(item.Id);
          }
        }}
      />
      <output data-testid="controlled-selected-id">{selectedId}</output>
    </div>
  );
};

const InitiallyExpandedFileExplorer = () => {
  const items = React.useMemo(() => {
    const child = createStableFile("Initially Visible.txt", "arc/initially-expanded/Initially Visible.txt", "initial-child");
    const folder = Object.assign(
      createStableFolder("Initially Expanded", "arc/initially-expanded", "initial-folder", [child]),
      { IsExpanded: true },
    );

    return ofArray([folder]);
  }, []);

  return (
    <div className="swt:p-4">
      <FileExplorer initialItems={items} />
    </div>
  );
};

const ExpansionRefreshFileExplorer = () => {
  const [lazyFolderLoaded, setLazyFolderLoaded] = React.useState(false);

  const items = React.useMemo(() => {
    const selectedFile = createStableFile("selected-report.txt", "arc/selected-parent/selected-report.txt", "selected-file");
    const selectedParent = createStableFolder("Selected Parent", "arc/selected-parent", "selected-parent", [selectedFile]);
    const lazyChild = createStableFile("Lazy Child.txt", "arc/lazy-folder/Lazy Child.txt", "lazy-child");
    const lazyFolder = createStableFolder(
      "Lazy Folder",
      "arc/lazy-folder",
      "lazy-folder",
      lazyFolderLoaded ? [lazyChild] : undefined,
    );

    return ofArray([selectedParent, lazyFolder]);
  }, [lazyFolderLoaded]);

  return (
    <div className="swt:p-4">
      <FileExplorer
        initialItems={items}
        selectedItemId="selected-file"
        onDirectoryExpansionChange={(item, willExpand) => {
          if (item.Id === "lazy-folder" && willExpand) {
            setLazyFolderLoaded(true);
          }
        }}
      />
    </div>
  );
};

const DeleteActionFileExplorer = () => {
  const [lastDeleted, setLastDeleted] = React.useState<string>("none");
  const [lastAction, setLastAction] = React.useState<string>("none");

  const items = React.useMemo(
    () =>
      ofArray([
        FileTree_createFolder("Deletable Folder", "studies/FolderA", FileItemIcon_Folder()),
        FileTree_createFile("Deletable File", "studies/FolderA/isa.study.xlsx", FileItemIcon_Document$()),
      ]),
    [],
  );

  return (
    <div className="swt:p-4 swt:space-y-4">
      <FileExplorer
        initialItems={items}
        canDeleteItem={() => true}
        onDeleteItem={(item) => setLastDeleted(item.Name)}
        getItemActions={(item) =>
          ofArray([new ContextMenuItem("Mark", "swt:fluent--star-24-regular", () => setLastAction(item.Name), undefined)])
        }
      />
      <div data-testid="last-deleted">Last deleted: {lastDeleted}</div>
      <div data-testid="last-row-action">Last action: {lastAction}</div>
    </div>
  );
};

const DestructiveContextMenuFileExplorer = () => {
  const [clickCount, setClickCount] = React.useState(0);
  const items = React.useMemo(() => ofArray([FileTree_createFile("Context File", "studies/A/file.txt", FileItemIcon_Document$())]), []);

  return (
    <div className="swt:p-4">
      <FileExplorer
        initialItems={items}
        onContextMenu={() =>
          ofArray([
            new ContextMenuItem(
              "Delete Item",
              "swt:fluent--delete-24-regular",
              () => setClickCount((count) => count + 1),
              undefined,
            ),
          ])
        }
      />
      <div data-testid="context-click-count">Context clicks: {clickCount}</div>
    </div>
  );
};

const LfsContextMenuFileExplorer = () => {
  const [downloadCount, setDownloadCount] = React.useState(0);
  const [freeCount, setFreeCount] = React.useState(0);

  const items = React.useMemo(
    () =>
      ofArray([
        Object.assign(
          FileTree_createFile("Downloaded LFS", "data/downloaded.bin", FileItemIcon_Document$()),
          { IsLFS: true, Downloaded: true, IsLFSPointer: false, SizeFormatted: "14 MB" },
        ),
        Object.assign(
          FileTree_createFile("Pointer LFS", "data/pointer.bin", FileItemIcon_Document$()),
          { IsLFS: true, Downloaded: false, IsLFSPointer: true, SizeFormatted: "42 MB" },
        ),
        Object.assign(
          FileTree_createFile("Plain File", "data/plain.txt", FileItemIcon_Document$()),
          { IsLFS: false, Downloaded: false, IsLFSPointer: false },
        ),
      ]),
    [],
  );

  const downloadLfsFile = React.useCallback(() => setDownloadCount((count) => count + 1), []);
  const freeLocalLfsCopy = React.useCallback(() => setFreeCount((count) => count + 1), []);

  return (
    <div className="swt:p-4">
      <FileExplorer
        initialItems={items}
        onContextMenu={(item) =>
          fileExplorerGitLfsContextMenuItems(
            item,
            () => {},
            downloadLfsFile,
            freeLocalLfsCopy,
          )
        }
        getItemStatusAction={(item) => fileExplorerGitLfsPillAction(item, downloadLfsFile)}
      />
      <div data-testid="lfs-download-count">Downloads: {downloadCount}</div>
      <div data-testid="lfs-free-count">Freed: {freeCount}</div>
    </div>
  );
};

const CopyPathDefaultFileExplorer = () => {
  const items = React.useMemo(
    () => ofArray([FileTree_createFile("Relative File", "studies/A/file.txt", FileItemIcon_Document$())]),
    [],
  );

  return (
    <div className="swt:p-4">
      <FileExplorer initialItems={items} />
    </div>
  );
};

const CopyPathResolverFileExplorer = () => {
  const items = React.useMemo(
    () => ofArray([FileTree_createFile("Absolute File", "studies/A/file.txt", FileItemIcon_Document$())]),
    [],
  );

  return (
    <div className="swt:p-4">
      <FileExplorer
        initialItems={items}
        getCopyPath={(item) => (item.Path ? `C:/arc/${item.Path}` : undefined)}
        getCopyRelativePath={(item) => item.Path ?? undefined}
      />
    </div>
  );
};

const veryLongFolderName =
  "Assay folder with an exceptionally long generated identifier that should be clipped before row controls";

const veryLongFileName =
  "measurement-export-with-an-exceptionally-long-generated-name-that-should-not-hide-controls.tsv";

const TruncatedOverflowFileExplorer = () => {
  const items = React.useMemo(() => {
    const childFile = createStableFile("child.txt", "assays/long-folder/child.txt", "truncate-child");
    const longFolder = createStableFolder(
      veryLongFolderName,
      "assays/long-folder",
      "truncate-folder",
      [childFile],
    );
    const longFile = createStableFile(veryLongFileName, "assays/long-file.tsv", "truncate-file");

    return ofArray([longFolder, longFile]);
  }, []);

  const rowAction = React.useCallback(
    (item: FileItem) =>
      ofArray([
        new ContextMenuItem(
          "Mark",
          "swt:fluent--star-24-regular",
          () => {
            void item;
          },
          undefined,
        ),
      ]),
    [],
  );

  const statusAction = React.useCallback(
    (item: FileItem) =>
      new ContextMenuItem(
        "Status",
        "swt:fluent--info-24-regular",
        () => {
          void item;
        },
        undefined,
      ),
    [],
  );

  return (
    <div
      data-testid="truncated-file-explorer-viewport"
      className="swt:p-2"
      style={{ width: 280, overflow: "hidden" }}
    >
      <FileExplorer
        initialItems={items}
        getItemActions={rowAction}
        getItemStatusAction={statusAction}
        truncateOverflowingItemNames={true}
      />
    </div>
  );
};

const lfsRowName = "normal.bin";

const createLfsRowItem = (id: string, lfsActivity?: string): FileItem =>
  Object.assign(createStableFile(lfsRowName, `runs/${id}.bin`, id), {
    IsLFS: true,
    Downloaded: true,
    IsLFSPointer: false,
    SizeFormatted: "30 MB",
    LfsActivity: lfsActivity,
  });

const BusyLfsRowFileExplorer = () => {
  const items = React.useMemo(
    () => ofArray([createLfsRowItem("lfs-idle"), createLfsRowItem("lfs-busy", "Freeing")]),
    [],
  );

  return (
    <div data-testid="busy-lfs-viewport" className="swt:p-2" style={{ width: 320, overflow: "hidden" }}>
      <FileExplorer
        initialItems={items}
        getItemActions={() =>
          ofArray([new ContextMenuItem("Rename", "swt:fluent--edit-24-regular", () => {}, undefined)])
        }
        canDeleteItem={() => true}
        onDeleteItem={() => {}}
        getItemStatusAction={(item) => fileExplorerGitLfsPillAction(item, () => {})}
        truncateOverflowingItemNames={true}
      />
    </div>
  );
};

const lfsRowLabel = (canvasElement: HTMLElement, id: string) => {
  const row = canvasElement.querySelector(`[role="treeitem"][data-file-item-id="${id}"]`);

  if (!(row instanceof HTMLElement)) {
    throw new Error(`Expected file row ${id}.`);
  }

  return within(row).getByText(lfsRowName);
};

const installClipboardMock = () => {
  const writeText = fn(async () => undefined);
  Object.defineProperty(navigator, "clipboard", {
    value: { writeText },
    configurable: true,
    writable: true,
  });

  return writeText;
};

const expectContextMenuCopy = async (
  canvasElement: HTMLElement,
  itemLabel: string,
  menuLabel: string,
  expectedText: string,
) => {
  const writeText = installClipboardMock();
  const canvas = within(canvasElement);
  const targetFile = await canvas.findByText(itemLabel);
  const fileItem = targetFile.closest("[data-file-item-id]");

  await waitFor(() => expect(fileItem).toBeTruthy());

  if (!fileItem) {
    throw new Error(`Expected file item element for ${itemLabel}.`);
  }

  fireEvent.contextMenu(fileItem, { clientX: 30, clientY: 30, bubbles: true });
  await userEvent.click(await screen.findByText(menuLabel));

  await waitFor(() => expect(writeText).toHaveBeenCalledWith(expectedText));
};

const expectLongNameControlsToStayVisible = async (canvasElement: HTMLElement, itemName: string) => {
  const canvas = within(canvasElement);
  const viewport = await canvas.findByTestId("truncated-file-explorer-viewport");
  const label = await canvas.findByText(itemName);
  const markButton = await canvas.findByRole("button", { name: `Mark ${itemName}` });
  const statusButton = await canvas.findByRole("button", { name: `Status ${itemName}` });

  await waitFor(() => {
    const viewportRect = viewport.getBoundingClientRect();
    const labelRect = label.getBoundingClientRect();
    const markRect = markButton.getBoundingClientRect();
    const statusRect = statusButton.getBoundingClientRect();

    expect(markRect.right).toBeLessThanOrEqual(viewportRect.right + 1);
    expect(statusRect.right).toBeLessThanOrEqual(viewportRect.right + 1);
    expect(labelRect.right).toBeLessThanOrEqual(markRect.left + 1);
    expect(label.clientWidth).toBeLessThan(label.scrollWidth);
  });
};

const narrowLfsPointerName = "a-very-long-pointer-file-name-for-the-sequencing-run.fastq.gz";
const narrowLfsDownloadedName = "a-very-long-downloaded-file-name-for-the-alignment.bam";

const createNarrowLfsItems = () =>
  ofArray([
    Object.assign(
      createStableFolder("data", "data", "narrow-data", [
        Object.assign(createStableFolder("raw", "data/raw", "narrow-raw", [
          Object.assign(createStableFile(narrowLfsPointerName, "data/raw/pointer.fastq.gz", "narrow-pointer"), {
            IsLFS: true,
            Downloaded: false,
            IsLFSPointer: true,
            SizeFormatted: "42 MB",
          }),
          Object.assign(createStableFile(narrowLfsDownloadedName, "data/raw/downloaded.bam", "narrow-downloaded"), {
            IsLFS: true,
            Downloaded: true,
            IsLFSPointer: false,
            SizeFormatted: "14 MB",
          }),
        ]), { IsExpanded: true }),
      ]),
      { IsExpanded: true },
    ),
  ]);

const NarrowLfsFileExplorer = () => {
  const [selectedPath, setSelectedPath] = React.useState("none");
  const items = React.useMemo(createNarrowLfsItems, []);

  return (
    <div>
      <div data-testid="narrow-lfs-viewport" style={{ width: 240, overflow: "hidden" }}>
        <FileExplorer
          initialItems={items}
          onItemClick={(item) => setSelectedPath(item.Path ?? "none")}
          getItemActions={() =>
            ofArray([new ContextMenuItem("Rename", "swt:fluent--edit-24-regular", () => {}, undefined)])
          }
          canDeleteItem={() => true}
          onDeleteItem={() => {}}
          getItemStatusAction={(item) => fileExplorerGitLfsPillAction(item, () => {})}
          truncateOverflowingItemNames={true}
        />
      </div>
      <div data-testid="narrow-lfs-selected">Selected: {selectedPath}</div>
    </div>
  );
};

export const LfsRowsKeepTheirNameAtNarrowWidth: StoryObj<typeof NarrowLfsFileExplorer> = {
  render: () => <NarrowLfsFileExplorer />,

  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const viewport = await canvas.findByTestId("narrow-lfs-viewport");

    for (const name of [narrowLfsPointerName, narrowLfsDownloadedName]) {
      const label = await canvas.findByText(name);
      const row = label.closest("div[data-file-item-id]") as HTMLElement;

      await waitFor(() => {
        const viewportRect = viewport.getBoundingClientRect();
        const pill = within(row).getByTitle(/LFS/);

        expect(label.getBoundingClientRect().width).toBeGreaterThanOrEqual(64);
        expect(pill.getBoundingClientRect().right).toBeLessThanOrEqual(viewportRect.right + 1);
      });
    }

    // The row is narrower than 16rem, so the pill drops its size segment.
    expect(canvas.getByText("42 MB")).not.toBeVisible();
    expect(canvas.getByText("14 MB")).not.toBeVisible();

    const downloadedName = await canvas.findByText(narrowLfsDownloadedName);
    await userEvent.click(downloadedName);
    await waitFor(() =>
      expect(canvas.getByTestId("narrow-lfs-selected")).toHaveTextContent("Selected: data/raw/downloaded.bam"),
    );
  },
};

const ScrollingLfsFileExplorer = () => {
  const items = React.useMemo(createNarrowLfsItems, []);

  return (
    <div style={{ width: 240 }}>
      <FileExplorer
        initialItems={items}
        getItemStatusAction={(item) => fileExplorerGitLfsPillAction(item, () => {})}
      />
    </div>
  );
};

export const DefaultModeScrollsHorizontallyForLongNames: StoryObj<typeof ScrollingLfsFileExplorer> = {
  render: () => <ScrollingLfsFileExplorer />,

  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await canvas.findByText(narrowLfsDownloadedName);
    const scrollContainer = await canvas.findByTestId("file-explorer-scroll-container");

    await waitFor(() => expect(scrollContainer.scrollWidth).toBeGreaterThan(scrollContainer.clientWidth));
  },
};

const meta: Meta<typeof FileExplorerExample> = {
  title: "Page Components/FileExplorer",
  component: FileExplorerExample,
  parameters: {
    layout: "fullscreen",
  },
};

export default meta;
type Story = StoryObj<typeof FileExplorerExample>;

export const Default: Story = {
  render: () => <FileExplorerExample />,

  play: (async ({ canvasElement }: { canvasElement: HTMLElement }) => {
    const canvas = within(canvasElement);

    const container = await canvas.findByTestId("file-explorer-container");
    expect(container).toBeTruthy();

    const resume = await canvas.findByText("resume.pdf");
    expect(resume).toBeTruthy();

    const folder = await canvas.findByText("My Files");
    await userEvent.click(folder);
  }),
};

export const DirectoryArrowsReflectLoadability: StoryObj<typeof LazyLoadDirectoryFileExplorer> = {
  render: () => <LazyLoadDirectoryFileExplorer />,

  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    // A folder row reports aria-expanded only when it can expand.
    const emptyFolder = await canvas.findByRole("treeitem", { name: "Empty Folder" });
    await expect(emptyFolder).not.toHaveAttribute("aria-expanded");

    const lazyFolder = await canvas.findByRole("treeitem", { name: "Lazy Folder" });
    await expect(lazyFolder).toHaveAttribute("aria-expanded", "false");
    await expect(canvas.queryByText("Lazy Child.txt")).toBeNull();

    await userEvent.click(within(lazyFolder).getByRole("button", { name: "Lazy Folder" }));

    await waitFor(() => {
      expect(canvas.getByText("Lazy Child.txt")).toBeInTheDocument();
    });
  },
};

export const SelectingLazyDirectoryMaterializesChildren: StoryObj<typeof LazyLoadDirectoryFileExplorer> = {
  render: () => <LazyLoadDirectoryFileExplorer />,

  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await expect(canvas.queryByText("Lazy Child.txt")).toBeNull();
    await userEvent.click(await canvas.findByText("Lazy Folder"));

    await waitFor(() => expect(canvas.getByText("Lazy Child.txt")).toBeInTheDocument());
    await expect(canvas.getByRole("treeitem", { name: "Lazy Folder" })).toHaveAttribute("aria-expanded", "true");

    await userEvent.click(await canvas.findByText("Lazy Folder"));

    await waitFor(() => expect(canvas.queryByText("Lazy Child.txt")).toBeNull());
    await expect(canvas.getByRole("treeitem", { name: "Lazy Folder" })).toHaveAttribute("aria-expanded", "false");
  },
};

export const ContextMenuExpansionMaterializesChildren: StoryObj<typeof LazyLoadDirectoryFileExplorer> = {
  render: () => <LazyLoadDirectoryFileExplorer />,

  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const lazyFolder = await canvas.findByText("Lazy Folder");
    const lazyFolderItem = lazyFolder.closest("[data-file-item-id]");

    await waitFor(() => expect(lazyFolderItem).toBeTruthy());

    if (!lazyFolderItem) {
      throw new Error("Expected lazy folder item for context menu test.");
    }

    fireEvent.contextMenu(lazyFolderItem, { clientX: 24, clientY: 24, bubbles: true });
    await userEvent.click(await screen.findByText("Expand"));

    await waitFor(() => expect(canvas.getByText("Lazy Child.txt")).toBeInTheDocument());
  },
};

export const InitialExpandedHintIsApplied: StoryObj<typeof InitiallyExpandedFileExplorer> = {
  render: () => <InitiallyExpandedFileExplorer />,

  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await expect(await canvas.findByText("Initially Visible.txt")).toBeInTheDocument();
    await expect(canvas.getByRole("treeitem", { name: "Initially Expanded" })).toHaveAttribute("aria-expanded", "true");
  },
};

export const SelectedFileHighlightPersistsAfterParentReopen: StoryObj<typeof SelectedPathFileExplorer> = {
  render: () => <SelectedPathFileExplorer />,

  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    const selectedFileLabel = await canvas.findByText("selected-report.txt");
    await expect(selectedFileLabel).toHaveClass(/swt:font-semibold/);
    await expect(selectedFileLabel).toHaveClass(/swt:text-primary/);

    // The parent row is highlighted as part of the selected path.
    const parentFolder = await canvas.findByRole("treeitem", { name: "Selected Parent" });
    await expect(parentFolder).toHaveAttribute("aria-expanded", "true");
    const parentButton = within(parentFolder).getByRole("button", { name: "Selected Parent" });
    await expect(parentButton.closest("[data-file-item-id]")).toHaveClass(/swt:bg-base-300/);
    await userEvent.click(parentButton);

    await waitFor(() => {
      expect(canvas.queryByText("selected-report.txt")).toBeNull();
    });

    await userEvent.click(await canvas.findByRole("button", { name: "Selected Parent" }));

    const selectedFileLabelAfterReopen = await canvas.findByText("selected-report.txt");
    await expect(selectedFileLabelAfterReopen).toHaveClass(/swt:font-semibold/);
    await expect(selectedFileLabelAfterReopen).toHaveClass(/swt:text-primary/);
  },
};

export const ControlledSelectionFollowsTheParent: StoryObj<typeof ControlledSelectionFileExplorer> = {
  render: () => <ControlledSelectionFileExplorer />,

  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const selectedId = () => canvas.getByTestId("controlled-selected-id");
    const row = (name: string) => canvas.getByRole("treeitem", { name });
    const rowContent = (name: string) => row(name).querySelector("[data-file-item-id]");
    const expectPath = async (highlighted: string[], unhighlighted: string[]) => {
      for (const name of highlighted) {
        await expect(rowContent(name)).toHaveClass(/swt:bg-base-300/);
      }
      for (const name of unhighlighted) {
        await expect(rowContent(name)).not.toHaveClass(/swt:bg-base-300/);
      }
    };

    await waitFor(() => expect(canvas.getByText("first.txt")).toHaveClass(/swt:text-primary/));

    // Keep the original controlled-selection cases.
    await userEvent.click(canvas.getByText("locked.txt"));
    await expect(canvas.getByText("locked.txt")).not.toHaveClass(/swt:text-primary/);
    await expect(canvas.getByText("first.txt")).toHaveClass(/swt:text-primary/);

    await userEvent.click(canvas.getByText("second.txt"));
    await waitFor(() => expect(canvas.getByText("second.txt")).toHaveClass(/swt:text-primary/));
    await expect(canvas.getByText("first.txt")).not.toHaveClass(/swt:text-primary/);

    await userEvent.click(canvas.getByText("Entity Folder"));
    await waitFor(() => expect(canvas.getByText("entity-child.txt")).toBeInTheDocument());
    await userEvent.click(canvas.getByText("entity-child.txt"));
    await userEvent.click(canvas.getByText("Entity Folder"));
    await waitFor(() => expect(canvas.queryByText("entity-child.txt")).toBeNull());
    await expect(selectedId()).toHaveTextContent("entity-folder");
    await userEvent.click(canvas.getByText("Entity Folder"));
    await waitFor(() => expect(canvas.getByText("entity-child.txt")).toBeInTheDocument());
    await expect(row("Entity Folder")).toHaveAttribute("aria-selected", "true");
    await expect(row("entity-child.txt")).toHaveAttribute("aria-selected", "false");

    // The added fixture continues with a deeper ARC hierarchy.
    await userEvent.click(within(row("studies")).getByText("studies"));
    await userEvent.click(within(row("selected-study")).getByText("selected-study"));
    await userEvent.click(within(row("resources")).getByText("resources"));
    await userEvent.click(within(row("raw-data")).getByText("raw-data"));
    await userEvent.click(within(row("batch-01")).getByText("batch-01"));
    await userEvent.click(within(row("replicate-01")).getByText("replicate-01"));
    await userEvent.click(canvas.getByText("selected-report.txt"));
    await waitFor(() => expect(selectedId()).toHaveTextContent("selected-report"));

    const selectedPath = [
      "studies",
      "selected-study",
      "resources",
      "raw-data",
      "batch-01",
      "replicate-01",
      "selected-report.txt",
    ];

    await expect(selectedId()).toHaveTextContent("selected-report");
    await expect(row("selected-report.txt")).toHaveAttribute("aria-selected", "true");
    await expectPath(selectedPath, ["isa.study.xlsx", "sibling-report.txt", "neighbor-study", "assays"]);

    // Collapse a folder several levels above the selected file.
    await userEvent.click(within(row("batch-01")).getByText("batch-01"));
    await waitFor(() => expect(canvas.queryByText("selected-report.txt")).toBeNull());
    await expect(row("batch-01")).toHaveAttribute("aria-expanded", "false");
    await expect(selectedId()).toHaveTextContent("batch-01");
    await expectPath(["studies", "selected-study", "resources", "raw-data", "batch-01"], ["isa.study.xlsx", "neighbor-study", "assays"]);

    // Reopening selects the folder, not every descendant below it.
    await userEvent.click(within(row("batch-01")).getByText("batch-01"));
    await waitFor(() => expect(canvas.getByText("selected-report.txt")).toBeInTheDocument());
    await expect(row("batch-01")).toHaveAttribute("aria-selected", "true");
    await expect(row("selected-report.txt")).toHaveAttribute("aria-selected", "false");
    await expectPath(["studies", "selected-study", "resources", "raw-data", "batch-01"], ["replicate-01", "selected-report.txt", "sibling-report.txt", "neighbor-study", "assays"]);

    // Selecting the ISA study file moves the path without highlighting its neighbors.
    await userEvent.click(canvas.getByText("isa.study.xlsx"));
    await waitFor(() => expect(selectedId()).toHaveTextContent("isa-study"));
    await expect(row("isa.study.xlsx")).toHaveAttribute("aria-selected", "true");
    await expectPath(["studies", "selected-study", "isa.study.xlsx"], ["resources", "raw-data", "batch-01", "neighbor-study", "assays"]);

    // Selecting an entity on another branch clears every highlight from the old branch.
    await userEvent.click(within(row("assays")).getByText("assays"));
    await waitFor(() => expect(row("selected-assay")).toBeInTheDocument());
    await userEvent.click(within(row("selected-assay")).getByText("selected-assay"));
    await waitFor(() => expect(selectedId()).toHaveTextContent("selected-assay"));
    await expect(row("selected-assay")).toHaveAttribute("aria-selected", "true");
    await expectPath(["assays", "selected-assay"], ["studies", "selected-study", "isa.study.xlsx", "resources", "neighbor-study", "isa.assay.xlsx"]);
  },
};

export const CollapsedDirectoryStaysClosedAfterLazySiblingLoads: StoryObj<typeof ExpansionRefreshFileExplorer> = {
  render: () => <ExpansionRefreshFileExplorer />,

  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await userEvent.click(await canvas.findByRole("button", { name: "Selected Parent" }));
    await waitFor(() => expect(canvas.queryByText("selected-report.txt")).toBeNull());

    await userEvent.click(await canvas.findByRole("button", { name: "Lazy Folder" }));
    await waitFor(() => expect(canvas.getByText("Lazy Child.txt")).toBeInTheDocument());

    await expect(canvas.queryByText("selected-report.txt")).toBeNull();
    await expect(canvas.getByRole("treeitem", { name: "Selected Parent" })).toHaveAttribute("aria-expanded", "false");
  },
};

export const ContextMenuCreatesInMemoryUntilSave: StoryObj<typeof InMemoryCreateFileExplorer> = {
  render: () => <InMemoryCreateFileExplorer />,

  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const rootFolder = await canvas.findByText("ARC Root");

    await waitFor(() => expect(rootFolder.closest("[data-file-item-id]")).toBeTruthy());
    fireEvent.contextMenu(rootFolder, { clientX: 24, clientY: 24, bubbles: true });

    const addMenuItems = await Promise.all(
      arcCreateItems.map((item) => screen.findByText(item.label)),
    );

    expect(addMenuItems.map((item) => item.textContent)).toEqual(arcCreateItems.map((item) => item.label));

    await userEvent.click(addMenuItems[1]);

    await waitFor(() => {
      expect(canvas.getByTestId("pending-draft")).toHaveTextContent("Pending draft: assays/NewAssay/isa.assay.xlsx");
      expect(canvas.getByTestId("saved-count")).toHaveTextContent("Saved count: 0");
      expect(canvas.getByTestId("saved-paths")).toHaveTextContent("Saved paths: none");
    });

    await userEvent.click(canvas.getByRole("button", { name: "Save" }));

    await waitFor(() => {
      expect(canvas.getByTestId("pending-draft")).toHaveTextContent("Pending draft: none");
      expect(canvas.getByTestId("saved-count")).toHaveTextContent("Saved count: 1");
      expect(canvas.getByTestId("saved-paths")).toHaveTextContent("Saved paths: assays/NewAssay/isa.assay.xlsx");
    });
  },
};

export const DataMapAddSelectDeleteLifecycle: StoryObj<typeof DataMapLifecycleFileExplorer> = {
  render: () => <DataMapLifecycleFileExplorer />,
  parameters: { isolated: true },

  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const portal = within(canvasElement.ownerDocument.body);
    const assayLabel = await canvas.findByText("DataMapAssay");
    const assayItem = assayLabel.closest("[data-file-item-id]");

    expect(assayItem).toBeTruthy();
    expect(canvas.queryByText("isa.datamap.xlsx")).not.toBeInTheDocument();

    fireEvent.contextMenu(assayItem!, { clientX: 24, clientY: 24, bubbles: true });
    await userEvent.click(await portal.findByText("Add DataMap"));

    const dataMapLabel = await canvas.findByText("isa.datamap.xlsx");
    expect(dataMapLabel).toBeVisible();

    await userEvent.click(dataMapLabel);
    await waitFor(() => {
      expect(canvas.getByTestId("datamap-selected-path")).toHaveTextContent(`Selected path: ${DATA_MAP_PATH}`);
      expect(dataMapLabel).toHaveClass(/swt:font-semibold/);
      expect(dataMapLabel).toHaveClass(/swt:text-primary/);
    });

    const dataMapItem = dataMapLabel.closest("[data-file-item-id]");
    expect(dataMapItem).toBeTruthy();
    fireEvent.contextMenu(dataMapItem!, { clientX: 24, clientY: 24, bubbles: true });
    await userEvent.click(await portal.findByText("Delete DataMap"));

    await waitFor(() => {
      expect(canvas.queryByText("isa.datamap.xlsx")).not.toBeInTheDocument();
      expect(canvas.getByTestId("datamap-selected-path")).toHaveTextContent("Selected path: none");
    });
  },
};

export const InlineDeleteDispatchesForFileAndDirectory: StoryObj<typeof DeleteActionFileExplorer> = {
  render: () => <DeleteActionFileExplorer />,

  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    const deletableDirectory = await canvas.findByText("Deletable Folder");
    await userEvent.hover(deletableDirectory);
    await userEvent.click(await canvas.findByRole("button", { name: "Mark Deletable Folder" }));
    await waitFor(() => expect(canvas.getByTestId("last-row-action")).toHaveTextContent("Last action: Deletable Folder"));
    await userEvent.click(await canvas.findByRole("button", { name: "Delete Deletable Folder" }));
    await waitFor(() => expect(canvas.getByTestId("last-deleted")).toHaveTextContent("Last deleted: Deletable Folder"));

    const deletableFile = await canvas.findByText("Deletable File");
    await userEvent.hover(deletableFile);
    await userEvent.click(await canvas.findByRole("button", { name: "Mark Deletable File" }));
    await waitFor(() => expect(canvas.getByTestId("last-row-action")).toHaveTextContent("Last action: Deletable File"));
    await userEvent.click(await canvas.findByRole("button", { name: "Delete Deletable File" }));
    await waitFor(() => expect(canvas.getByTestId("last-deleted")).toHaveTextContent("Last deleted: Deletable File"));
  },
};

export const ContextMenuItemDispatchesAction: StoryObj<typeof DestructiveContextMenuFileExplorer> = {
  render: () => <DestructiveContextMenuFileExplorer />,

  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const targetFile = await canvas.findByText("Context File");
    const fileItem = targetFile.closest("[data-file-item-id]");

    await waitFor(() => expect(fileItem).toBeTruthy());

    if (!fileItem) {
      throw new Error("Expected file item element for context menu test.");
    }

    fireEvent.contextMenu(fileItem, { clientX: 30, clientY: 30, bubbles: true });
    const deleteMenuItem = await screen.findByText("Delete Item");
    await userEvent.click(deleteMenuItem);
    await waitFor(() =>
      expect(canvas.getByTestId("context-click-count")).toHaveTextContent("Context clicks: 1"),
    );
  },
};

export const LfsContextMenuStates: StoryObj<typeof LfsContextMenuFileExplorer> = {
  render: () => <LfsContextMenuFileExplorer />,

  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    const pointerPill = await canvas.findByLabelText("Download LFS file Pointer LFS. LFS Pointer - 42 MB");
    await userEvent.click(pointerPill);
    await waitFor(() => expect(canvas.getByTestId("lfs-download-count")).toHaveTextContent("Downloads: 1"));

    const downloadedPill = await canvas.findByLabelText("LFS Downloaded - 14 MB");
    expect(downloadedPill.tagName).not.toBe("BUTTON");
    await userEvent.click(downloadedPill);
    expect(canvas.getByTestId("lfs-free-count")).toHaveTextContent("Freed: 0");

    const downloadedFile = await canvas.findByText("Downloaded LFS");
    const downloadedItem = downloadedFile.closest("[data-file-item-id]");
    await waitFor(() => expect(downloadedItem).toBeTruthy());

    if (!downloadedItem) {
      throw new Error("Expected downloaded LFS file item.");
    }

    fireEvent.contextMenu(downloadedItem, { clientX: 30, clientY: 30, bubbles: true });
    const enabledMenuItem = await screen.findByText("Free local LFS copy");
    const disabledDownloadMenuItem = await screen.findByText("Download LFS file");
    await expect(enabledMenuItem).toBeVisible();
    await expect(enabledMenuItem).not.toHaveClass("swt:opacity-50");
    await expect(disabledDownloadMenuItem).toHaveClass("swt:opacity-50");
    await userEvent.click(enabledMenuItem);
    await waitFor(() => expect(canvas.getByTestId("lfs-free-count")).toHaveTextContent("Freed: 1"));

    await userEvent.click(document.body);

    const pointerFile = await canvas.findByText("Pointer LFS");
    const pointerItem = pointerFile.closest("[data-file-item-id]");
    await waitFor(() => expect(pointerItem).toBeTruthy());

    if (!pointerItem) {
      throw new Error("Expected pointer LFS file item.");
    }

    fireEvent.contextMenu(pointerItem, { clientX: 30, clientY: 30, bubbles: true });
    const enabledDownloadMenuItem = await screen.findByText("Download LFS file");
    const disabledMenuItem = await screen.findByText("Free local LFS copy");
    await expect(enabledDownloadMenuItem).toBeVisible();
    await expect(enabledDownloadMenuItem).not.toHaveClass("swt:opacity-50");
    await expect(disabledMenuItem).toBeVisible();
    await expect(disabledMenuItem).toHaveClass("swt:opacity-50");

    await userEvent.click(document.body);

    const plainFile = await canvas.findByText("Plain File");
    const plainItem = plainFile.closest("[data-file-item-id]");
    await waitFor(() => expect(plainItem).toBeTruthy());

    if (!plainItem) {
      throw new Error("Expected plain file item.");
    }

    fireEvent.contextMenu(plainItem, { clientX: 30, clientY: 30, bubbles: true });
    await expect(screen.queryByText("Free local LFS copy")).toBeNull();
  },
};

export const CopyPathUsesRelativePathByDefault: StoryObj<typeof CopyPathDefaultFileExplorer> = {
  render: () => <CopyPathDefaultFileExplorer />,

  play: async ({ canvasElement }) => {
    await expectContextMenuCopy(canvasElement, "Relative File", "Copy Path", "studies/A/file.txt");
  },
};

export const CopyPathUsesProvidedResolver: StoryObj<typeof CopyPathResolverFileExplorer> = {
  render: () => <CopyPathResolverFileExplorer />,

  play: async ({ canvasElement }) => {
    await expectContextMenuCopy(canvasElement, "Absolute File", "Copy Path", "C:/arc/studies/A/file.txt");
  },
};

export const CopyRelativePathUsesProvidedResolver: StoryObj<typeof CopyPathResolverFileExplorer> = {
  render: () => <CopyPathResolverFileExplorer />,

  play: async ({ canvasElement }) => {
    await expectContextMenuCopy(canvasElement, "Absolute File", "Copy Relative Path", "studies/A/file.txt");
  },
};

export const BusyLfsRowKeepsNameSpace: StoryObj<typeof BusyLfsRowFileExplorer> = {
  render: () => <BusyLfsRowFileExplorer />,

  play: async ({ canvasElement }) => {
    await waitFor(() => {
      const idleLabel = lfsRowLabel(canvasElement, "lfs-idle");
      const busyLabel = lfsRowLabel(canvasElement, "lfs-busy");

      const widths = JSON.stringify({
        idle: [idleLabel.clientWidth, idleLabel.scrollWidth],
        busy: [busyLabel.clientWidth, busyLabel.scrollWidth],
      });

      expect(idleLabel.clientWidth, widths).toBeGreaterThanOrEqual(idleLabel.scrollWidth);
      expect(busyLabel.clientWidth, widths).toBeGreaterThanOrEqual(busyLabel.scrollWidth);
      expect(busyLabel.getBoundingClientRect().width, widths).toBeGreaterThanOrEqual(
        idleLabel.getBoundingClientRect().width - 1,
      );
    });
  },
};

export const LongNamesKeepInlineControlsVisible: StoryObj<typeof TruncatedOverflowFileExplorer> = {
  render: () => <TruncatedOverflowFileExplorer />,

  play: async ({ canvasElement }) => {
    await expectLongNameControlsToStayVisible(canvasElement, veryLongFolderName);
    await expectLongNameControlsToStayVisible(canvasElement, veryLongFileName);
  },
};

const LARGE_TREE_FILE_COUNT = 10000;

const LargeTreeFileExplorer = () => {
  const items = React.useMemo(
    () =>
      ofArray(
        Array.from({ length: LARGE_TREE_FILE_COUNT }, (_, index) =>
          createStableFile(`file-${index + 1}.txt`, `data/file-${index + 1}.txt`, `large-${index + 1}`),
        ),
      ),
    [],
  );

  return (
    <div data-testid="large-tree-viewport" style={{ height: 400, width: 320 }}>
      <FileExplorer initialItems={items} truncateOverflowingItemNames={true} />
    </div>
  );
};

export const LargeTreeRendersOnlyVisibleRows: StoryObj<typeof LargeTreeFileExplorer> = {
  render: () => <LargeTreeFileExplorer />,

  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await canvas.findByText("file-1.txt");
    await expect(canvas.getAllByRole("treeitem").length).toBeLessThan(100);

    const scrollContainer = await canvas.findByTestId("file-explorer-scroll-container");
    scrollContainer.scrollTop = scrollContainer.scrollHeight;

    await waitFor(() => expect(canvas.getByText(`file-${LARGE_TREE_FILE_COUNT}.txt`)).toBeInTheDocument());
    await expect(canvas.getAllByRole("treeitem").length).toBeLessThan(100);
    await expect(canvas.getByRole("tree")).toBeInTheDocument();
  },
};

export const RefreshFolderIsOfferedForEveryDirectory: StoryObj<typeof LazyLoadDirectoryFileExplorer> = {
  render: () => {
    const refreshed: string[] = [];
    return (
      <div>
        <FileExplorer
          initialItems={ofArray([
            createStableFolder("Empty Folder", "empty", "empty-folder", []),
            createStableFile("notes.txt", "notes.txt", "notes"),
          ])}
          onRefreshDirectory={(item: FileItem) => {
            refreshed.push(item.Id);
            document.body.dataset.refreshed = refreshed.join(",");
          }}
        />
      </div>
    );
  },

  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const folder = await canvas.findByText("Empty Folder");

    fireEvent.contextMenu(folder, { clientX: 24, clientY: 24, bubbles: true });
    await userEvent.click(await screen.findByText("Refresh folder"));

    await waitFor(() => expect(document.body.dataset.refreshed).toBe("empty-folder"));

    fireEvent.contextMenu(await canvas.findByText("notes.txt"), { clientX: 24, clientY: 24, bubbles: true });
    await waitFor(() => expect(screen.queryByText("Refresh folder")).toBeNull());
  },
};
