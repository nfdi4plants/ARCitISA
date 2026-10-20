import React from 'react';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, userEvent, waitFor, within } from 'storybook/test';
import { Entry, Fixtures_templates } from './Main.sample.fs.js';

const meta = {
  title: 'Page Components/ArcFileEditor/Empty table setup',
  component: Entry,
  parameters: { layout: 'fullscreen', isolated: true },
} satisfies Meta<typeof Entry>;

export default meta;
type Story = StoryObj<typeof meta>;

const [leafTemplate, rootTemplate, instrumentTemplate] = Fixtures_templates();
const leafRowId = `template-row-${leafTemplate.Id}`;
const rootRowId = `template-row-${rootTemplate.Id}`;
const instrumentRowId = `template-row-${instrumentTemplate.Id}`;
const leafSelectionId = `template-selection-${leafTemplate.Id}`;

// Storybook's browser tests own the real viewport. A container width alone would
// leave portaled menus unconstrained and miss the clipping regression.
async function withViewport(width: number, height: number, run: () => Promise<void>) {
  if (!('__vitest_browser__' in globalThis)) return run();
  const { page } = await import('vitest/browser');
  const previousWidth = window.innerWidth;
  const previousHeight = window.innerHeight;
  await page.viewport(width, height);
  try {
    await run();
  } finally {
    await page.viewport(previousWidth, previousHeight);
  }
}

function expectPointerTarget(element: HTMLElement) {
  const doc = element.ownerDocument;
  const view = doc.defaultView!;
  const rect = element.getBoundingClientRect();
  expect(rect.width).toBeGreaterThan(0);
  expect(rect.height).toBeGreaterThan(0);
  expect(rect.left).toBeGreaterThanOrEqual(0);
  expect(rect.top).toBeGreaterThanOrEqual(0);
  expect(rect.right).toBeLessThanOrEqual(view.innerWidth + 1);
  expect(rect.bottom).toBeLessThanOrEqual(view.innerHeight + 1);
  const target = doc.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2);
  expect(target && (target === element || element.contains(target))).toBe(true);
}

async function buildingBlockFlow(canvasElement: HTMLElement) {
  const canvas = within(canvasElement);
  const portal = within(canvasElement.ownerDocument.body);
  await userEvent.click(canvas.getByTestId('empty-table-start-scratch'));
  expect(canvas.getByTestId('empty-table-building-block-page')).toBeInTheDocument();
  const heading = canvas.getByTestId('empty-table-setup-heading');
  await waitFor(() => expect(heading).toHaveFocus());
  expect(canvas.queryByTestId('empty-table-choices')).not.toBeInTheDocument();

  await userEvent.click(canvas.getByTestId('building-block-header-select'));
  await userEvent.click(await portal.findByTestId('building-block-submenu-more'));
  const date = portal.getByTestId('building-block-header-Date');
  date.scrollIntoView({ block: 'nearest' });
  await waitFor(() => expectPointerTarget(date));
  await userEvent.click(date);
  expect(portal.queryByTestId('building-block-header-Date')).not.toBeInTheDocument();
  await userEvent.click(canvas.getByTestId('building-block-add-column'));
  await waitFor(() => {
    expect(canvas.queryByTestId('empty-table-building-block-page')).not.toBeInTheDocument();
    expect(canvasElement).toHaveTextContent('Date');
  });
}

export const DesktopBuildingBlock: Story = {
  play: async ({ canvasElement }) => withViewport(1280, 800, () => buildingBlockFlow(canvasElement)),
};

export const TabletBuildingBlock: Story = {
  play: async ({ canvasElement }) => withViewport(768, 600, () => buildingBlockFlow(canvasElement)),
};

export const NarrowBuildingBlock: Story = {
  play: async ({ canvasElement }) => withViewport(375, 667, () => buildingBlockFlow(canvasElement)),
};

export const TermAutocomplete: Story = {
  play: async ({ canvasElement }) => withViewport(1280, 800, async () => {
    const canvas = within(canvasElement);
    const portal = within(canvasElement.ownerDocument.body);
    await userEvent.click(canvas.getByTestId('empty-table-start-scratch'));
    const headerControls = within(canvas.getByTestId('building-block-header-controls'));
    const input = headerControls.getByTestId('term-search-input');
    await userEvent.type(input, 'Temp');
    const result = await portal.findByTestId('term-search-result-PATO:0000146');
    await waitFor(() => expectPointerTarget(result));
    await userEvent.click(result);
    expect(input).toHaveValue('Temperature');
    await userEvent.click(canvas.getByTestId('building-block-add-column'));
    await waitFor(() => {
      expect(canvas.queryByTestId('empty-table-building-block-page')).not.toBeInTheDocument();
      expect(canvasElement).toHaveTextContent('Temperature');
    });
  }),
};

export const TemplateFiltersAndCancel: Story = {
  play: async ({ canvasElement }) => withViewport(1280, 800, async () => {
    const canvas = within(canvasElement);
    const portal = within(canvasElement.ownerDocument.body);
    await userEvent.click(canvas.getByTestId('empty-table-start-template'));
    await waitFor(() => expect(canvas.getByTestId('empty-table-setup-heading')).toHaveFocus());
    expect(canvas.getByTestId(leafRowId)).toBeInTheDocument();
    expect(canvas.getByTestId(rootRowId)).toBeInTheDocument();
    expect(canvas.queryByTestId(instrumentRowId)).not.toBeInTheDocument();

    const search = canvas.getByTestId('template-filter-search');
    await userEvent.type(search, 'Leaf');
    const leafFilter = await portal.findByTestId('template-filter-option-nameLeaf template');
    await userEvent.click(leafFilter);
    await waitFor(() => expect(canvas.queryByTestId(rootRowId)).not.toBeInTheDocument());
    await userEvent.click(search);
    await userEvent.keyboard('{Backspace}{Escape}');
    await waitFor(() => expect(canvas.getByTestId(rootRowId)).toBeInTheDocument());

    // Community filtering must change the rendered results, not only the count.
    const communityFilter = canvas.getByTestId('template-community-filter');
    await userEvent.click(communityFilter.parentElement!);
    const communities = await portal.findByRole('listbox');
    await userEvent.click(within(communities).getByRole('option', { name: /Other Lab/ }));
    await userEvent.keyboard('{Escape}');
    await waitFor(() => expect(canvas.getByTestId(instrumentRowId)).toBeInTheDocument());

    await userEvent.click(canvas.getByTestId(leafRowId));
    await userEvent.click(canvas.getByTestId('template-browser-import'));
    const review = await portal.findByTestId('modal_template-import');
    await userEvent.click(within(review).getByTestId('template-import-cancel'));
    expect(portal.queryByTestId('modal_template-import')).not.toBeInTheDocument();
    expect(canvas.getByTestId(leafSelectionId)).toBeChecked();
    expect(canvas.getByTestId('empty-table-templates-page')).toBeInTheDocument();
    await userEvent.click(canvas.getByTestId('empty-table-setup-back'));
    await waitFor(() => {
      expect(canvas.getByTestId('empty-table-choices')).toBeInTheDocument();
      expect(canvas.queryByTestId('empty-table-templates-page')).not.toBeInTheDocument();
    });
  }),
};

export const TemplateAppendReturnsToEditor: Story = {
  play: async ({ canvasElement }) => withViewport(1280, 800, async () => {
    const canvas = within(canvasElement);
    const portal = within(canvasElement.ownerDocument.body);
    await userEvent.click(canvas.getByTestId('empty-table-start-template'));
    await userEvent.click(canvas.getByTestId(leafRowId));
    await userEvent.click(canvas.getByTestId('template-browser-import'));
    const review = await portal.findByTestId('modal_template-import');
    await userEvent.click(within(review).getByTestId('template-import-confirm'));
    await waitFor(() => {
      expect(canvas.queryByTestId('empty-table-templates-page')).not.toBeInTheDocument();
      expect(canvasElement).toHaveTextContent('Leaf temperature');
    });
    await userEvent.click(canvas.getByTestId('arc-file-editor-table-tab-1'));
    expect(canvas.getByTestId('empty-table-choices')).toBeInTheDocument();
  }),
};

export const PriorTablesFromArc: Story = {
  args: { withArcNodes: "fetchArcNodes" },
  play: async ({ canvasElement }) => withViewport(1280, 800, async () => {
    const canvas = within(canvasElement);
    await userEvent.click(canvas.getByTestId('empty-table-use-existing-tables'));
    expect(canvas.getByTestId('empty-table-existing-tables-page')).toBeInTheDocument();
    await waitFor(() => expect(canvas.getByTestId('empty-table-setup-heading')).toHaveFocus());
    const select = await canvas.findByTestId('empty-table-existing-column-select');
    const input = canvas.getByTestId('empty-table-existing-column-Study Field trial-Harvest-Input');
    await userEvent.selectOptions(select, input);
    expect(canvas.getByTestId('empty-table-existing-column-preview')).toHaveTextContent('Plant 1');
    await userEvent.click(canvas.getByTestId('empty-table-existing-column-import'));
    await waitFor(() => {
      expect(canvas.queryByTestId('empty-table-existing-tables-page')).not.toBeInTheDocument();
      expect(canvasElement).toHaveTextContent('Plant 1');
    });
  }),
};

