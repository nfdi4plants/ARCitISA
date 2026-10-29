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

export const Default: Story = {
  render: () => <Sample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    expect(canvas.getAllByText('Columns: 5/5 selected')).toHaveLength(3);

    await userEvent.click(canvas.getAllByRole('checkbox')[0]);
    expect(canvas.getAllByText('Columns: 5/5 selected')).toHaveLength(2);
    expect(canvas.getByText('Columns: 4/5 selected')).toBeInTheDocument();

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
