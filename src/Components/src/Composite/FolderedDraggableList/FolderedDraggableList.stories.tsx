import React from 'react';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { DndContext, useDraggable, useDroppable, type DragEndEvent } from '@dnd-kit/core';
import { expect, fireEvent, userEvent, waitFor, within } from 'storybook/test';
import { FolderedDraggableList } from './FolderedDraggableList.fs.js';
import { ofArray, type FSharpList } from '../../fable_modules/fable-library-ts.5.0.0-alpha.21/List.ts';

type PropertyPayload = {
  LayerId: string;
  Origin: string;
  Header: string;
  Connections: string[];
};

type StoryItem = {
  Id: string;
  Label: string;
  DragKey: string;
  Payload: PropertyPayload;
  Color?: string;
  Badge?: string;
  Tooltip?: string;
  Disabled: boolean;
};

type StoryFolder = {
  Id: string;
  Name: string;
  Color?: string;
  Items: FSharpList<StoryItem>;
};

type StoryListProps = {
  folders: FSharpList<StoryFolder>;
  dragId: (folder: StoryFolder, item: StoryItem) => string;
  activeFolderId?: string;
  defaultActiveFolderId?: string;
  onActiveFolderIdChange?: (activeFolderId: string) => void;
  shelfDropId?: string;
  tryCreateItemFromExternalDrop?: (drop: StoryExternalDrop) => StoryItem | undefined;
  onFoldersChange?: (folders: FSharpList<StoryFolder>) => void;
  onSetFolderColor?: (folderId: string, color?: string) => void;
  className?: string;
  debug?: boolean;
};

type StoryExternalDrop = {
  TargetFolder: StoryFolder;
  ActiveId: string;
  ActiveData: unknown;
  Folders: FSharpList<StoryFolder>;
};

type StoryDragData = {
  FolderId: string;
  FolderName: string;
  FolderColor?: string;
  ItemId: string;
  ItemLabel: string;
  ItemColor?: string;
  EffectiveColor?: string;
  Payload: PropertyPayload;
};

type ExternalPropertyDragData = {
  Kind: 'external-property';
  Id: string;
  Label: string;
  Origin: string;
};

const StoryFolderedDraggableList = FolderedDraggableList as React.ComponentType<StoryListProps>;

function fsList<T>(items: T[]): FSharpList<T> {
  return ofArray(items);
}

function propertyItem(
  id: string,
  label: string,
  layerId: string,
  origin: string,
  color?: string,
  badge?: string,
  disabled = false
): StoryItem {
  return {
    Id: id,
    Label: label,
    DragKey: `story-drag-${id}`,
    Payload: {
      LayerId: layerId,
      Origin: origin,
      Header: label,
      Connections: [`connection:${id}`],
    },
    Color: color,
    Badge: badge,
    Tooltip: `${origin} / ${label}`,
    Disabled: disabled,
  };
}

function folder(id: string, name: string, color: string | undefined, items: StoryItem[]): StoryFolder {
  return {
    Id: id,
    Name: name,
    Color: color,
    Items: fsList(items),
  };
}

function initialFolders() {
  return fsList([
    folder('layer-a', 'Layer A', '#2563eb', [
      propertyItem('layer-a-mass', 'Mass', 'layer-a', 'previous-table', undefined, '3'),
      propertyItem('layer-a-species', 'Species', 'layer-a', 'previous-table', '#16a34a', '2'),
    ]),
    folder('layer-b', 'Layer B', '#d97706', [
      propertyItem('layer-b-instrument', 'Instrument', 'layer-b', 'upstream-layer', undefined, '1'),
      propertyItem('layer-b-archived', 'Archived method', 'layer-b', 'upstream-layer', '#6b7280', 'disabled', true),
    ]),
  ]);
}

function overflowingFolders() {
  return fsList(
    Array.from({ length: 8 }, (_, folderIndex) =>
      folder(
        `wide-layer-${folderIndex + 1}`,
        `Wide Layer ${folderIndex + 1}`,
        folderIndex % 2 === 0 ? '#2563eb' : '#d97706',
        Array.from({ length: 8 }, (_, itemIndex) =>
          propertyItem(
            `wide-layer-${folderIndex + 1}-property-${itemIndex + 1}`,
            `Property ${itemIndex + 1}`,
            `wide-layer-${folderIndex + 1}`,
            'overflow-story',
            itemIndex % 2 === 0 ? '#16a34a' : undefined,
            `${itemIndex + 1}`
          )
        )
      )
    )
  );
}

function dragId(_folder: StoryFolder, item: StoryItem) {
  // Opaque stable identity only. Consumers must read metadata from event.active.data.current.
  return item.DragKey;
}

function DropZone() {
  const droppable = useDroppable({ id: 'outside-drop' });

  return (
    <div
      ref={droppable.setNodeRef}
      data-testid="outside-drop"
      className={`swt:min-h-16 swt:rounded swt:border swt:border-dashed swt:p-3 ${
        droppable.isOver ? 'swt:border-primary swt:bg-primary/10' : 'swt:border-base-300'
      }`}
    >
      Drop outside
    </div>
  );
}

function OutsideProperty() {
  const draggable = useDraggable({
    id: 'outside-property-temperature',
    data: {
      Kind: 'external-property',
      Id: 'temperature',
      Label: 'Temperature',
      Origin: 'outside-source',
    } satisfies ExternalPropertyDragData,
  });

  return (
    <button
      ref={draggable.setNodeRef}
      type="button"
      data-testid="outside-property-temperature"
      className="swt:btn swt:btn-sm swt:btn-outline swt:w-fit swt:cursor-grab active:swt:cursor-grabbing"
      {...draggable.attributes}
      {...draggable.listeners}
    >
      External temperature
    </button>
  );
}

function UpdatingHarness() {
  const [folders, setFolders] = React.useState(() => initialFolders());

  const addReplicate = () => {
    setFolders(
      fsList([
        folder('layer-a', 'Layer A', '#2563eb', [
          propertyItem('layer-a-mass', 'Mass', 'layer-a', 'previous-table', undefined, '3'),
          propertyItem('layer-a-species', 'Species', 'layer-a', 'previous-table', '#16a34a', '2'),
          propertyItem('layer-a-replicate', 'Replicate', 'layer-a', 'previous-table', '#dc2626', '4'),
        ]),
        folder('layer-b', 'Layer B', '#d97706', [
          propertyItem('layer-b-instrument', 'Instrument', 'layer-b', 'upstream-layer', undefined, '1'),
          propertyItem('layer-b-archived', 'Archived method', 'layer-b', 'upstream-layer', '#6b7280', 'disabled', true),
        ]),
      ])
    );
  };

  const removeSpecies = () => {
    setFolders(
      fsList([
        folder('layer-a', 'Layer A', '#2563eb', [
          propertyItem('layer-a-mass', 'Mass', 'layer-a', 'previous-table', undefined, '3'),
        ]),
        folder('layer-b', 'Layer B', '#d97706', [
          propertyItem('layer-b-instrument', 'Instrument', 'layer-b', 'upstream-layer', undefined, '1'),
          propertyItem('layer-b-archived', 'Archived method', 'layer-b', 'upstream-layer', '#6b7280', 'disabled', true),
        ]),
      ])
    );
  };

  const renameAndRecolorLayerA = () => {
    setFolders(
      fsList([
        folder('layer-a', 'Layer Alpha', '#7c3aed', [
          propertyItem('layer-a-mass', 'Mass', 'layer-a', 'previous-table', undefined, '3'),
          propertyItem('layer-a-species', 'Species', 'layer-a', 'previous-table', '#16a34a', '2'),
        ]),
        folder('layer-b', 'Layer B', '#d97706', [
          propertyItem('layer-b-instrument', 'Instrument', 'layer-b', 'upstream-layer', undefined, '1'),
          propertyItem('layer-b-archived', 'Archived method', 'layer-b', 'upstream-layer', '#6b7280', 'disabled', true),
        ]),
      ])
    );
  };

  return (
    <div className="swt:flex swt:flex-col swt:gap-3 swt:w-96">
      <div className="swt:flex swt:gap-2">
        <button type="button" className="swt:btn swt:btn-xs swt:btn-outline" onClick={addReplicate}>
          Add replicate
        </button>
        <button type="button" className="swt:btn swt:btn-xs swt:btn-outline" onClick={removeSpecies}>
          Remove species
        </button>
        <button type="button" className="swt:btn swt:btn-xs swt:btn-outline" onClick={renameAndRecolorLayerA}>
          Rename and recolor Layer A
        </button>
      </div>
      <DndContext>
        <StoryFolderedDraggableList folders={folders} dragId={dragId} debug />
      </DndContext>
    </div>
  );
}

function OverflowToggleHarness() {
  const [folders, setFolders] = React.useState(() => initialFolders());

  return (
    <div className="swt:w-72">
      <button
        type="button"
        className="swt:btn swt:btn-xs swt:btn-outline swt:mb-2"
        onClick={() => setFolders(overflowingFolders())}
      >
        Overflow properties
      </button>
      <DndContext>
        <StoryFolderedDraggableList folders={folders} dragId={dragId} debug />
      </DndContext>
    </div>
  );
}

function FolderColorHarness() {
  const [folders, setFolders] = React.useState(() => initialFolders());

  const setFolderColor = (folderId: string, color?: string) => {
    setFolders((current) =>
      fsList(
        Array.from(current).map((currentFolder) =>
          currentFolder.Id === folderId
            ? {
                ...currentFolder,
                Color: color,
              }
            : currentFolder,
        ),
      ),
    );
  };

  return (
    <div className="swt:flex swt:w-96 swt:flex-col swt:gap-3">
      <DndContext>
        <StoryFolderedDraggableList folders={folders} dragId={dragId} onSetFolderColor={setFolderColor} debug />
      </DndContext>
    </div>
  );
}

function OutsideDropHarness() {
  const [folders, setFolders] = React.useState(() => initialFolders());

  const targetFolder = Array.from(folders).find((candidate) => candidate.Id === 'layer-b');
  const targetFolderCount = targetFolder ? Array.from(targetFolder.Items).length : 0;

  const tryCreateItemFromExternalDrop = (drop: StoryExternalDrop): StoryItem | undefined => {
    const activeData = drop.ActiveData as Partial<ExternalPropertyDragData> | undefined;

    if (activeData?.Kind !== 'external-property' || !activeData.Id || !activeData.Label) {
      return undefined;
    }

    return propertyItem(
      `${drop.TargetFolder.Id}-${activeData.Id}`,
      activeData.Label,
      drop.TargetFolder.Id,
      activeData.Origin ?? 'outside-source'
    );
  };

  return (
    <div className="swt:flex swt:w-96 swt:flex-col swt:gap-3">
      <DndContext>
        <OutsideProperty />
        <StoryFolderedDraggableList
          folders={folders}
          dragId={dragId}
          defaultActiveFolderId="layer-b"
          shelfDropId="story-foldered-shelf"
          tryCreateItemFromExternalDrop={tryCreateItemFromExternalDrop}
          onFoldersChange={setFolders}
          debug
        />
      </DndContext>
      <pre data-testid="layer-b-count">{targetFolderCount}</pre>
    </div>
  );
}

function ControlledActiveTabHarness() {
  const [activeFolderId, setActiveFolderId] = React.useState('layer-a');

  return (
    <div className="swt:flex swt:flex-col swt:gap-3 swt:w-96">
      <DndContext>
        <StoryFolderedDraggableList
          folders={initialFolders()}
          dragId={dragId}
          activeFolderId={activeFolderId}
          onActiveFolderIdChange={setActiveFolderId}
          debug
        />
      </DndContext>
      <pre data-testid="last-active">{activeFolderId}</pre>
    </div>
  );
}

function DragHarness() {
  const [lastDrag, setLastDrag] = React.useState('none');

  const onDragEnd = (event: DragEndEvent) => {
    const current = event.active.data.current as StoryDragData;
    setLastDrag(
      JSON.stringify({
        overId: event.over?.id ?? null,
        folderId: current.FolderId,
        folderName: current.FolderName,
        folderColor: current.FolderColor ?? null,
        itemId: current.ItemId,
        itemLabel: current.ItemLabel,
        itemColor: current.ItemColor ?? null,
        effectiveColor: current.EffectiveColor ?? null,
        layerId: current.Payload.LayerId,
        origin: current.Payload.Origin,
        header: current.Payload.Header,
      })
    );
  };

  return (
    <div className="swt:flex swt:flex-col swt:gap-3 swt:w-96">
      <DndContext onDragEnd={onDragEnd}>
        <StoryFolderedDraggableList folders={initialFolders()} dragId={dragId} debug />
        <DropZone />
      </DndContext>
      <pre data-testid="last-drag">{lastDrag}</pre>
    </div>
  );
}

function removeItemFromFolders(folders: FSharpList<StoryFolder>, itemId: string) {
  return fsList(
    Array.from(folders).map((candidate) =>
      folder(
        candidate.Id,
        candidate.Name,
        candidate.Color,
        Array.from(candidate.Items).filter((item) => item.Id !== itemId)
      )
    )
  );
}

function ParentRemovalHarness() {
  const [folders, setFolders] = React.useState(() => initialFolders());
  const [lastDrop, setLastDrop] = React.useState('none');

  const onDragEnd = (event: DragEndEvent) => {
    const current = event.active.data.current as StoryDragData;

    if (event.over?.id === 'outside-drop') {
      setFolders((currentFolders) => removeItemFromFolders(currentFolders, current.ItemId));
      setLastDrop(`accepted:${current.ItemId}`);
    } else {
      setLastDrop(`rejected:${current.ItemId}`);
    }
  };

  return (
    <div className="swt:flex swt:flex-col swt:gap-3 swt:w-96">
      <DndContext onDragEnd={onDragEnd}>
        <StoryFolderedDraggableList folders={folders} dragId={dragId} debug />
        <DropZone />
      </DndContext>
      <pre data-testid="last-drop">{lastDrop}</pre>
    </div>
  );
}

function getPointerGeometry(source: Element, target: Element) {
  const from = source.getBoundingClientRect();
  const to = target.getBoundingClientRect();
  const fromX = from.left + from.width / 2;
  const fromY = from.top + from.height / 2;
  const toX = to.left + to.width / 2;
  const toY = to.top + to.height / 2;
  const deltaX = toX - fromX;
  const deltaY = toY - fromY;
  const distance = Math.hypot(deltaX, deltaY) || 1;
  const activationX = fromX + (deltaX / distance) * 8;
  const activationY = fromY + (deltaY / distance) * 8;

  return { fromX, fromY, toX, toY, activationX, activationY };
}

const nextFrame = () => new Promise((resolve) => requestAnimationFrame(resolve));

async function beginDragByPointer(source: Element, target: Element = source) {
  const geometry = getPointerGeometry(source, target);

  fireEvent.pointerDown(source, {
    clientX: geometry.fromX,
    clientY: geometry.fromY,
    button: 0,
    buttons: 1,
    isPrimary: true,
    pointerId: 1,
  });

  await nextFrame();

  fireEvent.pointerMove(target, {
    clientX: geometry.activationX,
    clientY: geometry.activationY,
    button: 0,
    buttons: 1,
    isPrimary: true,
    pointerId: 1,
  });

  await nextFrame();

  return geometry;
}

async function dragByPointer(source: Element, target: Element) {
  const geometry = await beginDragByPointer(source, target);

  fireEvent.pointerMove(target, {
    clientX: geometry.toX,
    clientY: geometry.toY,
    button: 0,
    buttons: 1,
    isPrimary: true,
    pointerId: 1,
  });

  await nextFrame();

  fireEvent.pointerUp(target, {
    clientX: geometry.toX,
    clientY: geometry.toY,
    button: 0,
    buttons: 0,
    isPrimary: true,
    pointerId: 1,
  });
}

const meta = {
  title: 'Composite Components/FolderedDraggableList',
  component: StoryFolderedDraggableList,
  // Deactivated for now: FolderedDraggableList is outdated.
  tags: ['skip-test'],
  parameters: { layout: 'centered' },
} satisfies Meta<typeof StoryFolderedDraggableList>;

export default meta;

type Story = StoryObj<typeof meta>;

export const SwitchesTabs: Story = {
  render: () => (
    <DndContext>
      <div className="swt:w-96">
        <StoryFolderedDraggableList folders={initialFolders()} dragId={dragId} debug />
      </div>
    </DndContext>
  ),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const shelf = canvas.getByTestId('foldered-draggable-item-shelf');

    // The first tab is active from the start; its items are visible.
    expect(canvas.getByRole('tab', { name: 'Show Layer A' })).toHaveAttribute('aria-selected', 'true');
    expect(within(shelf).getByTestId('foldered-draggable-item-layer-a-mass')).toBeVisible();
    expect(within(shelf).getByTestId('foldered-draggable-item-layer-a-species')).toBeVisible();
    expect(within(shelf).queryByTestId('foldered-draggable-item-layer-b-instrument')).not.toBeInTheDocument();
    expect(within(canvas.getByTestId('foldered-draggable-item-layer-a-species')).getByText('2')).toBeVisible();

    await userEvent.click(canvas.getByRole('tab', { name: 'Show Layer B' }));

    await waitFor(() => {
      expect(within(shelf).queryByTestId('foldered-draggable-item-layer-a-mass')).not.toBeInTheDocument();
      expect(within(shelf).getByTestId('foldered-draggable-item-layer-b-instrument')).toBeVisible();
    });

    // Clicking the active tab keeps it open - there is no collapse.
    await userEvent.click(canvas.getByRole('tab', { name: 'Show Layer B' }));
    expect(within(shelf).getByTestId('foldered-draggable-item-layer-b-instrument')).toBeVisible();
  },
};

export const DefaultActiveTabShowsItsItems: Story = {
  render: () => (
    <DndContext>
      <div className="swt:w-96">
        <StoryFolderedDraggableList
          folders={initialFolders()}
          dragId={dragId}
          defaultActiveFolderId="layer-b"
          debug
        />
      </div>
    </DndContext>
  ),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const shelf = canvas.getByTestId('foldered-draggable-item-shelf');

    expect(within(shelf).getByTestId('foldered-draggable-item-layer-b-instrument')).toBeVisible();
    expect(within(shelf).queryByTestId('foldered-draggable-item-layer-a-mass')).not.toBeInTheDocument();
  },
};

export const ControlledActiveTabFollowsProps: Story = {
  render: () => <ControlledActiveTabHarness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const shelf = canvas.getByTestId('foldered-draggable-item-shelf');

    expect(within(shelf).getByTestId('foldered-draggable-item-layer-a-mass')).toBeVisible();
    expect(within(shelf).queryByTestId('foldered-draggable-item-layer-b-instrument')).not.toBeInTheDocument();

    await userEvent.click(canvas.getByRole('tab', { name: 'Show Layer B' }));

    await waitFor(() => {
      expect(within(shelf).queryByTestId('foldered-draggable-item-layer-a-mass')).not.toBeInTheDocument();
      expect(within(shelf).getByTestId('foldered-draggable-item-layer-b-instrument')).toBeVisible();
      expect(canvas.getByTestId('last-active')).toHaveTextContent('layer-b');
    });
  },
};

export const PerTabSearchFiltersOnlyItsCard: Story = {
  render: () => (
    <DndContext>
      <div className="swt:w-96">
        <StoryFolderedDraggableList folders={initialFolders()} dragId={dragId} debug />
      </div>
    </DndContext>
  ),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const shelf = canvas.getByTestId('foldered-draggable-item-shelf');

    await userEvent.type(canvas.getByLabelText('Search in Layer A'), 'spec');
    await waitFor(() => {
      expect(within(shelf).queryByTestId('foldered-draggable-item-layer-a-mass')).not.toBeInTheDocument();
      expect(within(shelf).getByTestId('foldered-draggable-item-layer-a-species')).toBeVisible();
    });
    expect(canvas.getByText('1 of 2 items')).toBeVisible();

    // A query without matches says so instead of showing an empty shelf.
    await userEvent.type(canvas.getByLabelText('Search in Layer A'), 'xyz');
    await waitFor(() => expect(canvas.getByText('No items match "specxyz".')).toBeVisible());

    // The other card keeps its own (empty) search.
    await userEvent.click(canvas.getByRole('tab', { name: 'Show Layer B' }));
    expect(canvas.getByLabelText('Search in Layer B')).toHaveValue('');
    expect(within(shelf).getByTestId('foldered-draggable-item-layer-b-instrument')).toBeVisible();

    // Switching back restores the first card's search untouched.
    await userEvent.click(canvas.getByRole('tab', { name: 'Show Layer A' }));
    expect(canvas.getByLabelText('Search in Layer A')).toHaveValue('specxyz');
  },
};

export const HorizontalRowsScrollWhenOverflowing: Story = {
  render: () => (
    <DndContext>
      <div className="swt:w-72">
        <StoryFolderedDraggableList folders={overflowingFolders()} dragId={dragId} debug />
      </div>
    </DndContext>
  ),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const folderRow = canvas.getByTestId('foldered-draggable-folder-row');
    const shelf = canvas.getByTestId('foldered-draggable-item-shelf');

    expect(folderRow).toHaveClass('swt:flex-row');
    expect(folderRow).toHaveClass('swt:flex-nowrap');
    expect(getComputedStyle(folderRow).overflowX).toBe('auto');
    expect(getComputedStyle(folderRow).scrollbarGutter).toContain('stable');
    expect(shelf).toBeVisible();
    expect(canvas.getAllByRole('tab', { name: /Wide Layer/i })).toHaveLength(8);

    await userEvent.click(canvas.getByRole('tab', { name: 'Show Wide Layer 1' }));

    const itemRow = canvas.getByTestId('foldered-draggable-item-row');
    expect(itemRow).toHaveClass('swt:flex-row');
    expect(itemRow).toHaveClass('swt:flex-nowrap');
    expect(getComputedStyle(itemRow).overflowX).toBe('auto');
    expect(getComputedStyle(itemRow).scrollbarGutter).toContain('stable');
    expect(Math.round(itemRow.getBoundingClientRect().height)).toBeGreaterThanOrEqual(64);
    expect(within(itemRow).getAllByRole('button', { name: /Drag Property/i })).toHaveLength(8);
  },
};

export const PropertyRowKeepsHeightWhenScrollbarAppears: Story = {
  render: () => <OverflowToggleHarness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const before = Math.round(canvas.getByTestId('foldered-draggable-item-row').getBoundingClientRect().height);

    await userEvent.click(canvas.getByRole('button', { name: 'Overflow properties' }));

    await waitFor(() => {
      expect(within(canvas.getByTestId('foldered-draggable-item-row')).getAllByRole('button', { name: /Drag Property/i }))
        .toHaveLength(8);
    });

    const after = Math.round(canvas.getByTestId('foldered-draggable-item-row').getBoundingClientRect().height);
    expect(after).toBe(before);
  },
};

export const UpdatesWhenFoldersPropChanges: Story = {
  render: () => <UpdatingHarness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const shelf = canvas.getByTestId('foldered-draggable-item-shelf');

    expect(within(shelf).getByTestId('foldered-draggable-item-layer-a-species')).toBeVisible();

    await userEvent.click(canvas.getByRole('button', { name: 'Add replicate' }));
    expect(await within(shelf).findByTestId('foldered-draggable-item-layer-a-replicate')).toBeVisible();
    const replicateSwatch = canvas
      .getByTestId('foldered-draggable-item-layer-a-replicate')
      .querySelector<HTMLElement>('[data-foldered-color-swatch="true"]');
    expect(replicateSwatch).not.toBeNull();
    expect(replicateSwatch!).toHaveStyle({ backgroundColor: '#dc2626' });

    await userEvent.click(canvas.getByRole('button', { name: 'Remove species' }));
    await waitFor(() => {
      expect(within(shelf).queryByTestId('foldered-draggable-item-layer-a-species')).not.toBeInTheDocument();
    });
    expect(within(shelf).getByTestId('foldered-draggable-item-layer-a-mass')).toBeVisible();

    await userEvent.click(canvas.getByRole('button', { name: 'Rename and recolor Layer A' }));
    expect(canvas.getByRole('tab', { name: 'Show Layer Alpha' })).toBeVisible();
    const folderSwatch = canvas
      .getByTestId('foldered-draggable-folder-layer-a')
      .querySelector<HTMLElement>('[data-foldered-color-swatch="true"]');
    expect(folderSwatch).not.toBeNull();
    expect(folderSwatch!).toHaveStyle({ backgroundColor: '#7c3aed' });
  },
};

export const SetsFolderColorFromPreview: Story = {
  render: () => <FolderColorHarness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    // The color control lives in the active card's header, so picking a color
    // never switches or re-focuses tabs.
    const trigger = canvas.getByRole('button', { name: 'Set color for folder Layer A' });
    const activeTab = canvas.getByRole('tab', { name: 'Show Layer A' });

    await userEvent.click(trigger.parentElement!);
    expect(activeTab).toHaveAttribute('aria-selected', 'true');
    expect(activeTab).not.toHaveFocus();

    const body = within(document.body);
    const colorInput = await waitFor(() => body.getByLabelText('Choose color for folder Layer A'));
    fireEvent.change(colorInput, { target: { value: '#be185d' } });
    await userEvent.click(body.getByRole('button', { name: 'Select' }));

    await waitFor(() => {
      expect(canvas.getByRole('button', { name: 'Set color for folder Layer A' })).toHaveStyle({
        backgroundColor: '#be185d',
      });
    });

    expect(canvas.getByTestId('foldered-draggable-folder-layer-a')).toHaveAttribute(
      'data-foldered-folder-color',
      '#be185d',
    );
    expect(activeTab).toHaveAttribute('aria-selected', 'true');
  },
};

export const AcceptsOutsideDropsIntoExpandedShelf: Story = {
  render: () => <OutsideDropHarness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const shelf = canvas.getByTestId('foldered-draggable-item-shelf');

    expect(within(shelf).queryByTestId('foldered-draggable-item-layer-b-temperature')).not.toBeInTheDocument();
    expect(canvas.getByTestId('layer-b-count')).toHaveTextContent('2');

    await dragByPointer(canvas.getByTestId('outside-property-temperature'), shelf);

    await waitFor(() => {
      const addedItem = within(shelf).getByTestId('foldered-draggable-item-layer-b-temperature');
      expect(addedItem).toBeVisible();
      expect(canvas.getByTestId('layer-b-count')).toHaveTextContent('3');
    });

    const addedSwatch = canvas
      .getByTestId('foldered-draggable-item-layer-b-temperature')
      .querySelector<HTMLElement>('[data-foldered-color-swatch="true"]');
    expect(addedSwatch).not.toBeNull();
    expect(addedSwatch!).toHaveStyle({ backgroundColor: '#d97706' });
  },
};

export const CarriesStructuredDragDataAndColor: Story = {
  render: () => <DragHarness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await dragByPointer(
      canvas.getByTestId('foldered-draggable-item-layer-a-species'),
      canvas.getByTestId('outside-drop')
    );

    await waitFor(() => {
      const lastDrag = canvas.getByTestId('last-drag');
      expect(lastDrag).toHaveTextContent('"overId":"outside-drop"');
      expect(lastDrag).toHaveTextContent('"folderId":"layer-a"');
      expect(lastDrag).toHaveTextContent('"folderName":"Layer A"');
      expect(lastDrag).toHaveTextContent('"folderColor":"#2563eb"');
      expect(lastDrag).toHaveTextContent('"itemId":"layer-a-species"');
      expect(lastDrag).toHaveTextContent('"itemColor":"#16a34a"');
      expect(lastDrag).toHaveTextContent('"effectiveColor":"#16a34a"');
      expect(lastDrag).toHaveTextContent('"layerId":"layer-a"');
      expect(lastDrag).toHaveTextContent('"origin":"previous-table"');
      expect(lastDrag).toHaveTextContent('"header":"Species"');
    });
  },
};

export const CarriesFolderFallbackColor: Story = {
  render: () => <DragHarness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await dragByPointer(
      canvas.getByTestId('foldered-draggable-item-layer-a-mass'),
      canvas.getByTestId('outside-drop')
    );

    await waitFor(() => {
      const lastDrag = canvas.getByTestId('last-drag');
      expect(lastDrag).toHaveTextContent('"overId":"outside-drop"');
      expect(lastDrag).toHaveTextContent('"itemId":"layer-a-mass"');
      expect(lastDrag).toHaveTextContent('"itemColor":null');
      expect(lastDrag).toHaveTextContent('"effectiveColor":"#2563eb"');
    });
  },
};

export const ShowsDragPreviewAndRestoresUnacceptedDrag: Story = {
  render: () => <DragHarness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const shelf = canvas.getByTestId('foldered-draggable-item-shelf');
    const source = within(shelf).getByTestId('foldered-draggable-item-layer-a-mass');
    const geometry = await beginDragByPointer(source);

    await waitFor(() => {
      expect(source).not.toBeVisible();
      expect(within(document.body).getByTestId('foldered-draggable-drag-overlay')).toBeVisible();
    });

    fireEvent.pointerUp(document, {
      clientX: geometry.activationX,
      clientY: geometry.activationY,
      button: 0,
      buttons: 0,
      isPrimary: true,
      pointerId: 1,
    });

    await waitFor(() => {
      expect(within(shelf).getByTestId('foldered-draggable-item-layer-a-mass')).toBeVisible();
      expect(within(document.body).queryByTestId('foldered-draggable-drag-overlay')).not.toBeInTheDocument();
    });
  },
};

export const SecondItemDragPreviewStartsAtGrabbedItem: Story = {
  render: () => <DragHarness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const shelf = canvas.getByTestId('foldered-draggable-item-shelf');
    const firstItem = within(shelf).getByTestId('foldered-draggable-item-layer-a-mass');
    const secondItem = within(shelf).getByTestId('foldered-draggable-item-layer-a-species');
    const firstRect = firstItem.getBoundingClientRect();
    const secondRect = secondItem.getBoundingClientRect();
    const geometry = await beginDragByPointer(secondItem);

    const overlay = await waitFor(() => within(document.body).getByTestId('foldered-draggable-drag-overlay'));
    expect(overlay).toHaveTextContent('Species');

    const overlayRect = overlay.getBoundingClientRect();
    expect(overlayRect.left).toBeGreaterThan(firstRect.right);
    expect(Math.abs(overlayRect.left - (secondRect.left + (geometry.activationX - geometry.fromX)))).toBeLessThan(24);

    fireEvent.pointerUp(document, {
      clientX: geometry.activationX,
      clientY: geometry.activationY,
      button: 0,
      buttons: 0,
      isPrimary: true,
      pointerId: 1,
    });
  },
};

export const ParentControlsAcceptedDropRemoval: Story = {
  render: () => <ParentRemovalHarness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const shelf = canvas.getByTestId('foldered-draggable-item-shelf');
    const source = within(shelf).getByTestId('foldered-draggable-item-layer-a-species');
    const geometry = await beginDragByPointer(source);

    await waitFor(() => {
      expect(within(shelf).queryByTestId('foldered-draggable-item-layer-a-species')).not.toBeInTheDocument();
      expect(within(document.body).getByTestId('foldered-draggable-drag-overlay')).toBeVisible();
    });

    fireEvent.pointerUp(document, {
      clientX: geometry.activationX,
      clientY: geometry.activationY,
      button: 0,
      buttons: 0,
      isPrimary: true,
      pointerId: 1,
    });

    await waitFor(() => {
      expect(within(shelf).getByTestId('foldered-draggable-item-layer-a-species')).toBeVisible();
      expect(canvas.getByTestId('last-drop')).toHaveTextContent('rejected:layer-a-species');
    });

    await dragByPointer(
      within(shelf).getByTestId('foldered-draggable-item-layer-a-species'),
      canvas.getByTestId('outside-drop')
    );

    await waitFor(() => {
      expect(within(shelf).queryByTestId('foldered-draggable-item-layer-a-species')).not.toBeInTheDocument();
      expect(canvas.getByTestId('last-drop')).toHaveTextContent('accepted:layer-a-species');
    });
  },
};

export const DisabledItemsDoNotDrag: Story = {
  render: () => <DragHarness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await userEvent.click(canvas.getByRole('tab', { name: 'Show Layer B' }));

    const disabledItem = canvas.getByTestId('foldered-draggable-item-layer-b-archived');
    expect(disabledItem).toBeDisabled();

    await dragByPointer(disabledItem, canvas.getByTestId('outside-drop'));

    expect(canvas.getByTestId('last-drag')).toHaveTextContent('none');
  },
};
