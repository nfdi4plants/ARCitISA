import React from 'react';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, fn, userEvent, waitFor, within } from 'storybook/test';
import { GraphCanvas } from './GraphCanvas.fs.js';
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
