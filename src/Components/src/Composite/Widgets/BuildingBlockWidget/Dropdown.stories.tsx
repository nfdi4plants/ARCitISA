import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, screen, userEvent, waitFor, within } from "storybook/test";
import React, { useState } from "react";
import { Main as BuildingBlockDropdown } from "./Dropdown.fs.js";
import { CompositeHeaderDiscriminate_Parameter } from "../../../ARCtrl/Types/CompositeHeaderDiscriminate.fs.js";
import { customIOType } from "./Dropdown.sample.fs.js";

type PickerProps = React.ComponentProps<typeof BuildingBlockDropdown>;

const PickerExample = () => {
  const [header, setHeader] = useState<PickerProps["selectedHeaderType"]>(CompositeHeaderDiscriminate_Parameter);
  const [ioType, setIOType] = useState<PickerProps["selectedIOType"]>(customIOType("existing type"));
  const [selection, setSelection] = useState("none");
  const [submits, setSubmits] = useState(0);

  return (
    <form
      className="swt:relative swt:m-2 swt:max-w-full swt:overflow-hidden swt:rounded-box swt:border swt:p-3"
      onSubmit={(event) => {
        event.preventDefault();
        setSubmits((count) => count + 1);
      }}
    >
      <BuildingBlockDropdown
        selectedHeaderType={header}
        selectedIOType={ioType}
        onSelectHeaderType={(nextHeader) => {
          setHeader(nextHeader);
          setSelection(String(nextHeader));
        }}
        onSelectHeaderIOType={(nextHeader, nextIOType) => {
          setHeader(nextHeader);
          setIOType(nextIOType);
          setSelection(`${nextHeader}: ${nextIOType.tag === 4 ? nextIOType.fields[0] : nextIOType}`);
        }}
      />
      <output className="swt:block" aria-label="Selection">{selection}</output>
      <output className="swt:block" aria-label="Form submissions">{submits}</output>
      <div data-testid="outside-target" className="swt:mt-2 swt:h-8">Outside target</div>
    </form>
  );
};

const meta = {
  title: "Composite Components/Widgets/Building Block/Type Picker",
  component: PickerExample,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
} satisfies Meta<typeof PickerExample>;
export default meta;
type Story = StoryObj<typeof meta>;

const openPicker = async (trigger: HTMLElement) => {
  await userEvent.click(trigger);
  return screen.findByRole("dialog", { name: "Building block type" });
};

const summary = (dialog: HTMLElement, title: string) => {
  const node = within(dialog).getByText(title, { selector: "summary" });
  expect(node).toBeVisible();
  return node;
};

const expectClosed = async (trigger: HTMLElement) => {
  await waitFor(() => expect(screen.queryByRole("dialog", { name: "Building block type" })).not.toBeInTheDocument());
  await waitFor(() => expect(trigger).toHaveFocus());
};

export const NativeNavigationAndDismissal: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const trigger = canvas.getByRole("button", { name: "Parameter" });
    expect(trigger).toHaveAttribute("type", "button");
    let dialog = await openPicker(trigger);
    expect(canvasElement.contains(dialog)).toBe(false);
    expect(dialog).not.toHaveAttribute("aria-modal", "true");
    const rootItems = Array.from(dialog.querySelector("ul")!.children)
      .map((item) => item.querySelector(":scope > details > summary, :scope > button, :scope > a")?.textContent)
      .filter(Boolean);
    expect(rootItems).toEqual(["Input", "Parameter", "Factor", "Characteristic", "Component", "More", "Output", "info"]);

    for (const branch of ["Input", "More", "Output"]) {
      const branchSummary = summary(dialog, branch);
      await userEvent.click(branchSummary);
      expect(branchSummary.parentElement).toHaveAttribute("open");
      expect(within(dialog).queryByRole("button", { name: "Factor", exact: true })).not.toBeInTheDocument();
      if (branch === "More") {
        expect(within(dialog).getAllByRole("button").map((button) => button.textContent)).toEqual([
          "Comment", "Date", "Performer", "ProtocolDescription", "ProtocolREF", "ProtocolType", "ProtocolUri", "ProtocolVersion",
        ]);
      } else {
        expect(within(dialog).getByRole("button", { name: "Source Name", exact: true })).toBeVisible();
        expect(within(dialog).getByRole("textbox")).toHaveValue("existing type");
      }
      await userEvent.click(branchSummary);
      expect(branchSummary.parentElement).not.toHaveAttribute("open");
      expect(within(dialog).getByRole("button", { name: "Factor", exact: true })).toBeVisible();
    }

    const inputSummary = summary(dialog, "Input");
    inputSummary.focus();
    await userEvent.click(inputSummary);
    expect(inputSummary.parentElement).toHaveAttribute("open");
    await userEvent.keyboard("{Escape}");
    await expectClosed(trigger);

    dialog = await openPicker(trigger);
    expect(summary(dialog, "Input").parentElement).not.toHaveAttribute("open");
    await userEvent.click(canvas.getByTestId("outside-target"));
    await expectClosed(trigger);

    dialog = await openPicker(trigger);
    await userEvent.click(summary(dialog, "More"));
    await userEvent.click(within(dialog).getByRole("button", { name: "ProtocolREF", exact: true }));
    await expectClosed(trigger);
    expect(canvas.getByLabelText("Selection")).toHaveTextContent("ProtocolREF");
    expect(canvas.getByLabelText("Form submissions")).toHaveTextContent("0");
  },
};

export const CustomTypesAndReopen: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const trigger = canvas.getByRole("button", { name: "Parameter" });
    let dialog = await openPicker(trigger);
    await userEvent.click(summary(dialog, "Input"));
    const textInput = within(dialog).getByRole("textbox", { name: "Custom input or output type" });
    await userEvent.clear(textInput);
    await userEvent.type(textInput, "custom material{Enter}");
    await expectClosed(trigger);
    expect(canvas.getByLabelText("Selection")).toHaveTextContent("Input: custom material");
    expect(canvas.getByLabelText("Form submissions")).toHaveTextContent("0");

    dialog = await openPicker(trigger);
    expect(summary(dialog, "Input").parentElement).not.toHaveAttribute("open");
    await userEvent.click(summary(dialog, "Output"));
    const outputInput = within(dialog).getByRole("textbox");
    expect(outputInput).toHaveValue("custom material");
    await userEvent.clear(outputInput);
    await userEvent.type(outputInput, "custom output");
    await userEvent.click(within(dialog).getByRole("button", { name: "Confirm custom type" }));
    await expectClosed(trigger);
    expect(canvas.getByLabelText("Selection")).toHaveTextContent("Output: custom output");
    expect(canvas.getByLabelText("Form submissions")).toHaveTextContent("0");

    dialog = await openPicker(trigger);
    await userEvent.click(summary(dialog, "Output"));
    await userEvent.click(within(dialog).getByRole("button", { name: "Sample Name", exact: true }));
    await expectClosed(trigger);
    expect(canvas.getByLabelText("Selection")).toHaveTextContent("Output: Sample Name");
  },
};

const expectHitTarget = async (element: HTMLElement) => {
  const doc = element.ownerDocument;
  const view = doc.defaultView!;
  await waitFor(() => {
    const rect = element.getBoundingClientRect();
    expect(rect.width).toBeGreaterThan(0);
    expect(rect.height).toBeGreaterThan(0);
    expect(rect.left).toBeGreaterThanOrEqual(0);
    expect(rect.right).toBeLessThanOrEqual(view.innerWidth);
    expect(rect.top).toBeGreaterThanOrEqual(0);
    expect(rect.bottom).toBeLessThanOrEqual(view.innerHeight);
    const hit = doc.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2);
    expect(hit === element || element.contains(hit)).toBe(true);
  });
};

const geometryPlay = (width: number, height: number): NonNullable<Story["play"]> => async ({ canvasElement }) => {
  // Vitest owns an actual browser iframe. Keep ordinary Storybook play functions usable too.
  let restoreViewport = async () => {};
  if ("__vitest_browser__" in globalThis) {
    const { page } = await import("vitest/browser");
    const view = canvasElement.ownerDocument.defaultView!;
    const previousWidth = view.innerWidth;
    const previousHeight = view.innerHeight;
    restoreViewport = () => page.viewport(previousWidth, previousHeight);
    await page.viewport(width, height);
  }
  try {
    const canvas = within(canvasElement);
    const trigger = canvas.getByRole("button", { name: "Parameter" });
    const dialog = await openPicker(trigger);
    await expectHitTarget(summary(dialog, "Input"));
    await userEvent.click(summary(dialog, "Input"));
    const source = within(dialog).getByRole("button", { name: "Source Name", exact: true });
    await expectHitTarget(source);
    await userEvent.click(summary(dialog, "Input"));
    await userEvent.click(summary(dialog, "More"));
    const lastLeaf = within(dialog).getByRole("button", { name: "ProtocolVersion", exact: true });
    const doc = dialog.ownerDocument;
    const scrollBefore = doc.scrollingElement?.scrollTop;
    lastLeaf.scrollIntoView({ block: "nearest" });
    await expectHitTarget(lastLeaf);
    expect(doc.scrollingElement?.scrollTop).toBe(scrollBefore);
    const menu = dialog.querySelector("ul")!;
    if (height <= 240 && "__vitest_browser__" in globalThis) {
      expect(menu.scrollHeight).toBeGreaterThan(menu.clientHeight);
      expect(menu.scrollTop).toBeGreaterThan(0);
    }
    await userEvent.click(lastLeaf);
    await expectClosed(trigger);
    expect(canvas.getByLabelText("Selection")).toHaveTextContent("ProtocolVersion");
  } finally {
    await restoreViewport();
  }
};

const viewportParameters = (name: string, width: number, height: number) => ({
  viewport: {
    options: { [name]: { name, styles: { width: `${width}px`, height: `${height}px` } } },
    defaultViewport: name,
  },
});

export const DesktopGeometry: Story = {
  parameters: viewportParameters("desktop", 1280, 800),
  play: geometryPlay(1280, 800),
};
export const TabletGeometry: Story = {
  parameters: viewportParameters("tablet", 768, 600),
  play: geometryPlay(768, 600),
};
export const NarrowGeometry: Story = {
  parameters: viewportParameters("narrow", 375, 667),
  play: geometryPlay(375, 667),
};
export const ShortViewportScroll: Story = {
  parameters: viewportParameters("short", 375, 240),
  play: geometryPlay(375, 240),
};
