import type { Meta, StoryObj } from '@storybook/react-vite';
import { within, expect, userEvent, waitFor } from 'storybook/test';
import {
  ChangedOnDisk as ChangedOnDiskSample,
  Html as HtmlSample,
  Markdown as MarkdownSample,
  ReadOnly as ReadOnlySample,
  Yaml as YamlSample,
} from './TextDocument.sample.fs.js';

const meta = {
  title: 'Page Components/FileViewer/TextDocument',
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

/** An edit enables Save, and the status follows the save of the host. */
export const Markdown: Story = {
  render: () => <MarkdownSample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const content = await editorContent(canvasElement);
    const save = canvas.getByRole('button', { name: 'Save' });
    expect(save).toBeDisabled();

    await userEvent.click(content);
    await userEvent.keyboard('{Control>}{End}{/Control}- Note the results');
    await waitFor(() => expect(canvas.getByText('Unsaved changes')).toBeVisible());
    expect(save).toBeEnabled();

    await userEvent.click(save);
    await waitFor(() => expect(canvas.getByText('Saved')).toBeVisible());
    expect(save).toBeDisabled();
  },
};

/** An HTML file opens as it looks, and Edit shows its source. */
export const Html: Story = {
  render: () => <HtmlSample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    expect(canvas.getByTitle('HTML preview')).toBeVisible();
    expect(canvas.queryByRole('button', { name: 'Undo' })).toBeNull();

    await userEvent.click(canvas.getByRole('radio', { name: 'Edit' }));
    const content = await editorContent(canvasElement);
    await waitFor(() => expect(content).toHaveTextContent('<h1>Report</h1>'));
  },
};

/** Plain text files get Undo and Redo, but no formatting. */
export const Yaml: Story = {
  render: () => <YamlSample />,
};

/** The page says why the file is read-only and disables Save and the toolbar. */
export const ReadOnly: Story = {
  render: () => <ReadOnlySample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await editorContent(canvasElement);
    expect(canvas.getByRole('status')).toHaveTextContent('This file is write-protected.');
    expect(canvas.getByRole('button', { name: 'Save' })).toBeDisabled();
    expect(canvas.getByRole('button', { name: 'Bold' })).toBeDisabled();
  },
};

/** "Reload from disk" replaces the text in the open preview and in the editor behind it. */
export const ChangedOnDisk: Story = {
  render: () => <ChangedOnDiskSample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const content = await editorContent(canvasElement);
    expect(canvas.getByRole('alert')).toHaveTextContent('The file changed on disk since you opened it.');

    // The hidden editor holds the same text, so the preview is looked up by its elements.
    await userEvent.click(canvas.getByRole('radio', { name: 'Preview' }));
    await waitFor(() => expect(canvas.getByText('Prepare the buffer', { selector: 'li' })).toBeVisible());

    await userEvent.click(canvas.getByRole('button', { name: 'Reload from disk' }));
    await waitFor(() =>
      expect(canvas.getByText('Another program changed this file.', { selector: 'p' })).toBeVisible(),
    );
    expect(canvas.queryByRole('alert')).toBeNull();

    await userEvent.click(canvas.getByRole('radio', { name: 'Edit' }));
    await waitFor(() => expect(content).toHaveTextContent('Another program changed this file.'));
  },
};
