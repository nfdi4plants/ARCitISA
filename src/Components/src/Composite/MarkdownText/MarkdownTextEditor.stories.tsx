import type { Meta, StoryObj } from '@storybook/react-vite';
import { within, expect, userEvent, waitFor } from 'storybook/test';
import { Entry as MarkdownTextEditorEntry } from './MarkdownTextEditor.sample.fs.js';

const meta = {
  title: 'Composite Components/MarkdownTextEditor',
  tags: ['autodocs'],
  parameters: {
    layout: 'fullscreen',
    docs: {
      canvas: {
        withToolbar: false,
        sourceState: 'none',
      },
    },
  },
  component: MarkdownTextEditorEntry,
} satisfies Meta<typeof MarkdownTextEditorEntry>;

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

export const Default: Story = {
  render: () => <MarkdownTextEditorEntry />,
};

/** Preview hides the editor and disables the toolbar. Edit brings the editor back with its text. */
export const ModeSwitching: Story = {
  parameters: { isolated: true },
  render: () => <MarkdownTextEditorEntry />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const content = await editorContent(canvasElement);

    await userEvent.click(canvas.getByRole('button', { name: 'Preview' }));

    await waitFor(() => {
      expect(content).not.toBeVisible();
      expect(canvas.getByRole('heading', { name: 'Markdown Notes' })).toBeVisible();
      expect(canvas.getByRole('button', { name: 'Bold' })).toBeDisabled();
    });

    await userEvent.click(canvas.getByRole('button', { name: 'Edit' }));

    await waitFor(() => {
      expect(content).toBeVisible();
      expect(canvas.queryByRole('heading', { name: 'Markdown Notes' })).toBeNull();
      expect(canvas.getByRole('button', { name: 'Bold' })).toBeEnabled();
    });
  },
};

/** The toolbar formats the text, and the live preview follows. */
export const Formatting: Story = {
  parameters: { isolated: true },
  render: () => <MarkdownTextEditorEntry />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const content = await editorContent(canvasElement);

    await userEvent.click(content);
    await userEvent.keyboard('{Control>}{End}{/Control}');
    await userEvent.keyboard('ready');
    await userEvent.keyboard('{Shift>}{Home}{/Shift}');
    await userEvent.click(canvas.getByRole('button', { name: 'Bold' }));

    await waitFor(() => expect(content.textContent).toContain('**ready**'));
    await waitFor(() => expect(canvas.getByText('ready', { selector: 'strong' })).toBeVisible());
  },
};
