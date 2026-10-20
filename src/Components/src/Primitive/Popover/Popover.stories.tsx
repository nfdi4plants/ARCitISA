import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, screen, userEvent, waitFor, within } from "storybook/test";
import React, { useState } from "react";
import {
  Popover,
  Trigger,
  TriggerRender,
  Content,
  Heading,
  Description,
  Close,
  Simple,
} from "./Popover.fs.js";
// NOTE: Fable dev mode (--lang ts) generates `.fs.ts` files colocated with
// source. The `.fs.js` extension in the import is resolved by Vite's
// TypeScript handling — no literal `.fs.js` file is created during dev.

const TESTID_CUSTOM_TRIGGER = "popover_custom_trigger";
const TESTID_MODAL_PRIMARY_ACTION = "popover_modal_primary_action";

const BasicPopoverExample = () => (
  <Popover debug="basic">
    <Trigger>Open details</Trigger>
    <Content>
      <Heading>Dataset actions</Heading>
      <Description>Choose what should happen to this dataset.</Description>
      <button className="swt:btn swt:btn-sm">Keep open</button>
      <Close>Close</Close>
    </Content>
  </Popover>
);

const ControlledPopoverExample = () => {
  const [open, setOpen] = useState(false);

  return (
    <div className="swt:flex swt:flex-col swt:items-start swt:gap-3">
      <button className="swt:btn swt:btn-primary" onClick={() => setOpen(true)}>
        Open from outside
      </button>
      <Popover isOpen={open} onOpenChange={setOpen} debug="controlled">
        <Trigger>Toggle controlled</Trigger>
        <Content>
          <Heading>Controlled popover</Heading>
          <Description>The host owns the open state.</Description>
          <Close>Done</Close>
        </Content>
      </Popover>
    </div>
  );
};

const CustomTriggerExample = () => (
  <Popover debug="custom-trigger">
    <TriggerRender
      render={({ isOpen, setReference, referenceProps }) => (
        <button
          ref={setReference}
          data-state={isOpen ? "open" : "closed"}
          data-testid={TESTID_CUSTOM_TRIGGER}
          className="swt:btn swt:btn-secondary"
          {...referenceProps}
        >
          Custom trigger
        </button>
      )}
    />
    <Content>
      <Heading>Custom trigger popover</Heading>
      <Description>The trigger applied the Floating UI props itself.</Description>
      <Close>Close custom</Close>
    </Content>
  </Popover>
);

const NonModalPopoverExample = () => (
  <div className="swt:flex swt:items-center swt:gap-4">
    <Popover modal={false} debug="non-modal">
      <Trigger>Open non-modal</Trigger>
      <Content>
        <Heading>Non-modal popover</Heading>
        <Description>Focus can move outside this popover.</Description>
        <button className="swt:btn swt:btn-sm">Focusable content</button>
      </Content>
    </Popover>
    <button className="swt:btn" type="button">
      Outside target
    </button>
  </div>
);

const ModalPopoverExample = () => (
  <div className="swt:flex swt:flex-col swt:items-start swt:gap-4">
    <button className="swt:btn" type="button">
      Before target
    </button>
    <Popover modal debug="modal">
      <Trigger>Open modal</Trigger>
      <Content>
        <Heading>Modal popover</Heading>
        <Description>Focus stays inside this popover until it closes.</Description>
        <button className="swt:btn swt:btn-sm" data-testid={TESTID_MODAL_PRIMARY_ACTION}>
          First action
        </button>
        <Close>Close modal</Close>
      </Content>
    </Popover>
    <button className="swt:btn" type="button">
      After target
    </button>
  </div>
);

const DuplicateHeadingPopoverExample = () => (
  <Popover debug="duplicate-heading">
    <Trigger>Open duplicate heading</Trigger>
    <Content>
      <Heading>Primary heading</Heading>
      <Heading>Secondary heading</Heading>
      <Description>Only the first heading should label the dialog.</Description>
      <Close>Close duplicate</Close>
    </Content>
  </Popover>
);

const BareContentPopoverExample = () => (
  <Popover debug="bare">
    <Trigger>Open plain</Trigger>
    <Content ariaLabel="Plain actions">
      <button className="swt:btn swt:btn-sm">Only action</button>
    </Content>
  </Popover>
);

const meta = {
  title: "Primitive Components/Popover",
  tags: ["autodocs"],
  parameters: {
    layout: "centered",
    viewport: { defaultViewport: "responsive" },
  },
  component: BasicPopoverExample,
} satisfies Meta<typeof BasicPopoverExample>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Basic: Story = {
  render: () => <BasicPopoverExample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const trigger = canvas.getByTestId("popover_trigger_basic");

    expect(trigger).toHaveAttribute("data-state", "closed");
    await userEvent.click(trigger);

    const content = await screen.findByTestId("popover_content_basic");
    const dialog = await screen.findByRole("dialog", { name: /dataset actions/i });
    expect(content).toBe(dialog);
    expect(dialog).toHaveAttribute("data-state", "open");
    await waitFor(() => expect(dialog).toHaveAttribute("data-status", "open"));
    expect(screen.getByText(/choose what should happen/i)).toBeInTheDocument();

    await userEvent.keyboard("{Escape}");
    await waitFor(() =>
      expect(screen.queryByRole("dialog", { name: /dataset actions/i })).not.toBeInTheDocument(),
    );
  },
};

export const Controlled: Story = {
  render: () => <ControlledPopoverExample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(canvas.getByRole("button", { name: /open from outside/i }));

    const dialog = await screen.findByRole("dialog", { name: /controlled popover/i });
    expect(dialog).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /done/i }));
    await waitFor(() =>
      expect(screen.queryByRole("dialog", { name: /controlled popover/i })).not.toBeInTheDocument(),
    );
  },
};

export const CustomTrigger: Story = {
  render: () => <CustomTriggerExample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const trigger = canvas.getByTestId(TESTID_CUSTOM_TRIGGER);

    expect(trigger).toHaveAttribute("data-state", "closed");
    await userEvent.click(trigger);
    expect(trigger).toHaveAttribute("data-state", "open");
    expect(await screen.findByRole("dialog", { name: /custom trigger popover/i })).toBeInTheDocument();
  },
};

export const NonModal: Story = {
  render: () => <NonModalPopoverExample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(canvas.getByRole("button", { name: /open non-modal/i }));
    expect(await screen.findByRole("dialog", { name: /non-modal popover/i })).toBeInTheDocument();

    await userEvent.click(canvas.getByRole("button", { name: /outside target/i }));
    await waitFor(() =>
      expect(screen.queryByRole("dialog", { name: /non-modal popover/i })).not.toBeInTheDocument(),
    );
  },
};

export const ModalFocus: Story = {
  render: () => <ModalPopoverExample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const trigger = canvas.getByRole("button", { name: /open modal/i });
    const afterTarget = canvas.getByRole("button", { name: /after target/i });

    trigger.focus();
    expect(trigger).toHaveFocus();

    await userEvent.click(trigger);

    const dialog = await screen.findByTestId("popover_content_modal");
    const firstAction = screen.getByTestId(TESTID_MODAL_PRIMARY_ACTION);
    const closeButton = screen.getByRole("button", { name: /close modal/i });

    await waitFor(() => {
      expect(dialog.contains(document.activeElement)).toBe(true);
    });

    await userEvent.tab();
    expect(dialog.contains(document.activeElement)).toBe(true);

    await userEvent.tab();
    // Tabbing past the last element first lands on a floating-ui focus guard. The focus manager
    // moves focus back into the dialog in the next animation frame, so the test waits for it.
    await waitFor(() => {
      expect(dialog.contains(document.activeElement)).toBe(true);
      expect(document.activeElement === firstAction || document.activeElement === closeButton).toBe(true);
    });
    expect(afterTarget).not.toHaveFocus();

    await userEvent.click(closeButton);

    await waitFor(() =>
      expect(screen.queryByRole("dialog", { name: /modal popover/i })).not.toBeInTheDocument(),
    );
    await waitFor(() => expect(trigger).toHaveFocus());
  },
};

export const DuplicateHeading: Story = {
  render: () => <DuplicateHeadingPopoverExample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(canvas.getByRole("button", { name: /open duplicate heading/i }));

    const dialog = await screen.findByRole("dialog", { name: /primary heading/i });
    const labelId = dialog.getAttribute("aria-labelledby");

    expect(labelId).toBeTruthy();
    expect(document.querySelectorAll(`[id="${labelId}"]`)).toHaveLength(1);
    expect(screen.getByText(/primary heading/i)).toHaveAttribute("id", labelId);
    expect(screen.getByText(/secondary heading/i)).not.toHaveAttribute("id", labelId);
  },
};

export const BareContent: Story = {
  render: () => <BareContentPopoverExample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(canvas.getByTestId("popover_trigger_bare"));

    const dialog = await screen.findByRole("dialog", { name: /plain actions/i });
    expect(dialog).toHaveAttribute("data-testid", "popover_content_bare");
    expect(dialog).toHaveAttribute("aria-label", "Plain actions");
    expect(dialog).not.toHaveAttribute("aria-labelledby");
    expect(screen.getByRole("button", { name: /only action/i })).toBeInTheDocument();
  },
};

const SimplePopoverExample = () => (
  <Simple
    trigger={<>Show info</>}
    debug="simple"
    content={
      <>
        <p className="swt:text-sm">This is the easy-entry popover with a built-in close button.</p>
        <button className="swt:btn swt:btn-sm swt:btn-primary">Action</button>
      </>
    }
  />
);

export const SimpleEntry: Story = {
  render: () => <SimplePopoverExample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(canvas.getByTestId("popover_trigger_simple"));

    const content = await screen.findByTestId("popover_content_simple");
    expect(content).toBeInTheDocument();
    expect(screen.getByText(/easy-entry popover/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /action/i })).toBeInTheDocument();
  },
};

const MissingContextExample = () => (
  <div className="swt:flex swt:gap-4">
    <Trigger>Orphan trigger</Trigger>
    <Content>Orphan content</Content>
    <Close>Orphan close</Close>
  </div>
);

export const MissingContext: Story = {
  render: () => <MissingContextExample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const errors = canvas.getAllByText(/must be used inside/i);
    expect(errors.length).toBeGreaterThanOrEqual(3);
  },
};
