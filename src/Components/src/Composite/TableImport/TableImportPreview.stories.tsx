import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, userEvent, within } from 'storybook/test';
import { Sample, AppendDisabledSample } from './TableImportPreview.sample.fs.js';

const meta = {
  title: 'Composite Components/TableImportPreview',
  component: Sample,
  tags: ['autodocs'],
  parameters: { layout: 'padded' },
} satisfies Meta<typeof Sample>;

export default meta;
type Story = StoryObj<typeof meta>;

/** The opacity the user sees: an element fades with every ancestor that sets one. */
const visibleOpacity = (element: Element | null): number => {
  let opacity = 1;
  for (let current = element; current; current = current.parentElement) {
    opacity *= Number(getComputedStyle(current).opacity);
  }
  return opacity;
};

export const Default: Story = {
  render: () => <Sample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    expect(canvas.getAllByText('Columns: 5/5 selected, rows: 20/20 selected')).toHaveLength(3);

    await userEvent.click(canvasElement.querySelectorAll('details > summary')[0]);
    await userEvent.click((await canvas.findAllByRole('checkbox'))[0]);
    expect(canvas.getAllByText('Columns: 5/5 selected, rows: 20/20 selected')).toHaveLength(2);
    expect(canvas.getByText('Columns: 4/5 selected, rows: 20/20 selected')).toBeInTheDocument();

    await userEvent.click(canvas.getByTestId('table-import-row-0-0'));
    expect(canvas.getByText('Columns: 4/5 selected, rows: 19/20 selected')).toBeInTheDocument();

    // A left-out column or row dims its text. An unchecked box has a faint border, so it stays at full opacity.
    const columnBox = canvas.getByTestId('table-import-column-0-0');
    const rowBox = canvas.getByTestId('table-import-row-0-0');
    expect(visibleOpacity(columnBox)).toBe(1);
    expect(visibleOpacity(rowBox)).toBe(1);
    expect(visibleOpacity(columnBox.nextElementSibling)).toBeLessThan(1);
    expect(visibleOpacity(rowBox.nextElementSibling)).toBeLessThan(1);

    await userEvent.click(canvas.getByTestId('table-import-action-2-NoImport'));
    expect(canvas.getByTestId('table-import-action-2-NoImport')).toBeChecked();
    expect(canvas.getByTestId('table-import-action-0-ImportAsNewTable')).toBeChecked();
  },
};

export const AppendDisabled: Story = {
  render: () => <AppendDisabledSample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    expect(canvas.getByTestId('table-import-action-0-AppendToActiveTable')).toBeDisabled();
  },
};
