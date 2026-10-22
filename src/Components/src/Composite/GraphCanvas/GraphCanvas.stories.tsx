import React from 'react';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, fn, userEvent, waitFor, within } from 'storybook/test';
import { GraphCanvas } from './GraphCanvas.fs.js';
import { svgMarkup } from './ImageExport.fs.js';
import type { CanvasEdge, CanvasNode, CanvasPill } from './Types.fs.js';

// The Fable records are classes in the generated types. The stories build plain objects with the same fields,
// and an absent option is undefined.
type PillInit = {
  key: string;
  kind: 'property' | 'process' | 'count';
  name?: string;
  value?: string;
  category?: 'characteristic' | 'factor' | 'parameter' | 'component';
  tooltip?: string;
};

function pill(init: PillInit): CanvasPill {
  return {
    Key: init.key,
    Kind: init.kind,
    Name: init.name,
    Value: init.value,
    Unit: undefined,
    Category: init.category,
    Tooltip: init.tooltip,
  } as unknown as CanvasPill;
}

function node(id: string, title: string, color: 'study' | 'assay' | 'run', subtitle?: string, pills: CanvasPill[] = []): CanvasNode {
  return {
    Id: id,
    Title: title,
    Subtitle: subtitle,
    Color: color,
    Dashed: false,
    Mono: false,
    Selected: false,
    Emphasized: false,
    Pills: pills,
    Tooltip: undefined,
    AriaLabel: `${title}, select to open`,
    Clickable: true,
  } as unknown as CanvasNode;
}

function edge(
  id: string,
  from: string,
  to: string,
  color: 'study' | 'assay' | 'run' | 'neutral',
  pills: CanvasPill[],
  morePills: CanvasPill[] = [],
): CanvasEdge {
  return {
    Id: id,
    From: from,
    To: to,
    Color: color,
    Highlighted: false,
    Pills: pills,
    MorePills: morePills,
    Tooltip: undefined,
  } as unknown as CanvasEdge;
}

// The arrays are module constants. The canvas lays out again when nodes or edges change identity.
const nodes: CanvasNode[] = [
  node('study', 'Plant growth study', 'study', 'Drought experiment'),
  node('assay', 'Metabolite profiling', 'assay', undefined, [
    pill({ key: 'species', kind: 'property', name: 'species', value: 'Arabidopsis thaliana', category: 'characteristic' }),
  ]),
  node('run-1', 'LC-MS run 1', 'run'),
  { ...node('run-2', 'LC-MS run 2', 'run'), Dashed: true } as CanvasNode,
];

const edges: CanvasEdge[] = [
  edge(
    'study-assay',
    'study',
    'assay',
    'study',
    [pill({ key: 'temperature', kind: 'property', name: 'temperature', value: '20 C', category: 'parameter' })],
    [
      pill({ key: 'duration', kind: 'property', name: 'duration', value: '2 h', category: 'parameter' }),
      pill({ key: 'volume', kind: 'property', name: 'volume', value: '5 ml', category: 'parameter' }),
    ],
  ),
  edge('assay-run-1', 'assay', 'run-1', 'assay', [pill({ key: 'protocol', kind: 'process', value: 'Gas chromatography' })]),
  edge('assay-run-2', 'assay', 'run-2', 'neutral', [pill({ key: 'count', kind: 'count', value: '3', tooltip: '3 shared samples' })]),
];

const meta = {
  title: 'Composite Components/GraphCanvas',
  tags: ['autodocs'],
  parameters: {
    layout: 'fullscreen',
  },
  args: {
    nodes,
    edges,
    ariaLabel: 'Provenance graph',
    resetKey: 'story',
    onNodeClick: fn(),
  },
  component: GraphCanvas,
} satisfies Meta<typeof GraphCanvas>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
  decorators: [
    (Story) => (
      <div style={{ width: '100%', height: 480 }}>
        <Story />
      </div>
    ),
  ],
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);

    const study = await canvas.findByTestId('graph-node-study', {}, { timeout: 10000 });
    await userEvent.click(study);
    await waitFor(() => expect(args.onNodeClick).toHaveBeenCalledWith('study'));

    const toggle = await canvas.findByTestId('graph-edge-toggle-study-assay');
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await userEvent.click(toggle);
    await waitFor(() => expect(canvas.getByTestId('graph-edge-toggle-study-assay')).toHaveAttribute('aria-expanded', 'true'));
  },
};

// A box that is taller than it is wide gets the compact layout: top to bottom with one chip per edge.
export const Compact: Story = {
  decorators: [
    (Story) => (
      <div style={{ width: 360, height: 760 }}>
        <Story />
      </div>
    ),
  ],
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await waitFor(() => expect(canvas.getByTestId('graph-canvas-content')).toHaveAttribute('data-compact', 'true'), {
      timeout: 10000,
    });
  },
};

// Six rows of five nodes. The graph is larger than the 480 by 320 px box in both directions, so the minimap appears.
const gridNodes: CanvasNode[] = [];
const gridEdges: CanvasEdge[] = [];

for (let row = 0; row < 6; row++) {
  for (let col = 0; col < 5; col++) {
    gridNodes.push(node(`n-${row}-${col}`, `Step ${row + 1}.${col + 1}`, col === 0 ? 'study' : col === 4 ? 'run' : 'assay'));
    if (col > 0) {
      gridEdges.push(
        edge(`e-${row}-${col}`, `n-${row}-${col - 1}`, `n-${row}-${col}`, 'neutral', [pill({ key: 'count', kind: 'count', value: String(col) })]),
      );
    }
  }
}

export const Minimap: Story = {
  args: {
    nodes: gridNodes,
    edges: gridEdges,
    resetKey: 'minimap',
  },
  decorators: [
    (Story) => (
      <div style={{ width: 480, height: 320 }}>
        <Story />
      </div>
    ),
  ],
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    const minimap = await canvas.findByTestId('graph-minimap', {}, { timeout: 10000 });
    // One rect per node and one for the visible part.
    await waitFor(() => expect(minimap.querySelectorAll('rect')).toHaveLength(gridNodes.length + 1));

    const root = canvas.getByTestId('graph-canvas');
    const svg = minimap.querySelector('svg')!;
    const area = svg.getBoundingClientRect();
    svg.dispatchEvent(
      new PointerEvent('pointerdown', {
        bubbles: true,
        cancelable: true,
        pointerId: 1,
        pointerType: 'mouse',
        isPrimary: true,
        button: 0,
        buttons: 1,
        clientX: area.left + area.width * 0.9,
        clientY: area.top + area.height * 0.9,
      }),
    );

    await waitFor(() => expect(root.scrollLeft > 0 || root.scrollTop > 0).toBe(true));
  },
};

// The RGBA bytes that a CSS color resolves to. A canvas converts every color syntax that the browser knows.
function rgbaOf(color: string): number[] {
  const surface = document.createElement('canvas');
  surface.width = surface.height = 1;
  const context = surface.getContext('2d')!;
  context.fillStyle = color;
  context.fillRect(0, 0, 1, 1);
  return Array.from(context.getImageData(0, 0, 1, 1).data);
}

// The computed base-100 color, read through a probe element inside the root as ImageExport.svgMarkup reads it.
function base100Of(root: HTMLElement): string {
  const probe = document.createElement('span');
  probe.style.color = 'var(--color-base-100)';
  root.appendChild(probe);
  const color = getComputedStyle(probe).color;
  probe.remove();
  return color;
}

// The image holds the whole graph, also the nodes that are scrolled out of the 480 by 320 px box.
export const ImageExport: Story = {
  args: {
    nodes: gridNodes,
    edges: gridEdges,
    resetKey: 'image-export',
    imageFileName: 'graph.png',
  },
  decorators: [
    (Story) => (
      <div style={{ width: 480, height: 320 }}>
        <Story />
      </div>
    ),
  ],
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    const download = await canvas.findByTestId('graph-image-download', {}, { timeout: 10000 });
    const root = canvas.getByTestId('graph-canvas');
    const mainSvg = canvas.getByTestId('graph-canvas-content').querySelector('svg')!;

    // The layout size is the viewBox of the main svg.
    const viewBox = mainSvg.getAttribute('viewBox')!.split(' ').map(Number);
    const [layoutWidth, layoutHeight] = [viewBox[2], viewBox[3]];

    // The export must not depend on what is visible, so the canvas is zoomed in and scrolled to the middle first.
    await userEvent.click(canvas.getByTestId('graph-zoom-in'));
    await waitFor(() => expect(Number(mainSvg.getAttribute('width'))).toBeGreaterThan(layoutWidth));
    root.scrollLeft = (root.scrollWidth - root.clientWidth) / 2;
    root.scrollTop = (root.scrollHeight - root.clientHeight) / 2;

    const rootBox = root.getBoundingClientRect();
    const hidden = canvas.getAllByTestId(/^graph-node-/).find((node) => {
      const box = node.getBoundingClientRect();
      return box.right <= rootBox.left || box.left >= rootBox.right || box.bottom <= rootBox.top || box.top >= rootBox.bottom;
    });
    expect(hidden).toBeDefined();

    // The markup is a standalone copy of the drawing: computed colors only, nothing interactive, the background first.
    const background = base100Of(root);
    const markup = svgMarkup(mainSvg, root, layoutWidth, layoutHeight);
    for (const forbidden of ['var(', 'color-mix(', ' class=', ' tabindex=', ' role=', ' aria-', ' data-testid=', '<title']) {
      expect(markup).not.toContain(forbidden);
    }
    const copy = new DOMParser().parseFromString(markup, 'image/svg+xml').documentElement;
    expect(copy.localName).toBe('svg');
    expect(Number(copy.getAttribute('width'))).toBe(layoutWidth);
    expect(Number(copy.getAttribute('height'))).toBe(layoutHeight);
    expect(copy.getAttribute('viewBox')).toBe(`0 0 ${layoutWidth} ${layoutHeight}`);
    expect(copy.firstElementChild!.localName).toBe('rect');
    expect(copy.firstElementChild!.getAttribute('fill')).toBe(background);

    // The click on the anchor is captured, so the story does not navigate or open a save dialog.
    const originalClick = HTMLAnchorElement.prototype.click;
    let saved: { download: string; href: string } | undefined;
    HTMLAnchorElement.prototype.click = function (this: HTMLAnchorElement) {
      saved = { download: this.download, href: this.href };
    };

    try {
      await userEvent.click(download);
      await waitFor(() => expect(saved).toBeDefined(), { timeout: 10000 });
    } finally {
      HTMLAnchorElement.prototype.click = originalClick;
    }

    expect(saved!.download).toBe('graph.png');
    const png = await (await fetch(saved!.href)).blob();
    expect(png.type).toBe('image/png');
    const bitmap = await createImageBitmap(png);

    // The image has the size of the layout at the factor 2, which this small graph gets, and keeps its proportions.
    expect(Math.abs(bitmap.width - 2 * layoutWidth)).toBeLessThan(1);
    expect(Math.abs(bitmap.height - 2 * layoutHeight)).toBeLessThan(1);
    expect(Math.abs((bitmap.height * layoutWidth) / layoutHeight - bitmap.width)).toBeLessThanOrEqual(1 + layoutWidth / layoutHeight);

    const surface = document.createElement('canvas');
    surface.width = bitmap.width;
    surface.height = bitmap.height;
    const context = surface.getContext('2d', { willReadFrequently: true })!;
    context.drawImage(bitmap, 0, 0);
    bitmap.close();
    const pixelAt = (x: number, y: number) => Array.from(context.getImageData(Math.floor(x), Math.floor(y), 1, 1).data);

    // The top left corner shows the background of the page.
    const corner = pixelAt(0, 0);
    const expectedBackground = rgbaOf(background);
    corner.forEach((channel, index) => expect(Math.abs(channel - expectedBackground[index])).toBeLessThanOrEqual(1));

    // A node that is outside the visible area is in the image. Its colored stripe, at the middle of its height,
    // differs from the background.
    const [x, y] = hidden!.getAttribute('transform')!.match(/-?[\d.]+/g)!.map(Number);
    const nodeHeight = Number(hidden!.querySelector('rect')!.getAttribute('height'));
    expect(pixelAt((x + 2) * 2, (y + nodeHeight / 2) * 2).slice(0, 3)).not.toEqual(corner.slice(0, 3));

    // The copy button needs a secure context and ClipboardItem. Where the browser has them, the button must be there.
    const copyButton = canvas.queryByTestId('graph-image-copy');
    if (window.isSecureContext) {
      expect(copyButton).not.toBeNull();
    }
    if (!copyButton) {
      return;
    }

    await waitFor(() => expect(copyButton).toHaveAttribute('aria-disabled', 'false'));
    let item: ClipboardItem | undefined;
    Object.defineProperty(navigator.clipboard, 'write', {
      configurable: true,
      value: async (items: ClipboardItem[]) => {
        item = items[0];
      },
    });

    try {
      copyButton.click();
      // write runs inside the click handler, before the image exists. A wait here would hide a lost user action.
      expect(item).toBeDefined();
    } finally {
      // The stub is an own property of the instance, so deleting it brings back the method of the prototype.
      delete (navigator.clipboard as { write?: unknown }).write;
    }

    const copied = await item!.getType('image/png');
    expect(copied.type).toBe('image/png');
    expect(copied.size).toBeGreaterThan(0);
  },
};
