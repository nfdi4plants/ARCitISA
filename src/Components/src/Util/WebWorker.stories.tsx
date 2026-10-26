import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, spyOn, userEvent, waitFor, within } from 'storybook/test';
import { CrashSample, FallbackSample, MountSample, RejectSample, Sample, rejectionMessage } from './WebWorker.sample.fs.js';
import { ProbeWorker } from './WebWorker.test.worker.fs.js';
import { RejectingWorker } from './WebWorker.test.worker.rejecting.fs.js';
import { CrashingWorker } from './WebWorker.test.worker.crashing.fs.js';
import { warmUpWorker } from './WebWorker.test.helpers';

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
  loaders: [() => warmUpWorker(ProbeWorker, undefined)],
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

// The handler rejects with a plain string, and the request rejects with the same text.
export const RejectsWithTheHandlerMessage: Story = {
  loaders: [() => warmUpWorker(RejectingWorker, undefined)],
  render: () => <RejectSample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(canvas.getByTestId('worker-run'));
    await waitFor(() => expect(canvas.getByTestId('worker-result')).toHaveAttribute('data-error', rejectionMessage));
    expect(canvas.getByTestId('worker-result')).toHaveAttribute('data-in-worker', '');
  },
};

// The worker script fails 300 ms after the request arrives, while the handler still waits. The pending request
// runs through the handler on the main thread.
export const RerunsPendingRequestsWhenTheWorkerFails: Story = {
  loaders: [() => warmUpWorker(CrashingWorker, undefined)],
  render: () => <CrashSample />,
  play: async ({ canvasElement }) => {
    await run(within(canvasElement), 'false');
  },
};

export const TerminatesOnUnmount: Story = {
  loaders: [() => warmUpWorker(ProbeWorker, undefined)],
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
