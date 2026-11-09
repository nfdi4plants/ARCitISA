import type { Meta, StoryObj } from '@storybook/react-vite';
import { within, expect, userEvent, waitFor, screen } from 'storybook/test';
import {
  SingleEntry,
  QueuedEntry,
  BatchEntry,
  ScopedQueueEntry,
  ProviderScopedQueueEntry,
  LongDetailsEntry,
} from "./Provider.fs.js";

const meta = {
  title: "Primitive Components/ErrorModal",
  tags: ["autodocs"],
  parameters: {
    layout: 'centered',
    viewport: { defaultViewport: 'responsive' }
  },
  component: SingleEntry,
} satisfies Meta<typeof SingleEntry>;

export default meta;

type Story = StoryObj<typeof meta>;

export const SingleError: Story = {
  render: () => <SingleEntry />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    expect(canvas.queryByRole("button", { name: /queue errors/i })).not.toBeInTheDocument();
    expect(canvas.queryByRole("button", { name: /show multiple errors/i })).not.toBeInTheDocument();

    await userEvent.click(canvas.getByRole("button", { name: /show error/i }));

    expect(await screen.findByText("Sample runtime error")).toBeInTheDocument();
    expect(screen.getByText("The renderer could not finish the requested operation.")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /^ok$/i }));

    await waitFor(() => {
      expect(screen.queryByText("Sample runtime error")).not.toBeInTheDocument();
    });
  }
};

export const QueuedErrors: Story = {
  render: () => <QueuedEntry />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    expect(canvas.queryByRole("button", { name: /show error/i })).not.toBeInTheDocument();
    expect(canvas.queryByRole("button", { name: /show multiple errors/i })).not.toBeInTheDocument();

    await userEvent.click(canvas.getByRole("button", { name: /queue errors/i }));

    expect(await screen.findByText("Queued error 1")).toBeInTheDocument();
    expect(screen.getByText("There are 1 more error modal(s) waiting.")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /^dismiss related errors$/i }));

    await waitFor(() => {
      expect(screen.queryByText("Queued error 1")).not.toBeInTheDocument();
    });
  }
};

export const MultipleErrorsAtOnce: Story = {
  render: () => <BatchEntry />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    expect(canvas.queryByRole("button", { name: /show error/i })).not.toBeInTheDocument();
    expect(canvas.queryByRole("button", { name: /queue errors/i })).not.toBeInTheDocument();

    await userEvent.click(canvas.getByRole("button", { name: /show multiple errors/i }));

    expect(await screen.findByText("Multiple errors at once")).toBeInTheDocument();
    expect(screen.getByText("Visible error 1")).toBeInTheDocument();
    expect(screen.getByText("Visible error 2")).toBeInTheDocument();
    expect(screen.getByText("Visible error 3")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /dismiss error 2/i }));

    await waitFor(() => {
      expect(screen.queryByText("Visible error 2")).not.toBeInTheDocument();
    });

    expect(screen.getByText("Visible error 1")).toBeInTheDocument();
    expect(screen.getByText("Visible error 3")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /dismiss visible errors/i }));

    await waitFor(() => {
      expect(screen.queryByText("Multiple errors at once")).not.toBeInTheDocument();
    });
  }
};

export const ScopedDismissKeepsOtherArcEntries: Story = {
  render: () => <ScopedQueueEntry />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await userEvent.click(canvas.getByRole("button", { name: /queue scoped errors/i }));

    expect(await screen.findByText("ARC A error")).toBeInTheDocument();
    expect(screen.getByText("There are 2 more error modal(s) waiting.")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /^dismiss related errors$/i }));

    await waitFor(() => {
      expect(screen.queryByText("ARC A error")).not.toBeInTheDocument();
      expect(screen.queryByText("ARC A follow-up error")).not.toBeInTheDocument();
    });

    expect(await screen.findByText("ARC B error")).toBeInTheDocument();
  }
};

export const ProviderScopeDismissKeepsExplicitOtherArcEntries: Story = {
  render: () => <ProviderScopedQueueEntry />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await userEvent.click(canvas.getByRole("button", { name: /queue provider scoped errors/i }));

    expect(await screen.findByText("Provider ARC error")).toBeInTheDocument();
    expect(screen.getByText("There are 2 more error modal(s) waiting.")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /^dismiss related errors$/i }));

    await waitFor(() => {
      expect(screen.queryByText("Provider ARC error")).not.toBeInTheDocument();
      expect(screen.queryByText("Provider ARC follow-up error")).not.toBeInTheDocument();
    });

    expect(await screen.findByText("Explicit other ARC error")).toBeInTheDocument();
  }
};

export const LongTechnicalDetailsScroll: Story = {
  render: () => <LongDetailsEntry />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await userEvent.click(canvas.getByRole("button"));

    const dialog = await screen.findByRole("dialog");
    const details = dialog.querySelector("details");
    expect(details).not.toBeNull();
    expect(details!.open).toBe(false);

    await userEvent.click(details!.querySelector("summary")!);

    await waitFor(() => {
      expect(details!.open).toBe(true);
    });

    const pre = details!.querySelector("pre")!;

    await waitFor(() => {
      expect(pre.scrollHeight).toBeGreaterThan(pre.clientHeight);
    });

    // The block is only scrollable when the browser moves it.
    pre.scrollTop = pre.scrollHeight;
    expect(pre.scrollTop).toBeGreaterThan(0);
  }
};
