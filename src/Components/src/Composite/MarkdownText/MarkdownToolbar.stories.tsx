import type { Meta, StoryObj } from '@storybook/react-vite';
import { within, expect, waitFor, userEvent } from 'storybook/test';
import {
  Editor as EditorSample,
  ReadOnly as ReadOnlySample,
  WithOntologyReference as WithOntologyReferenceSample,
  WithPathCompletion as WithPathCompletionSample,
} from './MarkdownToolbar.sample.fs.js';

const meta = {
  title: 'Composite Components/MarkdownToolbar',
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

/** The buttons and the key bindings run the same commands, and each edit is one undo step. */
export const Editor: Story = {
  render: () => <EditorSample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const content = await editorContent(canvasElement);
    await userEvent.click(content);
    await userEvent.keyboard('{Control>}a{/Control}');

    await userEvent.click(canvas.getByRole('button', { name: 'Heading' }));
    await userEvent.click(canvas.getByRole('button', { name: 'Heading 1' }));
    await waitFor(() => expect(content.textContent).toBe('# format me'));
    await userEvent.keyboard('{Control>}z{/Control}');
    await waitFor(() => expect(content.textContent).toBe('format me'));

    await userEvent.click(canvas.getByRole('button', { name: 'Bold' }));
    await waitFor(() => expect(content.textContent).toBe('**format me**'));

    await userEvent.keyboard('{Control>}z{/Control}');
    await waitFor(() => expect(content.textContent).toBe('format me'));

    await userEvent.keyboard('{Control>}b{/Control}');
    await waitFor(() => expect(content.textContent).toBe('**format me**'));

    await userEvent.click(canvas.getByRole('button', { name: 'Bulleted list' }));
    await waitFor(() => expect(content.textContent).toBe('- **format me**'));

    // A refused key changes nothing and shows its reason in the toolbar.
    await userEvent.click(content);
    await userEvent.keyboard('{Control>}{End}{/Control}');
    await userEvent.keyboard('{Control>}i{/Control}');
    await waitFor(() => expect(canvas.getByRole('status')).toHaveTextContent(/\S/));
    expect(content.textContent).toBe('- **format me**');
  },
};

/** The Ontology reference button inserts a reference link and its definition for a searched term. */
export const WithOntologyReference: Story = {
  render: () => <WithOntologyReferenceSample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const content = await editorContent(canvasElement);
    await userEvent.click(content);
    await userEvent.keyboard('{Control>}{End}{/Control}');
    await userEvent.click(canvas.getByRole('button', { name: 'Ontology reference' }));

    const body = within(canvasElement.ownerDocument.body);
    const search = await body.findByTestId('term-search-input');
    await userEvent.type(search, 'instrument');
    await userEvent.click(await body.findByText('instrument model', undefined, { timeout: 10_000 }));
    const insert = body.getByRole('button', { name: 'Insert' });
    await waitFor(() => expect(insert).toBeEnabled());
    await userEvent.click(insert);

    await waitFor(() => {
      expect(content.textContent).toContain('[instrument model][MS:1000031]');
      expect(content.textContent).toContain('[MS:1000031]: http://purl.obolibrary.org/obo/MS_1000031');
    });

    // The insert is one undo step: the typed "x" is one, the insert the other.
    const initial = 'The sample was measured with an ';
    await userEvent.keyboard('x');
    await waitFor(() => expect(content.textContent).toContain('[instrument model][MS:1000031]x'));
    await userEvent.keyboard('{Control>}z{/Control}');
    await userEvent.keyboard('{Control>}z{/Control}');
    await waitFor(() => expect(content.textContent).toBe(initial));
  },
};

/** Without a mouse: the key opens the dialog, Enter picks a hit, the next Enter inserts it and typing goes on. */
export const OntologyReferenceByKeyboard: Story = {
  render: () => <WithOntologyReferenceSample />,
  play: async ({ canvasElement }) => {
    const content = await editorContent(canvasElement);
    await userEvent.click(content);
    await userEvent.keyboard('{Control>}{End}{/Control}');
    await userEvent.keyboard('{Control>}{Shift>}o{/Shift}{/Control}');

    const body = within(canvasElement.ownerDocument.body);
    const search = await body.findByTestId('term-search-input');
    await waitFor(() => expect(search).toHaveFocus());
    await userEvent.keyboard('instrument');
    await body.findByText('instrument model', undefined, { timeout: 10_000 });
    await userEvent.keyboard('{ArrowDown}{Enter}');
    await waitFor(() => expect(body.getByRole('button', { name: 'Insert' })).toBeEnabled());
    await userEvent.keyboard('{Enter}');

    await waitFor(() => expect(content).toHaveFocus());
    await userEvent.keyboard('x');
    await waitFor(() => expect(content.textContent).toContain('[instrument model][MS:1000031]x'));
  },
};

/** A read-only editor disables the toolbar, and its format keys change nothing. */
export const ReadOnly: Story = {
  render: () => <ReadOnlySample />,
  play: async ({ canvasElement }) => {
    const content = await editorContent(canvasElement);
    await userEvent.click(content);
    await userEvent.keyboard('{Control>}a{/Control}');
    await userEvent.keyboard('{Control>}b{/Control}');
    expect(content.textContent).toBe('format me');
  },
};

/**
 * A link or image target that starts with "/", "./" or "../" lists the folder it names, folders first. A chosen folder
 * lists its own entries next, and an image target lists only folders and images.
 */
export const PathCompletion: Story = {
  render: () => <WithPathCompletionSample />,
  play: async ({ canvasElement }) => {
    const content = await editorContent(canvasElement);
    const optionLabels = () =>
      Array.from(canvasElement.querySelectorAll('.cm-tooltip-autocomplete li .cm-completionLabel')).map(
        (label) => label.textContent,
      );
    // Enter accepts an entry only once the list has stood still for a moment.
    const settle = () => new Promise((resolve) => setTimeout(resolve, 150));

    await userEvent.click(content);
    await userEvent.keyboard('{Control>}{End}{/Control}');

    // The editor closes the brackets itself, so the cursor stays inside the target.
    await userEvent.keyboard('[[g](./');
    await waitFor(() => expect(optionLabels()).toEqual(['sub/', 'guide.md', 'other.md']));
    expect(canvasElement.querySelector('.cm-tooltip-autocomplete .cm-swtCompletionIcon')).not.toBeNull();

    await userEvent.keyboard('su');
    await waitFor(() => expect(optionLabels()).toEqual(['sub/']));
    await settle();
    await userEvent.keyboard('{Enter}');
    await waitFor(() => expect(optionLabels()).toEqual(['deep.md']));
    await settle();
    await userEvent.keyboard('{Enter}');
    await waitFor(() => expect(content.textContent).toContain('[g](./sub/deep.md'));

    // A space in a name is encoded, and scan.tif is not an image the preview shows.
    await userEvent.keyboard('{End} ![[i](../images/');
    await waitFor(() => expect(optionLabels()).toEqual(['logo.png', 'photo%20one.jpg']));
    await userEvent.keyboard('{Escape}');
  },
};
