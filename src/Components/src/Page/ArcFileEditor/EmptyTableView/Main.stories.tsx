import React from 'react';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, userEvent, waitFor, within } from 'storybook/test';
import { Entry } from './Main.sample.fs.js';

const meta = {
  title: 'Page Components/ArcFileEditor/Empty table setup',
  component: Entry,
  parameters: { layout: 'fullscreen', isolated: true },
} satisfies Meta<typeof Entry>;

export default meta;
type Story = StoryObj<typeof meta>;

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
  await userEvent.click(canvas.getByRole('button', { name: /Start from scratch!/ }));
  const heading = canvas.getByRole('heading', { name: 'Select a building block' });
  await waitFor(() => expect(heading).toHaveFocus());
  expect(portal.queryByRole('dialog', { name: 'Select a building block' })).not.toBeInTheDocument();

  await userEvent.click(canvas.getByRole('button', { name: 'Parameter', exact: true }));
  const picker = await portal.findByRole('dialog', { name: 'Building block type' });
  await userEvent.click(within(picker).getByText('More', { exact: true }));
  const date = within(picker).getByRole('button', { name: 'Date', exact: true });
  date.scrollIntoView({ block: 'nearest' });
  await waitFor(() => expectPointerTarget(date));
  await userEvent.click(date);
  expect(portal.queryByRole('dialog', { name: 'Building block type' })).not.toBeInTheDocument();
  await userEvent.click(canvas.getByRole('button', { name: 'Add Column' }));
  await waitFor(() => {
    expect(canvas.queryByRole('heading', { name: 'Select a building block' })).not.toBeInTheDocument();
    expect(canvas.getByText('Date', { exact: true })).toBeInTheDocument();
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
    await userEvent.click(canvas.getByRole('button', { name: /Start from scratch!/ }));
    const input = canvas.getAllByRole('combobox')[0];
    await userEvent.type(input, 'Temp');
    const result = await portal.findByRole('option', { name: /Temperature/ });
    await waitFor(() => expectPointerTarget(result));
    await userEvent.click(result);
    expect(input).toHaveValue('Temperature');
    await userEvent.click(canvas.getByRole('button', { name: 'Add Column' }));
    await waitFor(() => {
      expect(canvas.queryByRole('heading', { name: 'Select a building block' })).not.toBeInTheDocument();
      expect(canvas.getByText(/Temperature/)).toBeInTheDocument();
    });
  }),
};

export const TemplateFiltersAndCancel: Story = {
  play: async ({ canvasElement }) => withViewport(1280, 800, async () => {
    const canvas = within(canvasElement);
    const portal = within(canvasElement.ownerDocument.body);
    await userEvent.click(canvas.getByRole('button', { name: /Start with template!/ }));
    await waitFor(() => expect(canvas.getByRole('heading', { name: 'Select template(s)' })).toHaveFocus());
    expect(canvas.getByText('Leaf template', { exact: true })).toBeInTheDocument();
    expect(canvas.getByText('Root template', { exact: true })).toBeInTheDocument();
    expect(canvas.queryByText('Instrument template', { exact: true })).not.toBeInTheDocument();

    const search = canvas.getByRole('combobox');
    await userEvent.type(search, 'Leaf');
    const leafFilter = await portal.findByRole('option', { name: /Leaf template/ });
    await userEvent.click(leafFilter);
    await waitFor(() => expect(canvas.queryByText('Root template', { exact: true })).not.toBeInTheDocument());
    await userEvent.click(search);
    await userEvent.keyboard('{Backspace}{Escape}');
    await waitFor(() => expect(canvas.getByText('Root template', { exact: true })).toBeInTheDocument());

    // Community filtering must change the rendered results, not only the count.
    const communityIcon = canvas.getByTitle('Filter communities');
    await userEvent.click(communityIcon.parentElement!);
    const communities = await portal.findByRole('listbox');
    await userEvent.click(within(communities).getByRole('option', { name: /Other Lab/ }));
    await userEvent.keyboard('{Escape}');
    await waitFor(() => expect(canvas.getByText('Instrument template', { exact: true })).toBeInTheDocument());

    await userEvent.click(canvas.getByText('Leaf template', { exact: true }));
    await userEvent.click(canvas.getByRole('button', { name: 'Import', exact: true }));
    const review = await portal.findByRole('dialog', { name: 'Import templates' });
    await userEvent.click(within(review).getByRole('button', { name: 'Cancel' }));
    expect(canvas.getByText('1 selected')).toBeInTheDocument();
    expect(canvas.getByRole('heading', { name: 'Select template(s)' })).toBeInTheDocument();
    await userEvent.click(canvas.getByRole('button', { name: 'Back', exact: true }));
    await waitFor(() => expect(canvas.getByRole('button', { name: /Start with template!/ })).toHaveFocus());
  }),
};

export const TemplateAppendReturnsToEditor: Story = {
  play: async ({ canvasElement }) => withViewport(1280, 800, async () => {
    const canvas = within(canvasElement);
    const portal = within(canvasElement.ownerDocument.body);
    await userEvent.click(canvas.getByRole('button', { name: /Start with template!/ }));
    await userEvent.click(canvas.getByText('Leaf template', { exact: true }));
    await userEvent.click(canvas.getByRole('button', { name: 'Import', exact: true }));
    const review = await portal.findByRole('dialog', { name: 'Import templates' });
    await userEvent.click(within(review).getByRole('button', { name: 'Import', exact: true }));
    await waitFor(() => {
      expect(canvas.queryByRole('heading', { name: 'Select template(s)' })).not.toBeInTheDocument();
      expect(canvas.getByText(/Leaf temperature/)).toBeInTheDocument();
    });
    await userEvent.click(canvas.getByRole('button', { name: 'Empty two', exact: true }));
    expect(canvas.getByRole('button', { name: /Start with template!/ })).toBeInTheDocument();
  }),
};
