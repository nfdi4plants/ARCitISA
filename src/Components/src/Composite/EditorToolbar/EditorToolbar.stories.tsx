import type { Meta, StoryObj } from '@storybook/react-vite';
import { within, expect, waitFor, userEvent } from 'storybook/test';
import { Editor as EditorSample, ReadOnly as ReadOnlySample } from './EditorToolbar.sample.fs.js';

const meta = {
  title: 'Composite Components/EditorToolbar',
  parameters: { layout: 'fullscreen' },
} satisfies Meta;

export default meta;
type Story = StoryObj<typeof meta>;

const editorContent = async (root: HTMLElement) =>
  waitFor(
    () => {
      const el = root.querySelector('.cm-editor .cm-content');
      expect(el).not.toBeNull();
      return el as HTMLElement;
    },
    { timeout: 30_000 },
  );

/** Undo and Redo follow the history of a YAML editor and give the focus back to it. */
export const Editor: Story = {
  render: () => <EditorSample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const content = await editorContent(canvasElement);
    const undo = canvas.getByRole('button', { name: 'Undo' });
    const redo = canvas.getByRole('button', { name: 'Redo' });
    expect(undo).toBeDisabled();
    expect(redo).toBeDisabled();

    await userEvent.click(content);
    await userEvent.keyboard('{Control>}{End}{/Control}');
    await userEvent.keyboard(' 2');
    await waitFor(() => expect(content.textContent).toBe('title: Dilution series 2'));
    await waitFor(() => expect(undo).toBeEnabled());

    await userEvent.click(undo);
    await waitFor(() => expect(content.textContent).toBe('title: Dilution series'));
    await waitFor(() => expect(redo).toBeEnabled());
    expect(content).toHaveFocus();

    await userEvent.click(redo);
    await waitFor(() => expect(content.textContent).toBe('title: Dilution series 2'));
    await waitFor(() => expect(redo).toBeDisabled());
  },
};

/** Over a read-only editor Undo and Redo are disabled and give the reason. */
export const ReadOnly: Story = {
  render: () => <ReadOnlySample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await editorContent(canvasElement);
    const toolbar = canvas.getByRole('toolbar', { name: 'Editing' });
    for (const button of within(toolbar).getAllByRole('button')) {
      expect(button).toBeDisabled();
      expect(button).toHaveAttribute('title', 'This file is write-protected.');
    }
  },
};
