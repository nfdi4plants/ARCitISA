import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, spyOn, userEvent, waitFor, within } from 'storybook/test';
import { FallbackSample, MountSample, Sample } from './WebWorker.sample.fs.js';

const meta = {
  title: 'Utilities/WebWorker',
  component: Sample,
  parameters: { layout: 'padded' },
} satisfies Meta<typeof Sample>;

export default meta;
type Story = StoryObj<typeof meta>;

// The samples show the answer of a request in the data-in-worker attribute of the result element.
async function run(canvas: ReturnType<typeof within>, expected: 'true' | 'false') {
  await userEvent.click(canvas.getByTestId('worker-run'));
  await waitFor(() => expect(canvas.getByTestId('worker-result')).toHaveAttribute('data-in-worker', expected));
}

export const RunsInWorker: Story = {
  play: async ({ canvasElement }) => {
    await run(within(canvasElement), 'true');
  },
};

export const FallsBackToMainThread: Story = {
  render: () => <FallbackSample />,
  play: async ({ canvasElement }) => {
    await run(within(canvasElement), 'false');
  },
};

export const TerminatesOnUnmount: Story = {
  render: () => <MountSample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await run(canvas, 'true');

    const terminate = spyOn(Worker.prototype, 'terminate');
    try {
      await userEvent.click(canvas.getByTestId('worker-toggle'));
      await waitFor(() => expect(canvas.queryByTestId('worker-run')).not.toBeInTheDocument());
      expect(terminate).toHaveBeenCalledTimes(1);

      await userEvent.click(canvas.getByTestId('worker-toggle'));
      await run(canvas, 'true');
    } finally {
      terminate.mockRestore();
    }
  },
};
