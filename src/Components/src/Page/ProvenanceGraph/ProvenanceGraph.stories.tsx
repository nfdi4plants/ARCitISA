import React from 'react';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, userEvent, waitFor, within } from 'storybook/test';
import { ProvenanceGraph } from './ProvenanceGraph.fs.js';
import { emptyIndex, index, wipIndex } from './ProvenanceGraph.sample.fs.js';

// The page fills its parent, so every story gives it a box with a height.
const box = (width: number | string, height: number) => (Story: React.ComponentType) => (
  <div style={{ width, height, border: '1px solid #8884' }}>
    <Story />
  </div>
);

const meta = {
  title: 'Page Components/ProvenanceGraph',
  tags: ['autodocs'],
  parameters: {
    layout: 'fullscreen',
  },
  args: {
    index,
  },
  component: ProvenanceGraph,
} satisfies Meta<typeof ProvenanceGraph>;

export default meta;

type Story = StoryObj<typeof meta>;

const extraction = 'assays/Extraction/';
const extractionProcess = 'assays/Extraction/::Extraction';
const inputGroup = `${extractionProcess}#in0`;
const wait = { timeout: 15000 };

// Clicks a node and waits for a node of the next view.
async function open(canvas: ReturnType<typeof within>, nodeId: string, nextId: string) {
  await userEvent.click(await canvas.findByTestId(`graph-node-${nodeId}`, {}, wait));
  await canvas.findByTestId(`graph-node-${nextId}`, {}, wait);
}

async function openLineage(canvas: ReturnType<typeof within>) {
  await canvas.findByTestId(`graph-node-${extraction}`, {}, wait);
  await open(canvas, extraction, extractionProcess);
  await open(canvas, extractionProcess, inputGroup);
  await open(canvas, inputGroup, 'leaf-1');
  await open(canvas, 'leaf-1', 'data/m1.raw');
}

export const Datasets: Story = {
  decorators: [box('100%', 560)],
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await canvas.findByTestId(`graph-node-${extraction}`, {}, wait);
    await expect(canvas.getByTestId('provenance-crumb-0')).toHaveAttribute('aria-current', 'page');

    // The legend swatches resolve to a color of their own, so the palette reaches them.
    const swatches = canvas.getAllByTestId('provenance-legend-swatch');
    const first = getComputedStyle(swatches[0]);
    await expect(first.backgroundColor).not.toBe('');
    await expect(first.backgroundColor).not.toBe('rgba(0, 0, 0, 0)');
  },
};

// Click through all five levels, then return to the datasets with the first crumb.
export const DrillDown: Story = {
  decorators: [box('100%', 640)],
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await openLineage(canvas);
    await waitFor(() => expect(canvas.getAllByTestId(/^provenance-crumb-\d+$/)).toHaveLength(5));
    await expect(canvas.queryByTestId('provenance-hint')).not.toBeInTheDocument();

    await userEvent.click(canvas.getByTestId('provenance-crumb-0'));
    await canvas.findByTestId(`graph-node-${extraction}`, {}, wait);
    await expect(canvas.getAllByTestId(/^provenance-crumb-\d+$/)).toHaveLength(1);
  },
};

// A box taller than it is wide gets the compact layout of the canvas.
export const SmallBox: Story = {
  decorators: [box(360, 640)],
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await canvas.findByTestId(`graph-node-${extraction}`, {}, wait);
    await waitFor(() => expect(canvas.getByTestId('graph-canvas-content')).toHaveAttribute('data-compact', 'true'), wait);
  },
};

// At five crumbs the narrow header keeps the first crumb and the last two and moves the others into a menu.
export const NarrowHeader: Story = {
  decorators: [box(320, 640)],
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await openLineage(canvas);
    await waitFor(() => expect(canvas.getByTestId('provenance-crumb-menu')).toBeInTheDocument(), wait);
    await expect(canvas.queryByTestId('provenance-crumb-1')).not.toBeInTheDocument();
    await expect(canvas.getByTestId('provenance-crumb-4')).toHaveAttribute('aria-current', 'page');
  },
};

// Every given item of the header has a width of its own and lies inside the header, and the header does not
// scroll sideways. The crumbs carry their full text in the title, because the narrow header cuts them.
async function expectHeaderFits(canvas: ReturnType<typeof within>, canvasElement: HTMLElement, ids: string[]) {
  const header = canvasElement.querySelector('header') as HTMLElement;
  const nav = header.querySelector('nav') as HTMLElement;

  await waitFor(() => {
    const headerBox = header.getBoundingClientRect();

    for (const id of ids) {
      const item = canvas.getByTestId(id);
      const itemBox = item.getBoundingClientRect();

      expect(itemBox.width).toBeGreaterThan(8);
      expect(itemBox.left).toBeGreaterThanOrEqual(headerBox.left - 1);
      expect(itemBox.right).toBeLessThanOrEqual(headerBox.right + 1);
      if (id !== 'provenance-crumb-menu') expect(item).toHaveAttribute('title');
    }

    expect(nav.scrollWidth).toBeLessThanOrEqual(nav.clientWidth + 1);
    expect(header.scrollWidth).toBeLessThanOrEqual(header.clientWidth + 1);
  }, wait);
}

// A header of about 180px with three crumbs. Every crumb cuts its text with an ellipsis and keeps a width of
// its own, and nothing sticks out of the header. The up button hides here, and the overview hides with its toggle.
export const VeryNarrowHeader: Story = {
  decorators: [box(180, 480)],
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await canvas.findByTestId(`graph-node-${extraction}`, {}, wait);
    await open(canvas, extraction, extractionProcess);
    await open(canvas, extractionProcess, inputGroup);

    await waitFor(() => expect(canvas.getAllByTestId(/^provenance-crumb-\d+$/)).toHaveLength(3), wait);
    await expect(canvas.queryByTestId('provenance-crumb-menu')).not.toBeInTheDocument();
    await expectHeaderFits(canvas, canvasElement, ['provenance-crumb-0', 'provenance-crumb-1', 'provenance-crumb-2']);
    // Below the width where the toggle hides, the overview hides too, because it could not be collapsed.
    await expect(canvas.getByTestId('provenance-overview')).not.toBeVisible();
    await expect(canvas.getByTestId('provenance-overview-toggle')).not.toBeVisible();
  },
};

// Five crumbs in a header of about 200px: the first crumb, the menu and the last two just fit. The minimum widths
// of the four items add up to about 165px, so a narrower header lets the last crumb into the padding of the header.
export const VeryNarrowHeaderWithMenu: Story = {
  decorators: [box(200, 480)],
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await openLineage(canvas);
    await waitFor(() => expect(canvas.getByTestId('provenance-crumb-menu')).toBeInTheDocument(), wait);
    await expectHeaderFits(canvas, canvasElement, [
      'provenance-crumb-0',
      'provenance-crumb-menu',
      'provenance-crumb-3',
      'provenance-crumb-4',
    ]);
  },
};

// A header between 16rem and 28rem wide shows the arrow of the up button without its label. The name stays in the
// aria label and the title. The crumbs and both header buttons fit.
export const NarrowHeaderUpIcon: Story = {
  decorators: [box(360, 480)],
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await canvas.findByTestId(`graph-node-${extraction}`, {}, wait);
    await open(canvas, extraction, extractionProcess);
    await open(canvas, extractionProcess, inputGroup);

    const up = await canvas.findByTestId('provenance-up');
    await expect(up).toBeVisible();
    await expect(canvas.getByTestId('provenance-overview')).toBeVisible();
    await expect(up).toHaveAttribute('aria-label');
    await expect(up.querySelector('span')).not.toBeVisible();
    await expectHeaderFits(canvas, canvasElement, [
      'provenance-up',
      'provenance-crumb-0',
      'provenance-crumb-1',
      'provenance-crumb-2',
      'provenance-overview-toggle',
    ]);
  },
};

// The rows of the overview wrap when they are wider than the panel, so every control stays inside the panel and the
// panel does not scroll sideways.
export const WideOverviewRows: Story = {
  decorators: [box(360, 560)],
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await openLineage(canvas);
    const overview = await canvas.findByTestId('provenance-overview');
    await expect(overview).toBeVisible();

    await waitFor(() => {
      const panel = overview.getBoundingClientRect();
      const controls = Array.from(overview.querySelectorAll('button'));

      expect(controls.length).toBeGreaterThan(0);
      for (const control of controls) {
        expect(control.getBoundingClientRect().right).toBeLessThanOrEqual(panel.right + 1);
      }
      expect(overview.scrollWidth).toBeLessThanOrEqual(overview.clientWidth + 1);
    }, wait);
  },
};

// The chips of the first overview row hold still while the user navigates. After a click into a deeper level and a
// click on a sibling chip, every chip that stays in the row keeps its left edge and its width. A chip has the same
// width as the selected chip and as an unselected one, because the pin sits over the corner of the chip and takes no
// inline space. The pin lies inside the panel, which clips what sticks out of its top.
export const OverviewHoldsStill: Story = {
  decorators: [box('100%', 640)],
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    const chipBoxes = () => {
      const chips = Array.from(
        canvasElement.querySelectorAll('[data-testid="provenance-overview-row-0"] [data-testid="provenance-overview-item"]'),
      );
      return new Map(
        chips.map((chip) => {
          const rect = chip.getBoundingClientRect();
          return [chip.getAttribute('title') as string, { left: rect.left, width: rect.width }] as const;
        }),
      );
    };

    const expectSameBoxes = (before: ReturnType<typeof chipBoxes>) => {
      const after = chipBoxes();
      expect(after.size).toBeGreaterThan(1);
      for (const [title, rect] of after) {
        const old = before.get(title);
        if (!old) continue;
        expect(Math.abs(rect.left - old.left)).toBeLessThanOrEqual(0.5);
        expect(Math.abs(rect.width - old.width)).toBeLessThanOrEqual(0.5);
      }
    };

    await canvas.findByTestId(`graph-node-${extraction}`, {}, wait);
    await open(canvas, extraction, extractionProcess);

    const overview = await canvas.findByTestId('provenance-overview');
    const before = chipBoxes();
    expect(before.size).toBeGreaterThan(2);

    const pin = canvas.getByTestId('provenance-overview-location');
    await expect(pin).toBeVisible();
    await expect(getComputedStyle(pin).position).toBe('absolute');
    await expect(getComputedStyle(pin).pointerEvents).toBe('none');
    await expect(pin.getBoundingClientRect().top).toBeGreaterThanOrEqual(overview.getBoundingClientRect().top);

    // A click into a deeper level turns the selected chip of the first row from the pin chip into a button.
    await open(canvas, extractionProcess, inputGroup);
    await waitFor(() => expectSameBoxes(before), wait);

    // A click on a sibling chip makes it the selected one and the old one an unselected button.
    const sibling = canvasElement.querySelector(
      '[data-testid="provenance-overview-row-0"] [data-testid="provenance-overview-item"][data-selected="false"]',
    ) as HTMLElement;
    const title = sibling.getAttribute('title') as string;
    await userEvent.click(sibling);
    await waitFor(() => expect(canvas.getByTestId('provenance-crumb-1')).toHaveTextContent(title), wait);
    await waitFor(() => expectSameBoxes(before), wait);
  },
};

// A work in progress ARC shows how many rows are unfinished. A table with a protocol only has nothing to draw.
export const WorkInProgress: Story = {
  args: { index: wipIndex },
  decorators: [box('100%', 560)],
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const draft = 'assays/Draft/';
    const draftExtraction = 'assays/Draft/::Extraction';
    const draftPlanned = 'assays/Draft/::Planned';

    await userEvent.click(await canvas.findByTestId(`graph-node-${draft}`, {}, wait));
    await canvas.findByTestId(`graph-node-${draftExtraction}`, {}, wait);

    const note = await canvas.findByTestId('provenance-incomplete', {}, wait);
    await expect(note).toHaveAttribute('data-missing-outputs', '4');
    await expect(note).toHaveAttribute('data-missing-inputs', '3');

    // The planned table has no groups, so the message stands in for the canvas and the note stays.
    await userEvent.click(canvas.getByTestId(`graph-node-${draftPlanned}`));
    await waitFor(() => expect(canvas.getByTestId('provenance-empty')).toHaveAttribute('data-empty', 'no-groups'), wait);
    await expect(canvas.getByTestId('provenance-incomplete')).toHaveAttribute('data-missing-outputs', '2');
    await expect(canvas.getByTestId('provenance-incomplete')).toHaveAttribute('data-missing-inputs', '2');
  },
};

export const EmptyArc: Story = {
  args: { index: emptyIndex },
  decorators: [box('100%', 320)],
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await expect(canvas.getByTestId('provenance-empty')).toHaveAttribute('data-empty', 'no-datasets');
    await expect(canvas.getByTestId('provenance-legend')).toBeInTheDocument();
  },
};
