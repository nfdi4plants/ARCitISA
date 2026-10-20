import React from 'react';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, fireEvent, screen, userEvent, waitFor, within } from 'storybook/test';
import { Main as ProvenanceGrouping } from './ProvenanceGrouping.fs.js';
import { sampleDroppedPropertyRailColor } from './Helper.fs.js';
import {
  Exports_createSampleSession as createSampleSession,
  Exports_createInputOnlySession as createInputOnlySession,
  Exports_createOutputOnlySession as createOutputOnlySession,
  Exports_createDisconnectedPropertySession as createDisconnectedPropertySession,
  Exports_createSwitchablePropertySession as createSwitchablePropertySession,
  Exports_createTypedSampleSession as createTypedSampleSession,
  Exports_createDataOutputOnlySession as createDataOutputOnlySession,
  Exports_createRetaggedTypedSampleSession as createRetaggedTypedSampleSession,
  Exports_patchLog as patchLog,
} from './Types.fs.js';

type Fixture = 'sample' | 'inputOnly' | 'outputOnly' | 'disconnectedProperty' | 'switchableProperty' | 'typedSample' | 'dataOutputOnly';

function createSessionForFixture(selected: Fixture) {
  switch (selected) {
    case 'inputOnly':
      return createInputOnlySession();
    case 'outputOnly':
      return createOutputOnlySession();
    case 'disconnectedProperty':
      return createDisconnectedPropertySession();
    case 'switchableProperty':
      return createSwitchablePropertySession();
    case 'typedSample':
      return createTypedSampleSession();
    case 'dataOutputOnly':
      return createDataOutputOnlySession();
    default:
      return createSampleSession();
  }
}

function Harness({
  inputOnly = false,
  outputOnly = false,
  fixture = 'sample',
  debug = true,
  allowTermReplacement = false,
  allowEndpointReplacement = false,
}: {
  inputOnly?: boolean;
  outputOnly?: boolean;
  fixture?: Fixture;
  debug?: boolean;
  allowTermReplacement?: boolean;
  allowEndpointReplacement?: boolean;
}) {
  const selected = inputOnly ? 'inputOnly' : outputOnly ? 'outputOnly' : fixture;
  const id = React.useId();

  return (
    <HarnessState
      key={`${selected}:${id}`}
      selected={selected}
      debug={debug}
      allowTermReplacement={allowTermReplacement}
      allowEndpointReplacement={allowEndpointReplacement}
    />
  );
}

function HarnessState({
  selected,
  debug,
  allowTermReplacement,
  allowEndpointReplacement,
}: {
  selected: Fixture;
  debug: boolean;
  allowTermReplacement: boolean;
  allowEndpointReplacement: boolean;
}) {
  const [session, setSession] = React.useState(() => createSessionForFixture(selected));

  React.useEffect(() => {
    setSession(createSessionForFixture(selected));
  }, [selected]);

  // The session's own PatchLog is the authoritative writeback record - reading
  // it directly (instead of accumulating each change's delta host-side) means
  // undo retracts already-emitted patches for free, since undo restores a
  // prior session snapshot complete with its own (shorter) PatchLog.
  const patches = Array.from(patchLog(session));

  return (
    <div className="swt:flex swt:flex-col swt:gap-4 swt:min-h-screen swt:bg-base-200 swt:p-4">
      {allowTermReplacement && (
        <button type="button" onClick={() => setSession(createRetaggedTypedSampleSession())}>
          Replace term metadata
        </button>
      )}
      {allowEndpointReplacement && (
        <button type="button" onClick={() => setSession(createOutputOnlySession())}>
          Replace endpoint context
        </button>
      )}
      <ProvenanceGrouping
        session={session}
        height={960}
        debug={debug}
        onChange={(change: any) => {
          setSession(change.Session);
        }}
      />
      <section className="swt:rounded-box swt:border swt:border-base-300 swt:bg-base-100 swt:p-4">
        <h3 className="swt:text-primary swt:font-semibold">Writeback patch preview</h3>
        <pre data-testid="provenance-patch-preview" className="swt:text-xs swt:whitespace-pre-wrap">
          {patches.length === 0 ? 'No patches emitted.' : patches.join('\n')}
        </pre>
      </section>
    </div>
  );
}

const meta = {
  title: 'Page Components/ProvenanceGrouping',
  component: ProvenanceGrouping,
  // Deactivated for now: ProvenanceGrouping is outdated.
  tags: ['autodocs', 'skip-test'],
  parameters: { layout: 'fullscreen', isolated: true },
} satisfies Meta<typeof ProvenanceGrouping>;

export default meta;
type Story = StoryObj<typeof meta>;

export const ExampleModel: Story = {
  render: () => <Harness />,
};

export const InputOnlyModel: Story = {
  render: () => <Harness inputOnly />,
};

export const OutputOnlyModel: Story = {
  render: () => <Harness outputOnly />,
};

export const GroupsByPropertiesAndShowsMembers: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await groupByProperty(canvas, 'Output', 'Species');
    const grouped = await waitFor(() => canvas.getByTestId('provenance-group-Output-output:Species=Arabidopsis'));
    expect(canvas.getByTestId('provenance-group-Output-output:Species=Chlamydomonas')).toBeInTheDocument();

    // The grouping shows as an organizer tab "Category: Value" on top of the member folder.
    const tab = within(grouped).getByTestId('provenance-group-tab-Output-output:Species=Arabidopsis-0');
    expect(tab).toHaveTextContent('Species: Arabidopsis');

    await userEvent.click(within(grouped).getByRole('button', { name: 'Show members' }));
    await waitFor(() => expect(grouped).toHaveTextContent('Output A'));
  },
};

export const ExpandedGroupsShowMemberHoverValues: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    // Single-entry cards share the folder silhouette, so they expand the same way.
    expect(within(canvas.getByText('Output A').closest('article')!).getByRole('button', { name: 'Show members' }))
      .toBeInTheDocument();

    await groupByProperty(canvas, 'Output', 'Species');
    const grouped = await waitFor(() => canvas.getByTestId('provenance-group-Output-output:Species=Arabidopsis'));

    await userEvent.click(within(grouped).getByRole('button', { name: 'Show members' }));
    const member = within(grouped).getByTestId('provenance-group-member-Output-output-a');

    expect(within(grouped).queryByTestId('provenance-member-values-Output-output-a')).not.toBeInTheDocument();
    await userEvent.hover(member);

    await waitFor(() => {
      const details = within(grouped).getByTestId('provenance-member-values-Output-output-a');
      expect(details).toHaveTextContent('Species: Arabidopsis');
      expect(details).toHaveTextContent('Analysis: Mass Spectrometry');
    });

    await userEvent.unhover(member);
  },
};

export const ShowsEntityTypesAndCollapsedSymbols: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await groupByProperty(canvas, 'Output', 'Species');
    const grouped = await waitFor(() => canvas.getByTestId('provenance-group-Output-output:Species=Arabidopsis'));

    // The collapsed card previews its member types as symbols instead of a bare "×3" count.
    expect(within(grouped).getByTestId('provenance-group-symbols-Output-output:Species=Arabidopsis'))
      .toBeInTheDocument();
    expect(grouped).not.toHaveTextContent('×3');

    // Expanding shows each member with its endpoint type ("Sample") above the name.
    await userEvent.click(within(grouped).getByRole('button', { name: 'Show members' }));
    const member = within(grouped).getByTestId('provenance-group-member-Output-output-a');
    expect(member).toHaveTextContent('Sample');
    expect(member).toHaveTextContent('Output A');
  },
};

export const HoveringGroupTabHighlightsItAndKeepsFolderPreview: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await groupByProperty(canvas, 'Output', 'Species');
    const grouped = await waitFor(() => canvas.getByTestId('provenance-group-Output-output:Species=Arabidopsis'));
    const tab = within(grouped).getByTestId('provenance-group-tab-Output-output:Species=Arabidopsis-0');

    expect(tab).toHaveAttribute('data-hovered', 'false');

    // Hovering the tab highlights it and the folder previews that tab's members.
    await userEvent.hover(tab);
    await waitFor(() => expect(tab).toHaveAttribute('data-hovered', 'true'));
    expect(within(grouped).getByTestId('provenance-group-symbols-Output-output:Species=Arabidopsis'))
      .toBeInTheDocument();

    await userEvent.unhover(tab);
    await waitFor(() => expect(tab).toHaveAttribute('data-hovered', 'false'));
  },
};

export const ShowsFileTypeForDataEndpoints: Story = {
  render: () => <Harness fixture="dataOutputOnly" />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    // A Data endpoint shows its type as a document symbol in the folder body;
    // the "File" type line appears on the expanded member row.
    const card = await waitFor(() => canvas.getByText('Data Output A').closest('article')!);
    expect(card.querySelector('[class*="fluent--document"]')).toBeInTheDocument();

    await userEvent.click(within(card).getByRole('button', { name: 'Show members' }));
    await waitFor(() => expect(card).toHaveTextContent('File'));
  },
};

export const GroupCardsSelectWithCheckboxAndExpandFromSurface: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const outputA = canvas.getByText('Output A').closest('article')!;

    // Selection is an explicit checkbox; a selection bar with a clear action
    // appears while any group is selected.
    await userEvent.click(within(outputA).getByRole('checkbox'));
    await waitFor(() => expect(outputA).toHaveClass('swt:border-primary'));
    expect(canvas.getByTestId('provenance-selection-bar')).toHaveTextContent('1 group selected');

    await userEvent.click(canvas.getByTestId('provenance-clear-selection'));
    await waitFor(() => {
      expect(outputA).not.toHaveClass('swt:border-primary');
      expect(canvas.queryByTestId('provenance-selection-bar')).not.toBeInTheDocument();
    });

    // Clicking the card body expands the members instead of selecting.
    const expandSurface = outputA.querySelector<HTMLElement>('[data-testid^="provenance-group-expand-surface-"]')!;
    await userEvent.click(expandSurface);
    await waitFor(() =>
      expect(within(outputA).getByTestId('provenance-group-member-Output-output-a')).toBeInTheDocument(),
    );
    expect(outputA).not.toHaveClass('swt:border-primary');
  },
};

export const GroupsBothSidesFromOutputProperty: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    for (
      let attempt = 0;
      attempt < 3 && !canvas.queryByTestId('provenance-group-Input-input:Replicate=1 | 2');
      attempt += 1
    ) {
      await showPropertyControls(canvas, 'Output', 'Replicate');
      fireEvent.click(canvas.getByTestId('provenance-property-both-Output-Replicate'));
      await waitFor(() => expect(canvas.queryByTestId('provenance-group-Input-input:Replicate=1 | 2')).toBeInTheDocument(), {
        timeout: 1000,
      }).catch(() => undefined);
    }

    await waitFor(() => {
      expect(canvas.getByTestId('provenance-group-Input-input:Replicate=1 | 2')).toBeInTheDocument();
      expect(canvas.getByTestId('provenance-group-Output-output:Replicate=1 | 2')).toBeInTheDocument();
      expect(canvas.queryByTestId('provenance-group-Input-input:Replicate=1')).not.toBeInTheDocument();
      expect(canvas.queryByTestId('provenance-group-Output-output:Replicate=2')).not.toBeInTheDocument();
    }, { timeout: 6000 });
  },
};

export const MissingSecondGroupingKeyKeepsAvailableGroupingKeys: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await groupByProperty(canvas, 'Input', 'Species');
    await groupByProperty(canvas, 'Input', 'Temperature');

    await waitFor(() => {
      expect(canvas.getByTestId('provenance-group-Input-input:Species=Arabidopsis|Temperature=12 C'))
        .toBeInTheDocument();
      expect(canvas.getByTestId('provenance-group-Input-input:Species=Arabidopsis|Temperature=24 C'))
        .toBeInTheDocument();
      expect(canvas.getByTestId('provenance-group-Input-input:Species=Chlamydomonas')).toBeInTheDocument();
      expect(canvas.queryByTestId('provenance-group-Input-input:input-d')).not.toBeInTheDocument();
    });
  },
};

export const ConnectedOutputsKeepPropertiesInRailsAndConnections: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const outputA = canvas.getByText('Output A').closest('article')!;

    expect(outputA).toHaveTextContent('Output A');
    expect(outputA).not.toHaveTextContent('Analysis: Mass Spectrometry');
    expect(outputA).not.toHaveTextContent('Species: Arabidopsis');
    expect(outputA).not.toHaveTextContent('Temperature: 12 C');
    expect(canvas.getAllByTestId('provenance-connection').length).toBeGreaterThan(0);
  },
};

export const PropertiesStartInOriginFoldersAndSideDropZonesAreEmpty: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    expect(canvas.getByTestId('foldered-draggable-list')).toBeInTheDocument();
    expect(canvas.getByTestId('foldered-draggable-folder-source-fixture-assay-table')).toBeInTheDocument();
    expect(canvas.getByTestId('provenance-property-rail-Input').querySelector('[data-testid^="provenance-property-Input-"]'))
      .not.toBeInTheDocument();
    expect(canvas.getByTestId('provenance-property-rail-Output').querySelector('[data-testid^="provenance-property-Output-"]'))
      .not.toBeInTheDocument();
    // waitFor: the shelf pops in with a brief opacity animation on mount, and
    // toBeVisible treats the first opacity-0 frame as hidden.
    await waitFor(() =>
      expect(within(canvas.getByTestId('foldered-draggable-item-row')).getByRole('button', { name: /^Drag Species$/ }))
        .toBeVisible());

    await userEvent.click(canvas.getByRole('button', { name: 'Minimize annotation folders' }));
    await waitFor(() => expect(canvas.queryByTestId('foldered-draggable-list')).not.toBeInTheDocument());

    await userEvent.click(canvas.getByRole('button', { name: 'Expand annotation folders' }));
    await waitFor(() => expect(canvas.getByTestId('foldered-draggable-list')).toBeInTheDocument());
    await waitFor(() =>
      expect(within(canvas.getByTestId('foldered-draggable-item-row')).getByRole('button', { name: /^Drag Species$/ }))
        .toBeVisible());

    const species = await shelfProperty(canvas, 'Species');
    expect(species).toBeInTheDocument();
    expect(species).toHaveTextContent('Species');
  },
};

export const DroppedShelfPropertyLeavesFolders: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await ensurePropertyInRail(canvas, 'Output', 'Species');

    expect(canvas.getByTestId('provenance-property-Output-Species')).toBeInTheDocument();
    const currentLayerShelf = await openShelfFolder(
      canvas,
      canvas.getByTestId('foldered-draggable-folder-source-fixture-assay-table'),
    );
    expect(currentLayerShelf.queryByRole('button', { name: /^Drag Species$/ })).not.toBeInTheDocument();
  },
};

export const DroppedShelfPropertyKeepsLayerColorAndSyncsUpdates: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const initialLayerColor = canvas.getByTestId('provenance-layer-layer-1').getAttribute('data-provenance-layer-color') ?? '';

    const property = await ensurePropertyInRail(canvas, 'Output', 'Species');
    expect(propertyColorSwatch(property)).toHaveStyle({ backgroundColor: initialLayerColor });

    expect(sampleDroppedPropertyRailColor('Output', 'Species', '#dc2626')).toBe('#dc2626');
  },
};

export const FolderColorPreviewSyncsLayerTabAndRailProperty: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await setFolderPreviewColor(canvas, canvas.getByTestId('foldered-draggable-folder-source-fixture-assay-table'), '#be185d');

    await waitFor(() => {
      expect(canvas.getByTestId('provenance-layer-layer-1')).toHaveAttribute(
        'data-provenance-layer-color',
        '#be185d',
      );
    });

    const property = await ensurePropertyInRail(canvas, 'Output', 'Species');
    expect(propertyColorSwatch(property)).toHaveStyle({ backgroundColor: '#be185d' });
  },
};

export const NonLayerFolderColorAppliesToShelfAndRailProperties: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const previousContextFolder = canvas.getByTestId(
      'foldered-draggable-folder-source-fixture-previous-study-table',
    );

    await setFolderPreviewColor(canvas, previousContextFolder, '#0891b2');

    const previousContextShelf = await openShelfFolder(canvas, previousContextFolder);
    const shelfPropertyButton = previousContextShelf.getByRole('button', { name: /^Drag Previous Treatment$/ });
    const shelfSwatch = shelfPropertyButton.querySelector<HTMLElement>('[data-foldered-color-swatch="true"]');

    expect(shelfSwatch).not.toBeNull();
    expect(shelfSwatch!).toHaveStyle({ backgroundColor: '#0891b2' });

    const property = await ensurePropertyInRail(canvas, 'Input', 'Previous Treatment');
    expect(propertyColorSwatch(property)).toHaveStyle({ backgroundColor: '#0891b2' });
  },
};

export const RejectedShelfPropertyDropRestoresFolderItem: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const source = await shelfProperty(canvas, 'Species');
    const target = canvas.getByText('Input A').closest('article')!;

    await dragByPointer(source, target);

    await waitFor(() => {
      expect(canvas.queryByTestId('foldered-draggable-drag-overlay')).not.toBeInTheDocument();
      expect(within(canvas.getByTestId('foldered-draggable-item-row')).getByRole('button', { name: /^Drag Species$/ }))
        .toBeVisible();
    });
  },
};

export const SingleSidedShelfPropertiesCannotDropOnOppositeSide: Story = {
  render: () => <Harness fixture="disconnectedProperty" />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const source = await shelfProperty(canvas, 'Analysis');
    const inputRail = canvas.getByTestId('provenance-property-rail-Input');

    const pointer = await startDragByPointer(source);
    await moveDragPointerTo(inputRail, pointer.pointerId);
    await waitFor(() => {
      expect(inputRail).toHaveAttribute('data-provenance-drop-state', 'rejecting');
      expect(inputRail).toHaveClass('swt:border-warning');
    });

    fireEvent.pointerUp(inputRail, {
      clientX: pointer.x,
      clientY: pointer.y,
      button: 0,
      buttons: 0,
      isPrimary: true,
      pointerId: pointer.pointerId,
    });
    await waitFor(() => expect(canvas.queryByTestId('foldered-draggable-drag-overlay')).not.toBeInTheDocument());

    expect(canvas.queryByTestId('provenance-property-Input-Analysis')).not.toBeInTheDocument();
    await waitFor(() => {
      expect(within(canvas.getByTestId('foldered-draggable-item-row')).getByRole('button', { name: /^Drag Analysis$/ }))
        .toBeVisible();
    });
  },
};

export const HelpLegendExplainsWorkflowAndSymbols: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await userEvent.click(canvas.getByTestId('provenance-help-trigger'));
    const content = await waitFor(() => within(document.body).getByTestId('provenance-help-content'));

    expect(content).toHaveTextContent('Group');
    expect(content).toHaveTextContent('Annotate');
    expect(content).toHaveTextContent('Connect');
    expect(content).toHaveTextContent('Continue');
    expect(content).toHaveTextContent(/upstream table/i);
    await userEvent.keyboard('{Escape}');
  },
};

export const ToolbarUsesSinglePropertySortAndOriginButtons: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const toolbar = within(canvas.getByTestId('provenance-filter-toolbar'));

    expect(toolbar.getByPlaceholderText('Search annotations & values...')).toBeInTheDocument();

    await userEvent.click(toolbar.getByRole('button', { name: /^Sort By$/i }));
    expect(toolbar.getByRole('button', { name: /^Annotation Value Count$/i })).toBeInTheDocument();
    expect(toolbar.getByRole('button', { name: /^Name$/i })).toBeInTheDocument();
    expect(toolbar.getAllByRole('button', { name: /^Connection Count$/i })).toHaveLength(1);

    expect(toolbar.getByRole('button', { name: /^Show upstream annotations$/i }).querySelector('[class*="fluent--arrow-up-20"]'))
      .toBeInTheDocument();
    expect(toolbar.getByRole('button', { name: /^Show current annotations$/i }).querySelector('[class*="fluent--circle-20-filled"]'))
      .toBeInTheDocument();
    const both = toolbar.getByRole('button', { name: /^Show current and upstream annotations$/i });
    expect(both.querySelector('[class*="fluent--arrow-up-20"]')).toBeInTheDocument();
    expect(both.querySelector('[class*="fluent--circle-20-filled"]')).toBeInTheDocument();
  },
};

export const TopControlsShareOneRowWhenSpaceAllows: Story = {
  // The controls row wraps by design (flex-wrap) once it runs out of width, so
  // this story must guarantee the ample width its name promises. At the default
  // 1280px browser viewport the row sits right at the edge - it fits under one
  // platform's font metrics and wraps under another's (Windows passes, Linux CI
  // wraps by a row), which is what made this test flaky. A fixed wide wrapper
  // pins the layout well clear of that edge so the single-row assertion is
  // deterministic regardless of the runner's font rendering.
  render: () => (
    <div style={{ width: 1600 }}>
      <Harness />
    </div>
  ),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const topControls = canvas.getByTestId('provenance-top-controls');
    const toolbar = canvas.getByTestId('provenance-filter-toolbar');
    const search = canvas.getByTestId('provenance-search');
    const viewActions = canvas.getByTestId('provenance-view-actions');
    const valueFilter = canvas.getByRole('combobox', { name: 'Filter by annotation value count' });
    const originFilter = canvas.getByRole('button', { name: /^Show upstream annotations$/i });

    const rowTop = (element: HTMLElement) => Math.round(element.getBoundingClientRect().top);
    const rowCenter = (element: HTMLElement) => {
      const rect = element.getBoundingClientRect();
      return rect.top + rect.height / 2;
    };

    expect(topControls).toContainElement(toolbar);
    expect(topControls).toContainElement(viewActions);
    expect(rowTop(toolbar)).toBe(rowTop(search));
    expect(rowTop(search)).toBe(rowTop(valueFilter));
    expect(rowTop(search)).toBe(rowTop(originFilter));
    // The view actions are deliberately smaller (btn-xs) than the toolbar
    // controls, so on the shared items-center row their tops differ while the
    // vertical centers align; a wrap onto a second row would offset the
    // center by a full row height.
    expect(Math.abs(rowCenter(toolbar) - rowCenter(viewActions))).toBeLessThanOrEqual(1);
  },
};

export const SearchInputUpdatesImmediatelyButFiltersAfterDebounce: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const toolbar = within(canvas.getByTestId('provenance-filter-toolbar'));
    const search = toolbar.getByPlaceholderText('Search annotations & values...') as HTMLInputElement;

    await ensurePropertyInRail(canvas, 'Output', 'Species');
    await ensurePropertyInRail(canvas, 'Output', 'Analysis');

    const outputRail = within(canvas.getByTestId('provenance-property-rail-Output'));

    expect(outputRail.getByTestId('provenance-property-Output-Species')).toBeInTheDocument();
    expect(outputRail.getByTestId('provenance-property-Output-Analysis')).toBeInTheDocument();

    await userEvent.type(search, 'mass');

    expect(search).toHaveValue('mass');
    expect(outputRail.getByTestId('provenance-property-Output-Species')).toBeInTheDocument();

    await waitFor(() => {
      expect(outputRail.queryByTestId('provenance-property-Output-Species')).not.toBeInTheDocument();
      expect(outputRail.getByTestId('provenance-property-Output-Analysis')).toBeInTheDocument();
    }, { timeout: 1200 });
  },
};

export const SortsPropertiesByNameAndConnectionCount: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const toolbar = within(canvas.getByTestId('provenance-filter-toolbar'));

    await userEvent.click(toolbar.getByRole('button', { name: /^Sort By$/i }));
    await userEvent.click(toolbar.getByRole('button', { name: /^Name$/i }));

    await waitFor(async () => {
      expect((await shelfPropertyOrder(canvas)).slice(0, 4)).toEqual([
        'Analysis',
        'Replicate',
        'Species',
        'Temperature',
      ]);
    });

    await userEvent.click(toolbar.getByRole('button', { name: /^Sort By$/i }));
    await userEvent.click(toolbar.getByRole('button', { name: /^Connection Count$/i }));

    await waitFor(async () => {
      expect((await shelfPropertyOrder(canvas)).slice(0, 4)).toEqual([
        'Species',
        'Analysis',
        'Temperature',
        'Replicate',
      ]);
    });
  },
};

export const SortsGroupsByMemberCount: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await groupByProperty(canvas, 'Output', 'Species');
    await waitFor(() =>
      expect(canvas.getByTestId('provenance-group-Output-output:Species=Arabidopsis')).toBeInTheDocument(),
    );

    const toolbar = within(canvas.getByTestId('provenance-filter-toolbar'));
    await userEvent.click(toolbar.getByRole('button', { name: /^Sort Groups$/i }));
    await userEvent.click(toolbar.getByRole('button', { name: /^Member Count$/i }));

    await waitFor(() => {
      const cards = Array.from(
        canvasElement.querySelectorAll<HTMLElement>('[data-testid^="provenance-group-Output-"]'),
      );
      expect(cards[0].getAttribute('data-testid')).toBe('provenance-group-Output-output:Species=Arabidopsis');
    });
  },
};

export const AddedRailPropertiesAreCurrentAndPinnedToTheirSide: Story = {
  render: () => <Harness inputOnly />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const inputRail = within(canvas.getByTestId('provenance-property-rail-Input'));
    const outputRail = within(canvas.getByTestId('provenance-property-rail-Output'));

    const source = await addRailProperty(canvas, 'Input', 'Treatment', 'Drought');
    expect(inputRail.getByTestId('provenance-property-Input-Treatment')).toBeInTheDocument();
    expect(within(inputRail.getByTestId('provenance-property-Input-Treatment')).getByTitle('Current')).toBeInTheDocument();
    expect(outputRail.queryByTestId('provenance-property-Output-Treatment')).not.toBeInTheDocument();

    await userEvent.click(within(canvas.getByTestId('provenance-filter-toolbar')).getByRole('button', { name: /^Show current annotations$/i }));
    await waitFor(() => expect(inputRail.getByTestId('provenance-property-Input-Treatment')).toBeInTheDocument());
    expect(outputRail.queryByTestId('provenance-property-Output-Treatment')).not.toBeInTheDocument();

    const target = canvas.getByText('Input Only A').closest('article')!;
    await dragByPointer(source, target);

    await waitFor(() =>
      expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('AddLoadedPropertyValue'),
    );
  },
};

export const LayerFocusDoesNotResortInitializedRails: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const initialOutputOrder = (await shelfPropertyOrder(canvas)).slice(0, 4);

    await selectGroup(canvas.getByText('Output A').closest('article')!);
    await createLayer(canvas, 'Layer 2');
    await waitFor(() => expect(canvas.getByTestId('provenance-layer-layer-2')).toHaveClass('swt:btn-primary'));

    const toolbar = within(canvas.getByTestId('provenance-filter-toolbar'));
    await userEvent.click(toolbar.getByRole('button', { name: /^Sort By$/i }));
    await userEvent.click(toolbar.getByRole('button', { name: /^Connection Count$/i }));

    await userEvent.click(canvas.getByTestId('provenance-layer-layer-1'));
    await waitFor(() => expect(canvas.getByTestId('provenance-layer-layer-1')).toHaveClass('swt:btn-primary'));
    expect((await shelfPropertyOrder(canvas)).slice(0, 4)).toEqual(initialOutputOrder);
  },
};

export const PropertyRailExpandsValuesAndAddControls: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const outputRail = within(canvas.getByTestId('provenance-property-rail-Output'));

    expect(outputRail.queryByText('Arabidopsis')).not.toBeInTheDocument();
    expect(outputRail.getByText('Add annotation')).toBeInTheDocument();

    const panel = await expandProperty(canvas, 'Output', 'Species');
    const arabidopsis = panel.getByText('Arabidopsis').closest('button, div')!;
    expect(arabidopsis).toBeInTheDocument();
    expect(arabidopsis).toHaveClass('swt:btn');
    // Outline, not primary: value chips share the ungrouped header button look so
    // they stay distinguishable from their header, which turns primary when grouped.
    expect(arabidopsis).toHaveClass('swt:btn-outline');
    expect(arabidopsis).toHaveClass('swt:w-fit');
    expect(arabidopsis).toHaveClass('swt:cursor-grab');
    expect(arabidopsis.querySelector('[class*="re-order-dots"]')).not.toBeInTheDocument();
    expect(panel.getByText('Chlamydomonas')).toBeInTheDocument();
    const addValue = panel.getByText('Add value').closest('button')!;
    expect(addValue).toHaveClass('swt:btn');
    expect(addValue).toHaveClass('swt:btn-outline');
    expect(addValue).toHaveClass('swt:w-fit');
    expect(addValue.querySelector('[class*="fluent--add-20-regular"]')).toBeInTheDocument();
  },
};

export const ExpandsPropertyValuesWithoutGrouping: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await expandProperty(canvas, 'Output', 'Species');

    expect(canvas.getByTestId('provenance-group-Output-output:output-a')).toBeInTheDocument();
    expect(canvas.queryByTestId('provenance-group-Output-output:Species=Arabidopsis')).not.toBeInTheDocument();
  },
};

export const RailValueShowsDragIndicatorWhileDragging: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const source = await railValue(canvas, 'Output', 'Analysis', 'Mass Spectrometry');

    const pointer = await startDragByPointer(source);

    await waitFor(() => expect(source).toHaveClass('swt:ring-2'));
    await waitFor(() => expect(screen.getByTestId('provenance-drag-overlay-value')).toHaveTextContent('Mass Spectrometry'));
    fireEvent.pointerUp(document, {
      clientX: source.getBoundingClientRect().left + 12,
      clientY: source.getBoundingClientRect().top + 12,
      button: 0,
      buttons: 0,
      isPrimary: true,
      pointerId: pointer.pointerId,
    });
  },
};

export const SingleSidedPropertiesCannotSwitchSides: Story = {
  render: () => <Harness fixture="disconnectedProperty" />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await ensurePropertyInRail(canvas, 'Output', 'Analysis');
    await ensurePropertyInRail(canvas, 'Output', 'Replicate');
    expect(canvas.getByTestId('provenance-property-drag-Output-Analysis')).toBeDisabled();
    expect(canvas.getByTestId('provenance-property-drag-Output-Replicate')).toBeDisabled();
  },
};

export const SwitchesPropertyGroupingSideByDrag: Story = {
  render: () => <Harness fixture="switchableProperty" />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await groupByProperty(canvas, 'Output', 'Batch');
    expect(canvas.queryByTestId('provenance-group-Input-input:Batch=A')).not.toBeInTheDocument();

    await waitFor(() => {
      expect(canvas.getByTestId('provenance-property-Output-Batch')).toBeInTheDocument();
    }, { timeout: 10_000 });

    await dragByPointer(
      canvas.getByTestId('provenance-property-Output-Batch'),
      canvas.getByTestId('provenance-property-rail-Input'),
    );

    await waitFor(() => {
      expect(canvas.queryByTestId('provenance-group-Output-output:Batch=A')).not.toBeInTheDocument();
      expect(canvas.getByTestId('provenance-group-Output-output:output-a')).toBeInTheDocument();
      expect(canvas.getByTestId('provenance-group-Input-input:Batch=A')).toBeInTheDocument();
      expect(canvas.getByTestId('provenance-group-Input-input:Batch=B')).toBeInTheDocument();
      expect(canvas.queryByTestId('provenance-group-Input-input:input-b')).not.toBeInTheDocument();
    });
  },
};

export const SwitchesInheritedPropertyToInputSideWithoutGrouping: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const inputRail = within(canvas.getByTestId('provenance-property-rail-Input'));
    const outputRail = within(canvas.getByTestId('provenance-property-rail-Output'));

    await ensurePropertyInRail(canvas, 'Output', 'Species');
    // This is the switch button between the two sides, which is only enabled for properties that are allowed to be dragged to the other side.
    expect(canvas.getByTestId('provenance-property-drag-Output-Species')).not.toBeDisabled();
    await waitFor(() => {
      expect(canvas.getByTestId('provenance-property-Output-Species')).toBeInTheDocument();
    }, { timeout: 10_000 });

    await dragByPointer(
      canvas.getByTestId('provenance-property-Output-Species'),
      canvas.getByTestId('provenance-property-rail-Input'),
    );

    await waitFor(() => {
      expect(inputRail.getByTestId('provenance-property-Input-Species')).toBeInTheDocument();
      expect(outputRail.queryByTestId('provenance-property-Output-Species')).not.toBeInTheDocument();
    }, { timeout: 10_000 });

    // Switching an ungrouped property only moves it; it must not group either side.
    expect(canvas.queryByTestId('provenance-group-Input-input:Species=Arabidopsis')).not.toBeInTheDocument();
    expect(canvas.queryByTestId('provenance-group-Output-output:Species=Arabidopsis')).not.toBeInTheDocument();
    expect(canvas.getByTestId('provenance-group-Input-input:input-d')).toBeInTheDocument();
  },
};

export const ClicksSwapHandleToSwitchSideWithoutGrouping: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const inputRail = within(canvas.getByTestId('provenance-property-rail-Input'));
    const outputRail = within(canvas.getByTestId('provenance-property-rail-Output'));

    await ensurePropertyInRail(canvas, 'Output', 'Species');
    await userEvent.hover(canvas.getByTestId('provenance-property-Output-Species'));
    await userEvent.click(canvas.getByTestId('provenance-property-drag-Output-Species'));

    await waitFor(() => {
      expect(inputRail.getByTestId('provenance-property-Input-Species')).toBeInTheDocument();
      expect(outputRail.queryByTestId('provenance-property-Output-Species')).not.toBeInTheDocument();
    });

    // Switching an ungrouped property only moves it; it must not group either side.
    expect(canvas.queryByTestId('provenance-group-Input-input:Species=Arabidopsis')).not.toBeInTheDocument();
    expect(canvas.queryByTestId('provenance-group-Output-output:Species=Arabidopsis')).not.toBeInTheDocument();
  },
};

export const RegroupedValuesAreReadOnlyOnCards: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await groupByProperty(canvas, 'Output', 'Species');
    const grouped = await waitFor(
      () => canvas.getByTestId('provenance-group-Output-output:Species=Chlamydomonas'),
      { timeout: 3000 },
    );

    const species = within(grouped).queryByTestId('provenance-value-pv-input-d-species');
    expect(species).not.toBeInTheDocument();
  },
};

export const RendersMeasuredConnections: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await waitFor(() => {
      const connector = canvas.getAllByTestId('provenance-connection')[0];
      expect(connector.getAttribute('d')).toMatch(/^M\s+\d/);
      expect(connector.getAttribute('d')).not.toContain('M 0 32');
    });
  },
};

export const ConnectorOverlayDoesNotMeasureConnectionNodesWhileIdle: Story = {
  render: () => {
    activeMeasurementCounter?.restore();
    activeMeasurementCounter = installConnectionNodeMeasurementCounter();
    return <Harness />;
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    try {
      await waitFor(() => expect(canvas.getAllByTestId('provenance-connection').length).toBeGreaterThan(0));
      await waitForStableConnectionMeasurements();

      const baseline = activeMeasurementCounter!.count();

      await waitForMilliseconds(180);

      expect(activeMeasurementCounter!.count()).toBe(baseline);
    } finally {
      activeMeasurementCounter?.restore();
    }
  },
};

export const RemeasuresConnectionsAfterGroupExpansion: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await groupByProperty(canvas, 'Output', 'Species');

    const grouped = await waitFor(() => canvas.getByTestId('provenance-group-Output-output:Species=Arabidopsis'));

    const before = await waitFor(() => {
      const paths = canvas.getAllByTestId('provenance-connection').map((connector) => connector.getAttribute('d'));
      expect(paths.length).toBeGreaterThan(0);
      return paths;
    });

    await userEvent.click(within(grouped).getByRole('button', { name: 'Show members' }));

    await waitFor(() => {
      const after = canvas.getAllByTestId('provenance-connection').map((connector) => connector.getAttribute('d'));
      expect(after).not.toEqual(before);
    });
  },
};

export const ConnectorPathsUpdateAfterPanelResize: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const surface = canvas.getByTestId('provenance-surface');

    await waitFor(() => expect(canvas.getAllByTestId('provenance-connection').length).toBeGreaterThan(0));

    const path = firstMeasuredConnectorPath(canvasElement);
    const before = path.getAttribute('d');
    expect(before).not.toBeNull();

    const splitter = canvas.getByTestId('provenance-left-splitter');
    const surfaceRect = surface.getBoundingClientRect();
    const splitterRect = splitter.getBoundingClientRect();
    const pointerId = 41;

    fireEvent.pointerDown(splitter, {
      clientX: splitterRect.left + 2,
      clientY: splitterRect.top + 8,
      button: 0,
      buttons: 1,
      isPrimary: true,
      pointerId,
    });

    fireEvent.pointerMove(document, {
      clientX: surfaceRect.left + surfaceRect.width * 0.36,
      clientY: splitterRect.top + 8,
      button: 0,
      buttons: 1,
      isPrimary: true,
      pointerId,
    });

    fireEvent.pointerUp(document, {
      button: 0,
      buttons: 0,
      isPrimary: true,
      pointerId,
    });

    await waitFor(() => expect(firstMeasuredConnectorPath(canvasElement).getAttribute('d')).not.toBe(before), {
      timeout: 1200,
    });
  },
};

export const RendersConnectionsForQuotedGroupingValues: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const source = await addRailValue(canvas, 'Output', 'Analysis', "Farmer's field");
    await groupByProperty(canvas, 'Output', 'Analysis');
    const outputD = canvas.getByText('Output D').closest('article')!;

    await dragByPointer(source, outputD);

    await waitFor(() => {
      const connectors = canvas.getAllByTestId('provenance-connection');
      expect(connectors).toHaveLength(4);
      expect(connectors.every((connector) => connector.getAttribute('d')?.startsWith('M '))).toBe(true);
    });
  },
};

export const ShowsLiveConnectionPreviewWhileDraggingHandle: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const input = canvas.getByText('Input C').closest('article')!;
    const handle = within(input).getByTestId('provenance-connection-handle-Input-GroupCard');

    const pointer = await startDragByPointer(handle);

    await waitFor(() => {
      const preview = canvas.getByTestId('provenance-live-connection');
      expect(preview.getAttribute('d')).toMatch(/^M\s+\d/);
    });

    fireEvent.pointerUp(document, { button: 0, buttons: 0, isPrimary: true, pointerId: pointer.pointerId });
    await waitFor(() => expect(canvas.queryByTestId('provenance-live-connection')).not.toBeInTheDocument());
  },
};

export const ExpandedGroupsRenderMemberLevelConnections: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await groupByProperty(canvas, 'Output', 'Species');

    const grouped = await waitFor(() => canvas.getByTestId('provenance-group-Output-output:Species=Arabidopsis'));

    await userEvent.click(within(grouped).getByRole('button', { name: 'Show members' }));

    await waitFor(() => {
      const paths = canvas.getAllByTestId('provenance-member-connection');
      expect(paths.length).toBeGreaterThan(0);
      expect(paths.every((path) => path.getAttribute('d')?.startsWith('M '))).toBe(true);
    });
  },
};

export const ExpandedGroupsHideGroupConnectionAnchors: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await groupByProperty(canvas, 'Output', 'Species');

    const groupId = 'output:Species=Arabidopsis';
    const grouped = await waitFor(() => canvas.getByTestId(`provenance-group-Output-${groupId}`));
    expect(within(grouped).getByTestId('provenance-connection-handle-Output-GroupCard')).toBeInTheDocument();
    expect(connectionKeys(canvas.getAllByTestId('provenance-connection')).some((key) => key.includes(groupId))).toBe(true);

    await userEvent.click(within(grouped).getByRole('button', { name: 'Show members' }));

    await waitFor(() => {
      const expanded = canvas.getByTestId(`provenance-group-Output-${groupId}`);
      expect(within(expanded).queryByTestId('provenance-connection-handle-Output-GroupCard')).not.toBeInTheDocument();
      expect(within(expanded).getAllByTestId('provenance-connection-handle-Output-GroupMember').length).toBeGreaterThan(0);
      expect(connectionKeys(canvas.queryAllByTestId('provenance-connection')).some((key) => key.includes(groupId))).toBe(false);
      expect(connectionKeys(canvas.getAllByTestId('provenance-member-connection')).some((key) => key.includes(groupId))).toBe(true);
    });
  },
};

const connectionKeys = (paths: HTMLElement[]) =>
  paths.map((path) => path.getAttribute('data-provenance-connection-key') ?? '');

export const ExpandedPropertyValuesConnectValueChipsToMatchingGroups: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await ensurePropertyInRail(canvas, 'Output', 'Species');
    await waitFor(() => {
      const headerKeys = connectionKeys(canvas.getAllByTestId('provenance-property-connection'));
      expect(headerKeys.some((key) => key.includes('Species'))).toBe(true);
    });

    const panel = await expandProperty(canvas, 'Output', 'Species');

    await waitFor(() => {
      const headerKeys = connectionKeys(canvas.queryAllByTestId('provenance-property-connection'));
      expect(headerKeys.some((key) => key.includes('Species'))).toBe(false);

      const valueKeys = connectionKeys(canvas.getAllByTestId('provenance-value-connection'));
      expect(valueKeys.some((key) => key.includes('Species') && key.includes('Arabidopsis'))).toBe(true);
      expect(valueKeys.some((key) => key.includes('Species') && key.includes('Chlamydomonas'))).toBe(true);
      expect(canvas.getAllByTestId('provenance-value-connection').every((path) => path.getAttribute('d')?.startsWith('M '))).toBe(true);
    });

    expect(panel.queryByTestId('provenance-connection-handle-Output-PropertyValue')).not.toBeInTheDocument();

    // Collapsing again must restore header connectors and drop value connectors,
    // so the expanded-header filter cannot become a one-way switch.
    await userEvent.hover(canvas.getByTestId('provenance-property-Output-Species'));
    await userEvent.click(canvas.getByTestId('provenance-property-expand-Output-Species'));

    await waitFor(() => {
      expect(canvas.queryByTestId('provenance-property-values-Output-Species')).not.toBeInTheDocument();
      const headerKeys = connectionKeys(canvas.getAllByTestId('provenance-property-connection'));
      expect(headerKeys.some((key) => key.includes('Species'))).toBe(true);
      expect(canvas.queryByTestId('provenance-value-connection')).not.toBeInTheDocument();
    });
  },
};

export const ExpandedGroupPropertyConnectorsTargetMatchingMembers: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const groupId = 'output:Species=Arabidopsis';

    await groupByProperty(canvas, 'Output', 'Species');

    const grouped = await waitFor(() => canvas.getByTestId(`provenance-group-Output-${groupId}`));
    await userEvent.click(within(grouped).getByRole('button', { name: 'Show members' }));

    await waitFor(() => {
      const speciesKeys = connectionKeys(canvas.getAllByTestId('provenance-property-connection'))
        .filter((key) => key.includes('Species') && key.includes('Arabidopsis'));

      expect(speciesKeys.some((key) => key.includes('output-a'))).toBe(true);
      expect(speciesKeys.some((key) => key.includes('output-b'))).toBe(true);
      expect(speciesKeys.some((key) => key.includes('output-c'))).toBe(true);
      expect(speciesKeys.some((key) => key.endsWith(`:${groupId}`))).toBe(false);
    });

    await expandProperty(canvas, 'Output', 'Species');

    await waitFor(() => {
      const arabidopsisKeys = connectionKeys(canvas.getAllByTestId('provenance-value-connection'))
        .filter((key) => key.includes('Species') && key.includes('Arabidopsis'));

      expect(arabidopsisKeys.some((key) => key.includes('output-a'))).toBe(true);
      expect(arabidopsisKeys.some((key) => key.includes('output-b'))).toBe(true);
      expect(arabidopsisKeys.some((key) => key.includes('output-c'))).toBe(true);
      expect(arabidopsisKeys.some((key) => key.endsWith(`:${groupId}`))).toBe(false);
    });
  },
};

export const ConnectedExpandedGroupPropertyConnectorsTargetMatchingMembers: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const inputGroupId = 'input:Replicate=1 | 2';
    const outputGroupId = 'output:Replicate=1 | 2';

    for (let attempt = 0; attempt < 3 && !canvas.queryByTestId(`provenance-group-Input-${inputGroupId}`); attempt += 1) {
      await showPropertyControls(canvas, 'Output', 'Replicate');
      fireEvent.click(canvas.getByTestId('provenance-property-both-Output-Replicate'));
      await waitFor(() => expect(canvas.queryByTestId(`provenance-group-Input-${inputGroupId}`)).toBeInTheDocument(), {
        timeout: 1000,
      }).catch(() => undefined);
    }

    await waitFor(() => {
      expect(canvas.getByTestId(`provenance-group-Input-${inputGroupId}`)).toBeInTheDocument();
      expect(canvas.getByTestId(`provenance-group-Output-${outputGroupId}`)).toBeInTheDocument();
    }, { timeout: 6000 });

    const inputGroup = await waitFor(() => canvas.getByTestId(`provenance-group-Input-${inputGroupId}`));

    await userEvent.click(within(inputGroup).getByRole('button', { name: 'Show members' }));

    await waitFor(() => {
      const outputGroup = canvas.getByTestId(`provenance-group-Output-${outputGroupId}`);
      expect(within(outputGroup).getByTestId('provenance-group-member-Output-output-b')).toBeInTheDocument();
    });

    await waitFor(() => {
      const replicateKeys = connectionKeys(canvas.getAllByTestId('provenance-property-connection'))
        .filter((key) => key.includes('Output') && key.includes('Replicate') && key.includes('1 | 2'));

      expect(replicateKeys.some((key) => key.includes('output-b'))).toBe(true);
      expect(replicateKeys.some((key) => key.endsWith(`:${outputGroupId}`))).toBe(false);
    });
  },
};

export const CollapsedPropertiesConnectToMatchingGroupsAutomatically: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await ensurePropertyInRail(canvas, 'Output', 'Species');
    await waitFor(() => {
      const paths = canvas.getAllByTestId('provenance-property-connection');
      expect(paths.every((path) => path.getAttribute('d')?.startsWith('M '))).toBe(true);
      // Species has Arabidopsis on Input A/B/C and Chlamydomonas on Input D; lines to
      // groups closer than the minimum connector distance are intentionally skipped.
      const speciesLines = connectionKeys(paths).filter((key) => key.includes('Species'));
      expect(speciesLines.length).toBeGreaterThanOrEqual(3);
    });

    // Property headers expose no draggable connection handles; their connectors derive
    // from the values they contain.
    expect(canvas.queryByTestId('provenance-connection-handle-Input-PropertyHeader')).not.toBeInTheDocument();
    expect(canvas.queryByTestId('provenance-connection-handle-Output-PropertyHeader')).not.toBeInTheDocument();
  },
};

export const PropertyConnectorPathsUpdateWhenRailControlsAppear: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await ensurePropertyInRail(canvas, 'Output', 'Species');

    const before = await waitFor(() => {
      const path = firstPropertyConnectorPath(canvasElement, 'Species');
      expect(path.getAttribute('d')).not.toBeNull();
      return path.getAttribute('d');
    });

    await userEvent.hover(canvas.getByTestId('provenance-property-Output-Species'));

    await waitFor(() => expect(firstPropertyConnectorPath(canvasElement, 'Species').getAttribute('d')).not.toBe(before), {
      timeout: 1200,
    });
  },
};

export const RailValuesAssignByDragWithoutConnectionHandles: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const source = await railValue(canvas, 'Output', 'Analysis', 'Mass Spectrometry');

    expect(within(source as HTMLElement).queryByTestId('provenance-connection-handle-Output-PropertyValue')).not.toBeInTheDocument();

    const target = canvas.getByText('Output D').closest('article')!;
    await dragByPointer(source, target);

    await waitFor(() => {
      expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('AddLoadedPropertyValue');
    });
  },
};

export const CreatesPropertyValueFromRail: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const source = await addRailValue(canvas, 'Output', 'Analysis', 'Imaging');
    await groupByProperty(canvas, 'Output', 'Analysis');
    const outputD = canvas.getByText('Output D').closest('article')!;

    await dragByPointer(source, outputD);

    await waitFor(() => {
      expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('AddLoadedPropertyValue');
      expect(canvas.getByTestId('provenance-group-Output-output:Analysis=Imaging')).toBeInTheDocument();
    });
  },
};

export const PaletteValuesLookTentativeUntilAssigned: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const source = await addRailValue(canvas, 'Output', 'Analysis', 'Sequencing');

    // A value created in the rail is only a palette entry until it is dropped.
    expect(source).toHaveAttribute('data-provenance-unassigned', 'true');
    expect(source).toHaveClass('swt:border-dashed');

    const outputD = canvas.getByText('Output D').closest('article')!;
    await dragByPointer(source, outputD);

    await waitFor(async () => {
      const assigned = await railValue(canvas, 'Output', 'Analysis', 'Sequencing');
      expect(assigned).not.toHaveAttribute('data-provenance-unassigned');
    });
  },
};

export const OverwritingAPaletteCreatedValueEmitsAnUpdatePatch: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const outputD = canvas.getByText('Output D').closest('article')!;

    const first = await addRailValue(canvas, 'Output', 'Analysis', 'Imaging');
    await dragByPointer(first, outputD);

    await waitFor(() => {
      expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('AddLoadedPropertyValue');
    });

    const second = await addRailValue(canvas, 'Output', 'Analysis', 'Sequencing');
    await dragByPointer(second, outputD);

    await waitFor(() => expect(canvas.getByTestId('provenance-overwrite-warning')).toBeInTheDocument());
    await userEvent.click(canvas.getByTestId('provenance-confirm-overwrite'));

    // The value being overwritten is Virtual (palette-created), not Real. Before
    // the PG-3 fix, editing a Virtual value emitted no patch, so the writeback
    // log would still say "add Imaging" while the model actually held
    // "Sequencing" - silent data loss for editor-created values.
    await waitFor(() => {
      expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('UpdatePropertyValue:Text:none');
    });
  },
};

export const ConnectionDetailsShowEntityPairsWithoutPropertyCreation: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    const connector = await waitFor(() => canvas.getAllByTestId('provenance-connection')[0]);
    connector.focus();
    await userEvent.keyboard('{Enter}');

    const details = await waitFor(() => canvas.getByTestId('provenance-connection-details'));
    // Underlying connections are listed as readable entity name pairs.
    expect(within(details).getByTestId('provenance-connection-pairs')).toHaveTextContent('→');
    expect(details).toHaveTextContent(/connection/i);
    expect(within(details).queryByText(/Add value/i)).not.toBeInTheDocument();
    expect(within(details).queryByText(/Add annotation/i)).not.toBeInTheDocument();
    expect(within(details).getByRole('button', { name: /remove connection/i })).toBeInTheDocument();
  },
};

export const RemovesConnectionFromDetailsPanel: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const initialCount = (await waitFor(() => {
      const connectors = canvas.getAllByTestId('provenance-connection');
      expect(connectors.length).toBeGreaterThan(0);
      return connectors;
    })).length;

    await userEvent.click(canvas.getAllByTestId('provenance-connection')[0]);
    const details = await waitFor(() => canvas.getByTestId('provenance-connection-details'));
    await userEvent.click(within(details).getByTestId('provenance-connection-remove'));

    await waitFor(() => {
      expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('RemoveLoadedConnection');
      expect(canvas.queryAllByTestId('provenance-connection').length).toBeLessThan(initialCount);
    });
    expect(canvas.queryByTestId('provenance-connection-details')).not.toBeInTheDocument();
  },
};

export const SelectsConnectionWithKeyboard: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const connector = await waitFor(() => canvas.getAllByTestId('provenance-connection')[0]);

    expect(connector).toHaveAttribute('role', 'button');
    expect(connector).toHaveAttribute('aria-label');
    expect(connector).toHaveAttribute('tabindex', '0');
    connector.focus();
    await userEvent.keyboard('{Enter}');

    await waitFor(() => expect(canvas.getByTestId('provenance-connection-details')).toBeInTheDocument());

    const secondConnector = canvas.getAllByTestId('provenance-connection')[1];
    const secondLabel = secondConnector.getAttribute('aria-label')!.replace('Select connection ', '');
    secondConnector.focus();
    await userEvent.keyboard(' ');

    await waitFor(() =>
      expect(canvas.getByTestId('provenance-connection-details')).toHaveAttribute('data-connection-id', secondLabel),
    );
  },
};

export const RemovesConnectionFromContextMenu: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const initialCount = (await waitFor(() => {
      const connectors = canvas.getAllByTestId('provenance-connection');
      expect(connectors.length).toBeGreaterThan(0);
      return connectors;
    })).length;

    const connector = canvas.getAllByTestId('provenance-connection')[0];
    fireEvent.contextMenu(connector, { clientX: 320, clientY: 240, bubbles: true });
    const menu = await screen.findByTestId('context_menu');
    await userEvent.click(within(menu).getByRole('button', { name: /delete/i }));

    await waitFor(() => {
      expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('RemoveLoadedConnection');
      expect(canvas.queryAllByTestId('provenance-connection').length).toBeLessThan(initialCount);
    });
    expect(canvas.queryByTestId('provenance-connection-details')).not.toBeInTheDocument();
  },
};

export const RemovesExpandedMemberConnectionFromContextMenu: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await groupByProperty(canvas, 'Output', 'Species');

    const grouped = await waitFor(() => canvas.getByTestId('provenance-group-Output-output:Species=Arabidopsis'));
    await userEvent.click(within(grouped).getByRole('button', { name: 'Show members' }));

    const connector = await waitFor(() => {
      const memberConnector = canvas
        .getAllByTestId('provenance-member-connection')
        .find((path) => path.getAttribute('data-provenance-connection-key')?.includes('output:Species=Arabidopsis'));
      expect(memberConnector).toBeTruthy();
      expect(memberConnector).toHaveAttribute('role', 'button');
      return memberConnector!;
    });

    const removedKey = connector.getAttribute('data-provenance-connection-key');
    await userEvent.click(connector);

    await waitFor(() => {
      expect(canvas.getByTestId('provenance-connection-details')).toBeInTheDocument();
      expect(within(grouped).getAllByTestId('provenance-connection-handle-Output-GroupMember').length).toBeGreaterThan(0);
    });

    const selectedConnector = canvas
      .getAllByTestId('provenance-member-connection')
      .find((path) => path.getAttribute('data-provenance-connection-key') === removedKey);
    expect(selectedConnector).toBeTruthy();
    fireEvent.contextMenu(selectedConnector!, { clientX: 360, clientY: 280, bubbles: true });
    const menu = await screen.findByTestId('context_menu');
    await userEvent.click(within(menu).getByRole('button', { name: /delete/i }));

    await waitFor(() => {
      expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('RemoveLoadedConnection');
      expect(connectionKeys(canvas.queryAllByTestId('provenance-member-connection'))).not.toContain(removedKey);
      expect(within(grouped).getAllByTestId('provenance-connection-handle-Output-GroupMember').length).toBeGreaterThan(0);
    });
  },
};

export const RemovesConnectionWithDeleteKey: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const connector = await waitFor(() => canvas.getAllByTestId('provenance-connection')[0]);
    const initialCount = canvas.getAllByTestId('provenance-connection').length;

    connector.focus();
    await userEvent.keyboard('{Delete}');

    await waitFor(() => {
      expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('RemoveLoadedConnection');
      expect(canvas.queryAllByTestId('provenance-connection').length).toBeLessThan(initialCount);
    });
  },
};

export const WarnsBeforeOverwritingSingleValueFromRail: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const source = await railValue(canvas, 'Output', 'Species', 'Arabidopsis');
    await groupByProperty(canvas, 'Output', 'Species');
    const target = canvas.getByTestId('provenance-group-Output-output:Species=Chlamydomonas');

    await dragByPointer(source, target);

    await waitFor(() => expect(canvas.getByTestId('provenance-overwrite-warning')).toBeInTheDocument());
    expect(canvas.getByTestId('provenance-overwrite-warning')).toHaveTextContent('Overwrite Species value?');
    // userEvent.click emits the full pointerdown/pointerup/click sequence, so a
    // stray onPointerUp bound next to onClick would double-fire the confirm
    // here - the exact-one-line assertion below is what catches that.
    await userEvent.click(canvas.getByTestId('provenance-confirm-overwrite'));

    await waitFor(() => {
      const preview = canvas.getByTestId('provenance-patch-preview').textContent ?? '';
      expect(preview).toContain('UpdatePropertyValue:Text:none');
      const updateLines = preview.split('\n').filter((line) => line.startsWith('UpdatePropertyValue:'));
      expect(updateLines).toHaveLength(1);
    });
  },
};

export const RejectsOverwriteWhenTargetHasMultipleValues: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const source = await railValue(canvas, 'Output', 'Replicate', '1');
    await groupByProperty(canvas, 'Output', 'Replicate');
    const target = canvas.getByTestId('provenance-group-Output-output:Replicate=1 | 2');

    await dragByPointer(source, target);

    await waitFor(() => expect(canvas.getByText(/Cannot overwrite Replicate/i)).toBeInTheDocument());
    expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('No patches emitted.');
  },
};

export const RefreshesRailTermValueAfterControlledMetadataReplacement: Story = {
  render: () => <Harness fixture="typedSample" allowTermReplacement />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await userEvent.click(canvas.getByRole('button', { name: /Replace term metadata/i }));
    const source = await railValue(canvas, 'Output', 'Instrument', 'mass spectrometer');
    expect(source).toBeInTheDocument();
  },
};

export const CreatesNumericPropertyValue: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const source = await addRailValue(canvas, 'Output', 'Analysis', '1.5', 'Float');
    const outputD = canvas.getByText('Output D').closest('article')!;

    await dragByPointer(source, outputD);

    await waitFor(() =>
      expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('AddLoadedPropertyValue:Float:none'),
    );
  },
};

export const RejectsNonFiniteNumericPropertyValue: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const panel = await expandProperty(canvas, 'Output', 'Analysis');

    await userEvent.click(panel.getByText('Add value'));
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /Value type/i }), 'Float');
    await userEvent.type(screen.getByRole('textbox', { name: /Analysis value/i }), 'Infinity');

    const submit = screen
      .getAllByRole('button', { name: /^Add value$/i })
      .find((button) => button.getAttribute('type') === 'submit')!;
    expect(submit).toBeDisabled();
    expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('No patches emitted.');
  },
};

export const CreatesDataEndpointFromAvailableKindList: Story = {
  render: () => <Harness fixture="dataOutputOnly" />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await userEvent.click(canvas.getByTestId('popover_trigger_provenance-add-input'));
    await userEvent.selectOptions(
      screen.getByRole('combobox', { name: /Endpoint kind/i }),
      'arc-isa:endpoint:data',
    );
    await userEvent.type(screen.getByRole('textbox', { name: /Endpoint name/i }), 'New Input');
    await userEvent.click(screen.getByRole('button', { name: /Create endpoint/i }));

    await waitFor(() =>
      expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('AddLoadedSet:arc-isa:endpoint:data:Data'),
    );
  },
};

export const KeepsEndpointKindListIndependentOfSessionReplacement: Story = {
  render: () => <Harness fixture="dataOutputOnly" allowEndpointReplacement />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await userEvent.click(canvas.getByRole('button', { name: /Replace endpoint context/i }));
    await userEvent.click(canvas.getByTestId('popover_trigger_provenance-add-input'));

    expect(screen.getByRole('combobox', { name: /Endpoint kind/i })).toHaveValue('arc-isa:endpoint:source');
  },
};

export const CreatesEndpointFromSelectedAvailableKind: Story = {
  render: () => <Harness inputOnly />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await userEvent.click(canvas.getByTestId('popover_trigger_provenance-add-output'));
    await userEvent.selectOptions(
      screen.getByRole('combobox', { name: /Endpoint kind/i }),
      'arc-isa:endpoint:material',
    );
    await userEvent.type(screen.getByRole('textbox', { name: /Endpoint name/i }), 'Custom Output');
    await userEvent.click(screen.getByRole('button', { name: /Create endpoint/i }));

    await waitFor(() =>
      expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent(
        'AddLoadedSet:arc-isa:endpoint:material:Material',
      ),
    );
  },
};

export const CreatesNextLayerAndKeepsBoundaryEditsSynchronized: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    const outputA = canvas.getByText('Output A').closest('article')!;
    await selectGroup(outputA);
    await createLayer(canvas, 'Layer 2');

    await waitFor(() => {
      expect(canvas.getByTestId('provenance-layer-layer-2')).toHaveClass('swt:btn-primary');
      expect(canvasElement).toHaveTextContent('Output A');
    });

    const source = await addRailValue(canvas, 'Input', 'Analysis', 'Imaging');
    await groupByProperty(canvas, 'Input', 'Analysis');
    const carried = canvas.getByTestId('provenance-group-Input-input:Analysis=Mass Spectrometry');
    await dragByPointer(source, carried);
    await userEvent.click(canvas.getByTestId('provenance-confirm-overwrite'));

    await userEvent.click(canvas.getByTestId('provenance-layer-layer-1'));
    await waitFor(() => expect(canvasElement).toHaveTextContent('Imaging'));
    expect(canvas.getByTestId('provenance-patch-preview')).not.toHaveTextContent('No patches emitted.');
  },
};

export const RapidEditThenLayerSwitchKeepsEdit: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    const outputA = canvas.getByText('Output A').closest('article')!;
    await selectGroup(outputA);
    await createLayer(canvas, 'Layer 2');

    await waitFor(() => {
      expect(canvas.getByTestId('provenance-layer-layer-2')).toHaveClass('swt:btn-primary');
      expect(canvasElement).toHaveTextContent('Output A');
    });

    // Dropping Analysis=Imaging onto the Mass Spectrometry group overwrites its
    // members' (inherited) Analysis values, so the edit goes through the
    // overwrite-confirm step - the same proven boundary edit as
    // CreatesNextLayerAndKeepsBoundaryEditsSynchronized. (The earlier attempt to
    // drop Species onto Output A "as a plain add" was wrong: Output A inherits
    // Species=Arabidopsis via its connection to Input A, so that drop is an
    // overwrite too and never emitted the AddLoadedPropertyValue this asserted.)
    const source = await addRailValue(canvas, 'Input', 'Analysis', 'Imaging');
    await groupByProperty(canvas, 'Input', 'Analysis');
    const carried = canvas.getByTestId('provenance-group-Input-input:Analysis=Mass Spectrometry');
    await dragByPointer(source, carried);

    await waitFor(() => expect(canvas.getByTestId('provenance-confirm-overwrite')).toBeInTheDocument());
    await userEvent.click(canvas.getByTestId('provenance-confirm-overwrite'));

    // Count of real patch lines the edit committed - the exact number depends on
    // how many members the group has, so the duplication guard below compares
    // against this baseline instead of hard-coding it.
    const patchCount = () =>
      (canvas.getByTestId('provenance-patch-preview').textContent ?? '')
        .split('\n')
        .filter((line) => line.trim().length > 0 && line !== 'No patches emitted.').length;

    let committedPatches = 0;
    await waitFor(() => {
      committedPatches = patchCount();
      expect(committedPatches).toBeGreaterThan(0);
    });

    // fireEvent (not userEvent, which adds its own settle delay): switch away
    // from and immediately back to layer 2 right after the publish above,
    // without awaiting the UI in between. A handler that closed over a
    // render-scope session instead of reading a `latest*` ref could fire
    // against a session from before this edit, dropping or duplicating it.
    fireEvent.click(canvas.getByTestId('provenance-layer-layer-1'));
    fireEvent.click(canvas.getByTestId('provenance-layer-layer-2'));

    await waitFor(() => {
      expect(canvasElement).toHaveTextContent('Imaging');
      // Neither dropped (Imaging gone / fewer patches) nor duplicated (more patches).
      expect(patchCount()).toBe(committedPatches);
    });
  },
};

export const CompletesAnInputOnlyLayer: Story = {
  render: () => <Harness inputOnly />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    expect(canvasElement).toHaveTextContent('Input Only A');
    expect(canvasElement).toHaveTextContent('No entries in this layer');

    await userEvent.click(canvas.getByTestId('popover_trigger_provenance-add-output'));
    await userEvent.type(screen.getByRole('textbox', { name: /Endpoint name/i }), 'New Output');
    await userEvent.click(screen.getByRole('button', { name: /Create endpoint/i }));

    await waitFor(() => expect(canvasElement).toHaveTextContent('New Output'));
    expect(canvas.getByTestId('provenance-patch-preview')).not.toHaveTextContent('No patches emitted.');
  },
};

export const AddsExistingPropertyToCreatedEmptySide: Story = {
  render: () => <Harness inputOnly />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await userEvent.click(canvas.getByTestId('provenance-add-output-trigger'));
    const endpoint = await waitFor(() => screen.getByRole('textbox', { name: /Endpoint name/i }));
    await userEvent.type(endpoint, 'New Output');
    await userEvent.click(screen.getByRole('button', { name: /Create endpoint/i }));

    const output = await waitFor(() => canvas.getByText('New Output').closest('article')!);
    const source = await railValue(canvas, 'Input', 'Species', 'Arabidopsis');
    await dragByPointer(source, output);

    await waitFor(() =>
      expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('AddLoadedPropertyValue'),
    );
    expect(canvas.getByText('New Output').closest('article')!).not.toHaveTextContent('Species: Arabidopsis');
  },
};

export const AddsNewPropertyFromRail: Story = {
  render: () => <Harness inputOnly />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    const target = canvas.getByText('Input Only A').closest('article')!;
    const source = await addRailProperty(canvas, 'Input', 'Treatment', 'Drought');
    await dragByPointer(source, target);

    await waitFor(() =>
      expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('AddLoadedPropertyValue'),
    );
    expect(target).not.toHaveTextContent('Treatment: Drought');
  },
};

export const HidingASideCentersTheRemainingSide: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    expect(canvas.getByTestId('provenance-property-rail-Output')).toBeInTheDocument();
    expect(canvas.getByText('Output A')).toBeInTheDocument();

    await userEvent.click(canvas.getByTestId('provenance-side-visibility-Output'));

    await waitFor(() => {
      expect(canvas.queryByTestId('provenance-property-rail-Output')).not.toBeInTheDocument();
      expect(canvas.queryByText('Output A')).not.toBeInTheDocument();
    });
    // The kept side and its rail stay on screen; only the hidden side leaves.
    expect(canvas.getByTestId('provenance-property-rail-Input')).toBeInTheDocument();
    expect(canvas.getByText('Input A')).toBeInTheDocument();

    // The visible side sits as a centered cluster: the rail is not flush to the
    // left edge, the card column keeps a generous width (no compact-container
    // downshift), and the empty space is balanced on both sides.
    {
      const surface = canvas.getByTestId('provenance-surface');
      const groupColumn = Array.from(surface.children).find((element) =>
        element.querySelector('[data-provenance-group-node^="provenance-node::Input::"]'),
      ) as HTMLElement;
      const sr = surface.getBoundingClientRect();
      const railLeft = canvas.getByTestId('provenance-property-rail-Input').getBoundingClientRect().left;
      const gc = groupColumn.getBoundingClientRect();
      const leftGap = railLeft - sr.left;
      const rightGap = sr.right - gc.right;
      expect(gc.width).toBeGreaterThan(360);
      expect(leftGap).toBeGreaterThan(24);
      expect(rightGap).toBeGreaterThan(24);
      // Equal spacers keep the cluster genuinely centered, not merely off the edge.
      expect(Math.abs(leftGap - rightGap)).toBeLessThan(32);
    }

    await userEvent.click(canvas.getByTestId('provenance-side-visibility-Output'));

    await waitFor(() => {
      expect(canvas.getByTestId('provenance-property-rail-Output')).toBeInTheDocument();
      expect(canvas.getByText('Output A')).toBeInTheDocument();
    });
  },
};

export const SwitchableAnnotationFollowsVisibleSideWhenItsSideIsHidden: Story = {
  render: () => <Harness fixture="switchableProperty" />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    // Drag Batch onto the output rail. Batch can switch sides because it exists
    // on both input and output sets.
    await ensurePropertyInRail(canvas, 'Output', 'Batch');
    expect(canvas.queryByTestId('provenance-property-Input-Batch')).not.toBeInTheDocument();

    // With outputs hidden, the switchable annotation moves onto the input rail.
    await toggleSideVisibility(canvas, 'Output', () =>
      expect(canvas.queryByTestId('provenance-property-rail-Output')).not.toBeInTheDocument(),
    );
    expect(canvas.getByTestId('provenance-property-Input-Batch')).toBeInTheDocument();

    // The move is permanent: revealing the output side leaves Batch on the input
    // rail rather than sending it back.
    await toggleSideVisibility(canvas, 'Output', () =>
      expect(canvas.getByTestId('provenance-property-rail-Output')).toBeInTheDocument(),
    );
    expect(canvas.getByTestId('provenance-property-Input-Batch')).toBeInTheDocument();
    expect(canvas.queryByTestId('provenance-property-Output-Batch')).not.toBeInTheDocument();
  },
};

export const GroupBothOnVisibleSideAppliesToHiddenSideWhenRevealed: Story = {
  render: () => <Harness fixture="switchableProperty" />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await toggleSideVisibility(canvas, 'Output', () =>
      expect(canvas.queryByTestId('provenance-property-rail-Output')).not.toBeInTheDocument(),
    );

    // Batch now sits on the visible input rail; grouping "both" from here must
    // still drive the hidden output side.
    await showPropertyControls(canvas, 'Input', 'Batch');
    for (
      let attempt = 0;
      attempt < 3 && !canvas.queryByTestId('provenance-group-Input-input:Batch=A');
      attempt += 1
    ) {
      fireEvent.click(canvas.getByTestId('provenance-property-both-Input-Batch'));
      await waitFor(
        () => expect(canvas.getByTestId('provenance-group-Input-input:Batch=A')).toBeInTheDocument(),
        { timeout: 1000 },
      ).catch(() => undefined);
    }
    await waitFor(() =>
      expect(canvas.getByTestId('provenance-group-Input-input:Batch=A')).toBeInTheDocument(),
    );

    // Showing the output side reveals the grouping the same action produced there.
    await toggleSideVisibility(canvas, 'Output', () =>
      expect(canvas.getByTestId('provenance-property-rail-Output')).toBeInTheDocument(),
    );

    await waitFor(() => {
      expect(canvas.getByTestId('provenance-group-Output-output:Batch=A')).toBeInTheDocument();
      expect(canvas.getByTestId('provenance-group-Output-output:Batch=B')).toBeInTheDocument();
    });
  },
};

let nextPointerId = 100;

function allocatePointerId() {
  nextPointerId += 1;
  return nextPointerId;
}

function nextFrame() {
  return new Promise((resolve) => requestAnimationFrame(resolve));
}

function activeDragElement() {
  return document.body.querySelector(
    [
      '[data-testid="foldered-draggable-drag-overlay"]',
      '[data-testid="provenance-drag-overlay-property"]',
      '[data-testid="provenance-drag-overlay-value"]',
      '[data-testid="provenance-live-connection"]',
    ].join(','),
  );
}

async function waitForDragActivation() {
  await waitFor(() => expect(activeDragElement()).not.toBeNull(), { timeout: 2000 });
  await nextFrame();
}

async function dragByPointer(source: Element, target: Element) {
  const pointerId = allocatePointerId();
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
  fireEvent.pointerDown(source, {
    clientX: fromX,
    clientY: fromY,
    button: 0,
    buttons: 1,
    isPrimary: true,
    pointerId,
  });
  await nextFrame();
  fireEvent.pointerMove(target, {
    clientX: activationX,
    clientY: activationY,
    button: 0,
    buttons: 1,
    isPrimary: true,
    pointerId,
  });
  await waitForDragActivation();
  fireEvent.pointerMove(document, {
    clientX: toX,
    clientY: toY,
    button: 0,
    buttons: 1,
    isPrimary: true,
    pointerId,
  });
  await nextFrame();
  fireEvent.pointerUp(target, {
    clientX: toX,
    clientY: toY,
    button: 0,
    buttons: 0,
    isPrimary: true,
    pointerId,
  });
  await nextFrame();
}

async function startDragByPointer(source: Element) {
  const pointerId = allocatePointerId();
  const from = source.getBoundingClientRect();
  const fromX = from.left + from.width / 2;
  const fromY = from.top + from.height / 2;
  const activationX = fromX + 8;
  const activationY = fromY;
  fireEvent.pointerDown(source, {
    clientX: fromX,
    clientY: fromY,
    button: 0,
    buttons: 1,
    isPrimary: true,
    pointerId,
  });
  await nextFrame();
  fireEvent.pointerMove(document, {
    clientX: activationX,
    clientY: activationY,
    button: 0,
    buttons: 1,
    isPrimary: true,
    pointerId,
  });
  await waitForDragActivation();

  return { x: activationX, y: activationY, pointerId };
}

async function moveDragPointerTo(target: Element, pointerId: number) {
  const to = target.getBoundingClientRect();
  const toX = to.left + to.width / 2;
  const toY = to.top + to.height / 2;
  fireEvent.pointerMove(document, {
    clientX: toX,
    clientY: toY,
    button: 0,
    buttons: 1,
    isPrimary: true,
    pointerId,
  });
  await nextFrame();

  return { x: toX, y: toY };
}

function escapeRegExp(value: string) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

type ConnectionNodeMeasurementCounter = {
  count: () => number;
  restore: () => void;
};

let activeMeasurementCounter: ConnectionNodeMeasurementCounter | null = null;

function waitForMilliseconds(milliseconds: number) {
  return new Promise<void>((resolve) => {
    window.setTimeout(resolve, milliseconds);
  });
}

function installConnectionNodeMeasurementCounter(): ConnectionNodeMeasurementCounter {
  const originalGetBoundingClientRect = Element.prototype.getBoundingClientRect;
  let measurementCount = 0;

  Element.prototype.getBoundingClientRect = function getBoundingClientRectWithCounter(this: Element) {
    if (this instanceof HTMLElement && this.hasAttribute('data-provenance-connection-node')) {
      measurementCount += 1;
    }

    return originalGetBoundingClientRect.call(this);
  };

  return {
    count: () => measurementCount,
    restore: () => {
      Element.prototype.getBoundingClientRect = originalGetBoundingClientRect;
      activeMeasurementCounter = null;
    },
  };
}

async function waitForStableConnectionMeasurements() {
  await waitFor(async () => {
    const before = activeMeasurementCounter!.count();
    await waitForMilliseconds(80);
    expect(activeMeasurementCounter!.count()).toBe(before);
  }, { timeout: 1600 });
}

function firstMeasuredConnectorPath(canvasElement: HTMLElement): SVGPathElement {
  const path = canvasElement.querySelector<SVGPathElement>('[data-testid="provenance-connection"]');
  if (!(path instanceof SVGPathElement)) {
    throw new Error('Expected a measured provenance connector path.');
  }
  return path;
}

function firstPropertyConnectorPath(canvasElement: HTMLElement, propertyName: string): SVGPathElement {
  const path = Array.from(canvasElement.querySelectorAll<SVGPathElement>('[data-testid="provenance-property-connection"]'))
    .find((candidate) => candidate.getAttribute('data-provenance-connection-key')?.includes(propertyName));

  if (!path) {
    throw new Error(`Expected a measured property connector path for "${propertyName}".`);
  }

  return path;
}

function shelfFolders(canvas: ReturnType<typeof within>) {
  return Array.from(
    canvas
      .getByTestId('foldered-draggable-folder-row')
      .querySelectorAll<HTMLElement>('[data-testid^="foldered-draggable-folder-"]'),
  );
}

async function openShelfFolder(canvas: ReturnType<typeof within>, folder: HTMLElement) {
  // Folders render as index-card tabs; clicking the tab activates its card.
  const folderTestId = folder.getAttribute('data-testid')!;
  const currentFolder = () => canvas.getByTestId(folderTestId);

  if (currentFolder().getAttribute('aria-selected') !== 'true') {
    await userEvent.click(currentFolder());
  }

  await waitFor(() => expect(currentFolder()).toHaveAttribute('aria-selected', 'true'));
  return within(canvas.getByTestId('foldered-draggable-item-row'));
}

// Clicking the visibility toggle right after a drag is flaky (dnd-kit's pointer
// sensor can swallow the first click), so retry until the layout settles.
async function toggleSideVisibility(
  canvas: ReturnType<typeof within>,
  side: 'Input' | 'Output',
  settled: () => void | Promise<void>,
) {
  for (let attempt = 0; attempt < 3; attempt += 1) {
    fireEvent.click(canvas.getByTestId(`provenance-side-visibility-${side}`));
    try {
      await waitFor(async () => await settled(), { timeout: 1500 });
      return;
    } catch {
      // Retry the click on the next iteration.
    }
  }

  await waitFor(async () => await settled());
}

async function createLayer(canvas: ReturnType<typeof within>, name: string) {
  await userEvent.click(canvas.getByTestId('provenance-add-layer'));
  const dialog = within(document.body);
  const input = await waitFor(() => dialog.getByRole('textbox', { name: 'Layer name' }));
  await userEvent.clear(input);
  await userEvent.type(input, name);
  await userEvent.click(dialog.getByRole('button', { name: 'Create layer' }));
}

function layerPageIds(canvas: ReturnType<typeof within>) {
  return Array.from(
    canvas
      .getByTestId('provenance-layer-pages')
      .querySelectorAll<HTMLElement>('[data-provenance-layer-page]'),
  ).map((page) => page.getAttribute('data-provenance-layer-page'));
}

async function shelfProperty(canvas: ReturnType<typeof within>, propertyName: string) {
  const name = new RegExp(`^Drag ${escapeRegExp(propertyName)}$`);

  for (const folder of shelfFolders(canvas)) {
    const row = await openShelfFolder(canvas, folder);
    const item = row.queryByRole('button', { name });

    if (item) {
      return item;
    }
  }

  throw new Error(`Could not find shelf property "${propertyName}".`);
}

async function ensurePropertyInRail(
  canvas: ReturnType<typeof within>,
  side: 'Input' | 'Output',
  propertyName: string,
) {
  const propertyId = `provenance-property-${side}-${propertyName}`;
  const existing = canvas.queryByTestId(propertyId);

  if (existing) {
    return existing;
  }

  const source = await shelfProperty(canvas, propertyName);
  await dragByPointer(source, canvas.getByTestId(`provenance-property-rail-${side}`));

  await waitFor(() => expect(canvas.queryByTestId('foldered-draggable-drag-overlay')).not.toBeInTheDocument(), {timeout: 10_000});
  return waitFor(() => canvas.getByTestId(propertyId), {timeout: 10_000});
}

function propertyColorSwatch(property: HTMLElement) {
  const swatch = property.querySelector<HTMLElement>('span[style*="background-color"]');

  if (!swatch) {
    throw new Error(`Property "${property.getAttribute('aria-label') ?? property.textContent}" has no color swatch.`);
  }

  return swatch;
}

async function setFolderPreviewColor(canvas: ReturnType<typeof within>, folder: HTMLElement, color: string) {
  // The color control sits in the active card's header, so the folder's tab
  // must be active first.
  await openShelfFolder(canvas, folder);
  const card = canvas.getByTestId('foldered-draggable-card');
  const trigger = within(card).getByRole('button', { name: /^Set color for folder / });
  const triggerLabel = trigger.getAttribute('aria-label') ?? '';
  const inputLabel = triggerLabel.replace(/^Set /, 'Choose ');

  await userEvent.click(trigger);
  const body = within(document.body);
  const colorInput = await waitFor(() => body.getByLabelText(inputLabel));
  fireEvent.change(colorInput, { target: { value: color } });
  await userEvent.click(body.getByRole('button', { name: 'Select' }));

  await waitFor(() => expect(trigger).toHaveStyle({ backgroundColor: color }));
}

async function showPropertyControls(
  canvas: ReturnType<typeof within>,
  side: 'Input' | 'Output',
  propertyName: string,
) {
  const property = await ensurePropertyInRail(canvas, side, propertyName);
  property.focus();
  await userEvent.hover(property);

  const controls = canvas.getByTestId(`provenance-property-both-${side}-${propertyName}`).parentElement!;
  await waitFor(() => expect(controls).not.toHaveClass('swt:hidden'));

  return property;
}

async function shelfPropertyOrder(canvas: ReturnType<typeof within>) {
  const folder = canvas.getByTestId('foldered-draggable-folder-source-fixture-assay-table');
  await openShelfFolder(canvas, folder);

  return Array.from(
    canvas
      .getByTestId('foldered-draggable-item-row')
      .querySelectorAll<HTMLElement>('[data-testid^="foldered-draggable-item-"]'),
  ).map((element) => (element.getAttribute('aria-label') ?? '').replace(/^Drag\s+/, ''));
}

async function expandProperty(canvas: ReturnType<typeof within>, side: 'Input' | 'Output', propertyName: string) {
  const panelId = `provenance-property-values-${side}-${propertyName}`;
  const triggerId = `provenance-property-expand-${side}-${propertyName}`;
  await ensurePropertyInRail(canvas, side, propertyName);
  // The row controls only enter the layout while the row is hovered or focused.
  await userEvent.hover(canvas.getByTestId(`provenance-property-${side}-${propertyName}`));
  for (let attempt = 0; attempt < 3 && !canvas.queryByTestId(panelId); attempt += 1) {
    await userEvent.click(canvas.getByTestId(triggerId));
    await waitFor(() => expect(canvas.getByTestId(panelId)).toBeInTheDocument(), { timeout: 1000 }).catch(() => {
      if (!canvas.queryByTestId(panelId)) {
        fireEvent.click(canvas.getByTestId(triggerId));
      }
    });
  }
  await waitFor(() => expect(canvas.getByTestId(panelId)).toBeInTheDocument(), { timeout: 3000 });
  return within(canvas.getByTestId(panelId));
}

async function groupByProperty(canvas: ReturnType<typeof within>, side: 'Input' | 'Output', propertyName: string) {
  const groupedPattern = new RegExp(`^provenance-group-${side}-${side.toLowerCase()}:.*${propertyName}=`);
  await ensurePropertyInRail(canvas, side, propertyName);

  for (let attempt = 0; attempt < 3 && canvas.queryAllByTestId(groupedPattern).length === 0; attempt += 1) {
    fireEvent.click(canvas.getByTestId(`provenance-property-${side}-${propertyName}`));
    await waitFor(() => expect(canvas.queryAllByTestId(groupedPattern).length).toBeGreaterThan(0), {
      timeout: 1000,
    }).catch(() => undefined);
  }

  await waitFor(
    () => {
      // Grouped cards carry their grouping in the card test id, e.g.
      // provenance-group-Input-input:Species=Arabidopsis.
      const grouped = canvas.getAllByTestId(groupedPattern);
      expect(grouped.length).toBeGreaterThan(0);
    },
    { timeout: 3000 },
  );
}

async function selectGroup(groupCard: HTMLElement) {
  for (let attempt = 0; attempt < 3 && !groupCard.classList.contains('swt:border-primary'); attempt += 1) {
    const checkbox = groupCard.querySelector<HTMLElement>('input[data-testid^="provenance-group-select-"]')!;
    await userEvent.click(checkbox);
    await waitFor(() => expect(groupCard).toHaveClass('swt:border-primary'), { timeout: 1000 }).catch(() => undefined);
  }

  await waitFor(() => expect(groupCard).toHaveClass('swt:border-primary'), { timeout: 3000 });
}

async function railValue(
  canvas: ReturnType<typeof within>,
  side: 'Input' | 'Output',
  propertyName: string,
  valueText: string,
) {
  const panel = await expandProperty(canvas, side, propertyName);
  const value = await waitFor(() => panel.getByText(valueText).closest('button, [role="button"]'));
  expect(value).toBeTruthy();
  return value!;
}

async function addRailValue(
  canvas: ReturnType<typeof within>,
  side: 'Input' | 'Output',
  propertyName: string,
  valueText: string,
  valueType = 'Text',
) {
  const panel = await expandProperty(canvas, side, propertyName);
  const valueLabel = new RegExp(`${escapeRegExp(propertyName)} value`, 'i');
  // Late in the loaded full-suite run the AddValuePopover trigger occasionally
  // needs a second activation before its portal form mounts (the first popover
  // still animating out from a prior add). Retry opening it until the value input
  // exists instead of assuming a single click landed.
  for (let attempt = 0; attempt < 3 && !screen.queryByRole('textbox', { name: valueLabel }); attempt += 1) {
    await userEvent.click(panel.getByText('Add value'));
    await waitFor(() => expect(screen.getByRole('textbox', { name: valueLabel })).toBeInTheDocument(), {
      timeout: 1000,
    }).catch(() => undefined);
  }
  if (valueType !== 'Text') {
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /Value type/i }), valueType);
  }
  await userEvent.type(screen.getByRole('textbox', { name: valueLabel }), valueText);
  const submit = screen
    .getAllByRole('button', { name: /^Add value$/i })
    .find((button) => button.getAttribute('type') === 'submit')!;
  await userEvent.click(submit);
  await userEvent.keyboard('{Escape}');
  return railValue(canvas, side, propertyName, valueText);
}

async function addRailProperty(
  canvas: ReturnType<typeof within>,
  side: 'Input' | 'Output',
  propertyName: string,
  valueText: string,
) {
  const rail = within(canvas.getByTestId(`provenance-property-rail-${side}`));
  const addPropertyTrigger = within(rail.getByTestId('popover_trigger_provenance-add-value-Annotation'))
    .getByText('Add annotation')
    .closest('button')!;
  fireEvent.click(addPropertyTrigger);
  const category = await waitFor(() => screen.getAllByTestId('term-search-input')[0]).catch(async () => {
    fireEvent.click(addPropertyTrigger);
    return waitFor(() => screen.getAllByTestId('term-search-input')[0]);
  });
  await userEvent.type(category, propertyName);
  await userEvent.keyboard('{Escape}');
  await userEvent.type(screen.getByRole('textbox', { name: new RegExp(`${propertyName} value`, 'i') }), valueText);
  const submit = screen
    .getAllByRole('button', { name: /^Add annotation$/i })
    .find((button) => button.getAttribute('type') === 'submit')!;
  await userEvent.click(submit);
  await userEvent.keyboard('{Escape}');
  return railValue(canvas, side, propertyName, valueText);
}

export const AppliesRailValueToSelectedGroupsByClick: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    // Without a selection the chips offer no click-apply action.
    const before = await railValue(canvas, 'Output', 'Analysis', 'Mass Spectrometry');
    expect(within(before as HTMLElement).queryByRole('button', { name: /apply to/i })).not.toBeInTheDocument();

    await selectGroup(canvas.getByText('Output D').closest('article')!);
    await selectGroup(canvas.getByText('Output E').closest('article')!);

    const source = await railValue(canvas, 'Output', 'Analysis', 'Mass Spectrometry');
    await userEvent.click(
      within(source as HTMLElement).getByRole('button', { name: /apply to 2 selected groups/i }),
    );

    // Applying to more than one group goes through the fan-out confirmation.
    await waitFor(() => expect(canvas.getByTestId('provenance-apply-batch-prompt')).toBeInTheDocument());
    await userEvent.click(canvas.getByTestId('provenance-confirm-apply'));

    await waitFor(() =>
      expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('AddLoadedPropertyValue'),
    );
  },
};

export const CopiesValueOntoAGroup: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const source = await railValue(canvas, 'Output', 'Analysis', 'Mass Spectrometry');
    const target = canvas.getByText('Output D').closest('article')!;

    await dragByPointer(source, target);

    await waitFor(() => {
      expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('AddLoadedPropertyValue');
    });
  },
};

export const ResizesThreePanelLayout: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const surface = canvas.getByTestId('provenance-surface');
    const leftSplitter = canvas.getByTestId('provenance-left-splitter');
    const before = surface.getAttribute('style');
    const surfaceRect = surface.getBoundingClientRect();
    const splitterRect = leftSplitter.getBoundingClientRect();

    fireEvent.pointerDown(leftSplitter, {
      clientX: splitterRect.left + 2,
      clientY: splitterRect.top + 8,
      button: 0,
      buttons: 1,
      isPrimary: true,
      pointerId: 11,
    });
    fireEvent.pointerMove(document, {
      clientX: surfaceRect.left + surfaceRect.width * 0.32,
      clientY: splitterRect.top + 8,
      button: 0,
      buttons: 1,
      isPrimary: true,
      pointerId: 11,
    });
    fireEvent.pointerUp(document, {
      button: 0,
      buttons: 0,
      isPrimary: true,
      pointerId: 11,
    });

    await waitFor(() => expect(surface.getAttribute('style')).not.toEqual(before));
    expect(surface.getAttribute('style')).toContain('grid-template-columns');
  },
};

export const ConnectsGroups: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const input = canvas.getByText('Input C').closest('article')!;
    const output = canvas.getByText('Output E').closest('article')!;

    await dragByPointer(
      within(input).getByTestId('provenance-connection-handle-Input-GroupCard'),
      within(output).getByTestId('provenance-connection-handle-Output-GroupCard'),
    );

    await waitFor(() => {
      expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('AddLoadedConnection');
    }, {timeout: 10_000});
    expect(canvas.queryByTestId('provenance-live-connection')).not.toBeInTheDocument();
  },
};

export const UndoRevertsLastChange: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    expect(canvas.getByTestId('provenance-undo')).toBeDisabled();

    const input = canvas.getByText('Input C').closest('article')!;
    const output = canvas.getByText('Output E').closest('article')!;
    const before = (await waitFor(() => {
      const connectors = canvas.getAllByTestId('provenance-connection');
      expect(connectors.length).toBeGreaterThan(0);
      return connectors;
    })).length;

    await dragByPointer(
      within(input).getByTestId('provenance-connection-handle-Input-GroupCard'),
      within(output).getByTestId('provenance-connection-handle-Output-GroupCard'),
    );
    await waitFor(() => expect(canvas.getAllByTestId('provenance-connection').length).toBeGreaterThan(before));

    expect(canvas.getByTestId('provenance-undo')).not.toBeDisabled();

    // fireEvent with a retry: toolbar reflow can move the button mid-click.
    for (
      let attempt = 0;
      attempt < 3 && !canvas.getByTestId('provenance-undo').hasAttribute('disabled');
      attempt += 1
    ) {
      fireEvent.click(canvas.getByTestId('provenance-undo'));
      await waitFor(() => expect(canvas.getByTestId('provenance-undo')).toBeDisabled(), {
        timeout: 1000,
      }).catch(() => undefined);
    }

    await waitFor(() => {
      expect(canvas.getByTestId('provenance-undo')).toBeDisabled();
      expect(canvas.queryAllByTestId('provenance-connection')).toHaveLength(before);
    });
  },
};

export const UndoRetractsPatchPreview: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('No patches emitted.');

    const input = canvas.getByText('Input C').closest('article')!;
    const output = canvas.getByText('Output E').closest('article')!;

    await dragByPointer(
      within(input).getByTestId('provenance-connection-handle-Input-GroupCard'),
      within(output).getByTestId('provenance-connection-handle-Output-GroupCard'),
    );

    await waitFor(() => {
      expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('AddLoadedConnection');
    });

    for (
      let attempt = 0;
      attempt < 3 && !canvas.getByTestId('provenance-undo').hasAttribute('disabled');
      attempt += 1
    ) {
      fireEvent.click(canvas.getByTestId('provenance-undo'));
      await waitFor(() => expect(canvas.getByTestId('provenance-undo')).toBeDisabled(), {
        timeout: 1000,
      }).catch(() => undefined);
    }

    // The patch preview reads the session's own PatchLog, so undoing the
    // connect (which restores the pre-edit session snapshot) must retract the
    // patch from the preview too, not just from the model.
    await waitFor(() => {
      expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('No patches emitted.');
    });
  },
};

export const ExternalSessionReplacementDisablesUndo: Story = {
  render: () => <Harness fixture="typedSample" allowTermReplacement />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    expect(canvas.getByTestId('provenance-undo')).toBeDisabled();

    const connector = await waitFor(() => canvas.getAllByTestId('provenance-connection')[0]);
    connector.focus();
    await userEvent.keyboard('{Delete}');

    await waitFor(() => expect(canvas.getByTestId('provenance-undo')).not.toBeDisabled());

    // The host replaces the session prop directly (not through onChange) -
    // the undo snapshot refers to a session the host has already discarded,
    // so it must be invalidated rather than left able to resurrect it.
    await userEvent.click(canvas.getByRole('button', { name: /Replace term metadata/i }));

    await waitFor(() => expect(canvas.getByTestId('provenance-undo')).toBeDisabled());
  },
};

export const IgnoresConnectionHandleDroppedOnCardBody: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const input = canvas.getByText('Input C').closest('article')!;
    const output = canvas.getByText('Output E').closest('article')!;
    const initialLineCount = await waitFor(() => {
      const lines = canvas.queryAllByTestId('provenance-connection');
      expect(lines.length).toBeGreaterThan(0);
      return lines.length;
    });

    await dragByPointer(
      within(input).getByTestId('provenance-connection-handle-Input-GroupCard'),
      output,
    );

    await waitFor(() => expect(canvas.queryByTestId('provenance-live-connection')).not.toBeInTheDocument());
    expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('No patches emitted.');
    expect(canvas.queryAllByTestId('provenance-connection')).toHaveLength(initialLineCount);
  },
};

export const InvalidSameSideConnectionDropIsIgnored: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const inputA = canvas.getByText('Input A').closest('article')!;
    const inputB = canvas.getByText('Input B').closest('article')!;
    const initialLines = canvas.queryAllByTestId('provenance-connection').length;

    await dragByPointer(
      within(inputA).getByTestId('provenance-connection-handle-Input-GroupCard'),
      inputB,
    );

    await waitFor(() => expect(canvas.queryAllByTestId('provenance-connection')).toHaveLength(initialLines));
    expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('No patches emitted.');
  },
};

export const MismatchedGroupConnectionPromptsForResolution: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await groupByProperty(canvas, 'Output', 'Species');

    const inputGroup = canvas.getByText('Input D').closest('article')!;
    const outputGroup = await waitFor(() => canvas.getByTestId('provenance-group-Output-output:Species=Arabidopsis'));
    const outputHandle = within(outputGroup).getByTestId('provenance-connection-handle-Output-GroupCard');

    await dragByPointer(
      within(inputGroup).getByTestId('provenance-connection-handle-Input-GroupCard'),
      outputHandle,
    );

    await waitFor(() => expect(canvas.getByTestId('provenance-member-resolution-prompt')).toBeInTheDocument());
    expect(canvas.getByTestId('provenance-member-resolution-prompt')).toHaveTextContent('1 input member');
    expect(canvas.getByTestId('provenance-member-resolution-prompt')).toHaveTextContent('3 output members');
  },
};

export const EqualCountGroupConnectionOffersPairByOrder: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    // Grouping Species on both sides yields two 3-member Arabidopsis groups.
    for (
      let attempt = 0;
      attempt < 3 && !canvas.queryByTestId('provenance-group-Input-input:Species=Arabidopsis');
      attempt += 1
    ) {
      await showPropertyControls(canvas, 'Output', 'Species');
      fireEvent.click(canvas.getByTestId('provenance-property-both-Output-Species'));
      await waitFor(() => expect(canvas.queryByTestId('provenance-group-Input-input:Species=Arabidopsis')).toBeInTheDocument(), {
        timeout: 1000,
      }).catch(() => undefined);
    }

    const inputGroup = await waitFor(() => canvas.getByTestId('provenance-group-Input-input:Species=Arabidopsis'));
    const outputGroup = await waitFor(() => canvas.getByTestId('provenance-group-Output-output:Species=Arabidopsis'));

    await dragByPointer(
      within(inputGroup).getByTestId('provenance-connection-handle-Input-GroupCard'),
      within(outputGroup).getByTestId('provenance-connection-handle-Output-GroupCard'),
    );

    // Equal counts are not connected silently; the prompt offers order pairing.
    const prompt = await waitFor(() => canvas.getByTestId('provenance-member-resolution-prompt'));
    expect(prompt).toHaveTextContent('3 input members');
    expect(prompt).toHaveTextContent('3 output members');

    // fireEvent with a retry: the floating prompt animates in, so a positioned
    // click can miss on slow runs.
    for (
      let attempt = 0;
      attempt < 3 && canvas.queryByTestId('provenance-member-resolution-prompt');
      attempt += 1
    ) {
      fireEvent.click(canvas.getByTestId('provenance-member-resolution-pair-by-order'));
      await waitFor(() => expect(canvas.queryByTestId('provenance-member-resolution-prompt')).not.toBeInTheDocument(), {
        timeout: 1000,
      }).catch(() => undefined);
    }

    // The three ordered pairs (input-a↔output-a, input-b↔output-b,
    // input-c↔output-c) are all already connected in the fixture, so pair-by-order
    // hits the connectSets duplicate guard: it resolves the prompt without
    // emitting a duplicate connection patch. Emitting AddLoadedConnection here (as
    // this once asserted) would mean re-connecting an already-connected pair,
    // which the shared Edit layer deliberately makes a no-op.
    expect(canvas.queryByTestId('provenance-member-resolution-prompt')).not.toBeInTheDocument();
    expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('No patches emitted.');
  },
};

export const ManualMismatchResolutionExpandsMembersWithoutPatches: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await groupByProperty(canvas, 'Output', 'Species');

    const inputGroup = canvas.getByText('Input D').closest('article')!;
    const outputGroup = await waitFor(() => canvas.getByTestId('provenance-group-Output-output:Species=Arabidopsis'));
    const outputHandle = within(outputGroup).getByTestId('provenance-connection-handle-Output-GroupCard');

    await dragByPointer(
      within(inputGroup).getByTestId('provenance-connection-handle-Input-GroupCard'),
      outputHandle,
    );
    await waitFor(() => expect(canvas.getByTestId('provenance-member-resolution-prompt')).toBeInTheDocument());
    await userEvent.click(canvas.getByTestId('provenance-member-resolution-manual'));

    // Exactly the two cards that were about to be connected open with their
    // member handles; other groups connected to them stay collapsed.
    await waitFor(() => {
      const currentInputGroup = canvas.getByTestId('provenance-group-Input-input:input-d');
      const currentOutputGroup = canvas.getByTestId('provenance-group-Output-output:Species=Arabidopsis');
      expect(within(currentInputGroup).getAllByTestId('provenance-connection-handle-Input-GroupMember').length).toBeGreaterThan(0);
      expect(within(currentOutputGroup).getAllByTestId('provenance-connection-handle-Output-GroupMember').length).toBeGreaterThan(0);
    });

    const otherOutputGroup = canvas.getByTestId('provenance-group-Output-output:Species=Chlamydomonas');
    expect(within(otherOutputGroup).queryByTestId('provenance-connection-handle-Output-GroupMember')).not.toBeInTheDocument();
    expect(canvas.queryByTestId('provenance-member-resolution-prompt')).not.toBeInTheDocument();
    expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('No patches emitted.');

    // A follow-up hint explains how to connect members individually.
    const hint = canvas.getByTestId('provenance-hint');
    expect(hint).toHaveTextContent(/connection handle/i);
    await userEvent.click(canvas.getByTestId('provenance-hint-dismiss'));
    await waitFor(() => expect(canvas.queryByTestId('provenance-hint')).not.toBeInTheDocument());
  },
};

export const ExpandedGroupedCardsDoNotExpandConnectedSingleCards: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await groupByProperty(canvas, 'Output', 'Species');

    const inputA = canvas.getByText('Input A').closest('article')!;
    const outputGroup = await waitFor(() => canvas.getByTestId('provenance-group-Output-output:Species=Arabidopsis'));

    await userEvent.click(within(outputGroup).getByRole('button', { name: 'Show members' }));

    await waitFor(() => {
      expect(within(outputGroup).getAllByTestId('provenance-connection-handle-Output-GroupMember').length).toBeGreaterThan(0);
      expect(within(inputA).queryByTestId('provenance-group-member-Input-input-a')).not.toBeInTheDocument();
      expect(within(inputA).queryByTestId('provenance-connection-handle-Input-GroupMember')).not.toBeInTheDocument();
    });
  },
};

export const LayerTabsUseSourceColorsAndSideRails: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    const layer1 = canvas.getByTestId('provenance-layer-layer-1');
    const layerColor = layer1.getAttribute('data-provenance-layer-color') ?? '';

    expect(layer1).toHaveClass('swt:btn-primary');
    expect(layerColor).toMatch(/^#/);
    expect(layerColor).not.toContain('|');

    expect(canvas.getByTestId('provenance-property-rail-Input')).toHaveAttribute(
      'data-provenance-side-id',
      'layer-1-input',
    );
    expect(canvas.getByTestId('provenance-property-rail-Output')).toHaveAttribute(
      'data-provenance-side-id',
      'layer-1-output',
    );
  },
};

export const LayerPaginationUsesNeighborWindowAndArrowSwitches: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await createLayer(canvas, 'Layer 2');
    await createLayer(canvas, 'Layer 3');

    const pagination = within(canvas.getByTestId('provenance-layer-pagination'));
    expect(canvas.queryByTestId('provenance-layer-select')).not.toBeInTheDocument();
    expect(pagination.getByTestId('provenance-add-layer')).toBeInTheDocument();

    await waitFor(() => {
      expect(layerPageIds(canvas)).toEqual(['layer-1', 'layer-2', 'layer-3']);
      expect(canvas.getByTestId('provenance-layer-layer-3')).toHaveClass('swt:btn-primary');
    });
    // The jump trigger doubles as the layer position indicator.
    expect(pagination.getByTestId('provenance-layer-jump')).toHaveTextContent('3 / 3');
    expect(canvas.getByTestId('provenance-layer-layer-2')).toHaveClass('swt:opacity-50');
    expect(canvas.queryByTestId('provenance-layer-next')).not.toBeInTheDocument();
    expect(pagination.getByTestId('provenance-layer-prev').querySelector('[class*="fluent--chevron-left"]'))
      .toBeInTheDocument();

    await userEvent.click(pagination.getByTestId('provenance-layer-prev'));

    await waitFor(() => {
      expect(layerPageIds(canvas)).toEqual(['layer-1', 'layer-2', 'layer-3']);
      expect(canvas.getByTestId('provenance-layer-layer-2')).toHaveClass('swt:btn-primary');
    });
    expect(canvas.getByTestId('provenance-layer-layer-1')).toHaveClass('swt:opacity-50');
    expect(canvas.getByTestId('provenance-layer-layer-3')).toHaveClass('swt:opacity-50');
    expect(pagination.getByTestId('provenance-layer-next').querySelector('[class*="fluent--chevron-right"]'))
      .toBeInTheDocument();

    await userEvent.click(pagination.getByTestId('provenance-layer-prev'));

    await waitFor(() => {
      expect(layerPageIds(canvas)).toEqual(['layer-1', 'layer-2', 'layer-3']);
      expect(canvas.getByTestId('provenance-layer-layer-1')).toHaveClass('swt:btn-primary');
    });
    expect(pagination.getByTestId('provenance-layer-jump')).toHaveTextContent('1 / 3');
    expect(canvas.queryByTestId('provenance-layer-prev')).not.toBeInTheDocument();
    expect(pagination.getByTestId('provenance-layer-next').querySelector('[class*="fluent--chevron-right"]'))
      .toBeInTheDocument();
  },
};

export const AddsLayerFromMixedSelection: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const inputA = canvas.getByText('Input A').closest('article')!;
    const outputB = canvas.getByText('Output B').closest('article')!;

    await selectGroup(inputA);
    await selectGroup(outputB);
    await createLayer(canvas, 'Layer 2');

    await waitFor(() => {
      expect(canvas.getByTestId('provenance-layer-layer-2')).toHaveClass('swt:btn-primary');
      expect(canvasElement).toHaveTextContent('Input A');
      expect(canvasElement).toHaveTextContent('Output B');
    });
  },
};

export const AddLayerPopoverAnnouncesSeedEntities: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    // Without a selection the new layer continues from all outputs by default.
    await userEvent.click(canvas.getByTestId('provenance-add-layer'));
    const dialog = within(document.body);
    await waitFor(() =>
      expect(dialog.getByTestId('provenance-layer-seed-summary')).toHaveTextContent(
        /Starts from all \d+ outputs of this layer \(default\)/,
      ),
    );
    await userEvent.keyboard('{Escape}');

    // With a selection the popover names the selected groups and entity count.
    const outputA = canvas.getByText('Output A').closest('article')!;
    await selectGroup(outputA);
    await userEvent.click(canvas.getByTestId('provenance-add-layer'));
    await waitFor(() =>
      expect(dialog.getByTestId('provenance-layer-seed-summary')).toHaveTextContent('Starts from 1 selected group (1 entity).'),
    );
    await userEvent.keyboard('{Escape}');
  },
};

export const CreatesNamedLayer: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await createLayer(canvas, 'Extraction');

    await waitFor(() => {
      const layer = canvas.getByTestId('provenance-layer-layer-2');
      expect(layer).toHaveClass('swt:btn-primary');
      expect(layer).toHaveAccessibleName('View provenance layer Extraction');
      expect(layer).toHaveTextContent('Extraction');
    });
  },
};

export const DoesNotReuseSelectionForEqualGroupIdsInDifferentLayers: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const outputA = canvas.getByText('Output A').closest('article')!;

    await selectGroup(outputA);
    await createLayer(canvas, 'Layer 2');

    await userEvent.click(canvas.getByTestId('popover_trigger_provenance-add-output'));
    await userEvent.type(screen.getByRole('textbox', { name: /Endpoint name/i }), 'Layer 2 Output');
    await userEvent.click(screen.getByRole('button', { name: /Create endpoint/i }));
    const layer2Output = await waitFor(() => canvas.getByText('Layer 2 Output').closest('article')!);

    await selectGroup(layer2Output);
    await createLayer(canvas, 'Layer 3');

    await userEvent.click(canvas.getByTestId('popover_trigger_provenance-add-output'));
    await userEvent.type(screen.getByRole('textbox', { name: /Endpoint name/i }), 'Layer 3 Output');
    await userEvent.click(screen.getByRole('button', { name: /Create endpoint/i }));
    const layer3Output = await waitFor(() => canvas.getByText('Layer 3 Output').closest('article')!);

    expect(layer3Output).not.toHaveClass('swt:border-primary');
  },
};

export const StrictModeSmoke: Story = {
  // React.StrictMode double-invokes renders (and, in the relevant React
  // versions, effects) in development - the closest browser-testable proxy
  // for a render being committed twice or discarded. Render-phase writes to
  // "latest" refs would show up here as duplicated patch lines from a single
  // user action.
  render: () => (
    <React.StrictMode>
      <Harness />
    </React.StrictMode>
  ),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const source = await addRailValue(canvas, 'Output', 'Analysis', 'Imaging');
    await groupByProperty(canvas, 'Output', 'Analysis');
    const outputD = canvas.getByText('Output D').closest('article')!;

    await dragByPointer(source, outputD);

    await waitFor(() => {
      const preview = canvas.getByTestId('provenance-patch-preview').textContent ?? '';
      const addLines = preview.split('\n').filter((line) => line.startsWith('AddLoadedPropertyValue:'));
      expect(addLines).toHaveLength(1);
    });
    expect(canvas.getByTestId('provenance-group-Output-output:Analysis=Imaging')).toBeInTheDocument();

    await waitFor(() => expect(canvas.getByTestId('provenance-undo')).not.toBeDisabled());

    // fireEvent with a retry (as elsewhere in this file): late in the loaded
    // suite the first undo click can land during a toolbar reflow and miss.
    // Undo is single-step, so once it takes the button disables and extra
    // clicks are safe no-ops.
    for (
      let attempt = 0;
      attempt < 3 && !canvas.getByTestId('provenance-undo').hasAttribute('disabled');
      attempt += 1
    ) {
      fireEvent.click(canvas.getByTestId('provenance-undo'));
      await waitFor(() => expect(canvas.getByTestId('provenance-undo')).toBeDisabled(), {
        timeout: 1000,
      }).catch(() => undefined);
    }

    await waitFor(() => {
      expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('No patches emitted.');
    });
  },
};

export const OpensInteractiveTutorialOnSampleData: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(await canvas.findByTestId('provenance-tutorial-trigger'));

    const modal = within(canvas.getByTestId('provenance-tutorial-modal'));
    expect(modal.getByText('Provenance editor tour')).toBeInTheDocument();
    expect(within(modal.getByTestId('tutorial-step-card')).getByText('Welcome')).toBeInTheDocument();

    // The sandboxed editor must not offer a tutorial of its own (no nesting).
    expect(modal.queryByTestId('provenance-tutorial-trigger')).not.toBeInTheDocument();

    // The feature list jumps straight to any step's explanation; the sandbox
    // remounts at that step's checkpoint, so the state its task needs (here:
    // inputs already grouped by Species) exists without doing earlier steps.
    await userEvent.click(modal.getByTestId('tutorial-sidebar-step-members'));
    expect(within(modal.getByTestId('tutorial-step-card')).getByText('Inspect group members')).toBeInTheDocument();
    await waitFor(() =>
      expect(modal.getByTestId('provenance-group-Input-input:Species=Arabidopsis')).toBeInTheDocument(),
    );

    // Closing returns to the host editor without any writeback patches.
    await userEvent.click(modal.getByTestId('tutorial-close'));
    expect(canvas.queryByTestId('provenance-tutorial-modal')).not.toBeInTheDocument();
    expect(canvas.getByTestId('provenance-patch-preview')).toHaveTextContent('No patches emitted.');
  },
};

export const TutorialTaskStepCompletesInsideSandbox: Story = {
  render: () => <Harness />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(await canvas.findByTestId('provenance-tutorial-trigger'));
    const modal = within(canvas.getByTestId('provenance-tutorial-modal'));

    // Jump to the shelf-to-rail step and fulfil it by dragging Species into
    // the sandbox's input rail; the polled condition marks the step completed
    // and Skip becomes Next. The modal's feature list narrows the editor into
    // the medium tier, so the rail sits behind its fold toggle first.
    await userEvent.click(modal.getByTestId('tutorial-sidebar-step-shelf-to-rail'));
    expect(modal.getByTestId('tutorial-next')).toHaveTextContent('Skip');
    if (!modal.queryByTestId('provenance-property-rail-Input')) {
      await userEvent.click(modal.getByTestId('provenance-rail-toggle-Input'));
    }
    const source = await shelfProperty(modal, 'Species');
    await dragByPointer(source, modal.getByTestId('provenance-property-rail-Input'));
    await waitFor(() => expect(modal.getByTestId('tutorial-next')).toHaveTextContent('Next'), { timeout: 5000 });
    expect(within(modal.getByTestId('tutorial-task')).getByText('Completed:')).toBeInTheDocument();
    await userEvent.click(modal.getByTestId('tutorial-next'));
    expect(within(modal.getByTestId('tutorial-step-card')).getByText('Group by an annotation')).toBeInTheDocument();

    // The click task completes in place as well; the user moves on themselves.
    await userEvent.click(modal.getByTestId('provenance-property-Input-Species'));
    await waitFor(() => expect(modal.getByTestId('tutorial-next')).toHaveTextContent('Next'), { timeout: 5000 });
    expect(modal.getByText('2 of 14 features explored')).toBeInTheDocument();
    await userEvent.click(modal.getByTestId('tutorial-next'));
    expect(within(modal.getByTestId('tutorial-step-card')).getByText('Inspect group members')).toBeInTheDocument();
  },
};
