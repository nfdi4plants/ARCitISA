import type { Meta, StoryObj } from '@storybook/react-vite';
import { within, expect, waitFor, userEvent } from 'storybook/test';
import {
  DirtySave as DirtySaveSample,
  LineEndings as LineEndingsSample,
  Languages as LanguagesSample,
  ThemeSwitch as ThemeSwitchSample,
  SchemaPlayground as SchemaPlaygroundSample,
  EditApi as EditApiSample,
  EditApiReadOnly as EditApiReadOnlySample,
} from './TextEditor.sample.fs.js';

const meta = {
  title: 'Composite Components/TextEditor',
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

export const DirtySave: Story = {
  render: () => <DirtySaveSample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const content = await editorContent(canvasElement);
    await userEvent.click(content);
    await userEvent.keyboard('x');
    await waitFor(() => expect(canvas.getByText('Unsaved changes')).toBeInTheDocument());
    await userEvent.keyboard('{Control>}s{/Control}');
    await waitFor(() => expect(canvas.getByText('Saved')).toBeInTheDocument());
    expect(content.textContent).toContain('x');
  },
};

/** A file with CRLF line breaks keeps them when it is saved. */
export const LineEndings: Story = {
  render: () => <LineEndingsSample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const content = await editorContent(canvasElement);
    await userEvent.click(content);
    await userEvent.keyboard('{Control>}{End}{/Control}x');
    await userEvent.click(canvas.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(canvas.getByTestId('saved-text')).toHaveTextContent('first\\r\\nsecond\\r\\nx'));
  },
};

/** A button and a key binding change the text through the edit API. Each edit is one undo step. */
export const EditApi: Story = {
  render: () => <EditApiSample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const content = await editorContent(canvasElement);
    await userEvent.click(canvas.getByRole('button', { name: 'Wrap first word' }));
    await waitFor(() => expect(content.textContent).toBe('**hello** world'));
    const state = canvas.getByTestId('edit-state');
    expect(state).toHaveTextContent('"anchor":2');
    expect(state).toHaveTextContent('"head":7');

    await userEvent.keyboard('{Control>}z{/Control}');
    await waitFor(() => expect(content.textContent).toBe('hello world'));

    // The binding runs ahead of selectParentSyntax, which basicSetup binds to Mod-i.
    await userEvent.click(content);
    await userEvent.keyboard('{Control>}{End}{/Control}');
    await userEvent.keyboard('{Control>}i{/Control}');
    await waitFor(() => expect(content.textContent!.endsWith('_')).toBe(true));
  },
};

/** The edit API changes nothing in a read-only editor. */
export const EditApiReadOnly: Story = {
  render: () => <EditApiReadOnlySample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const content = await editorContent(canvasElement);
    await userEvent.click(canvas.getByRole('button', { name: 'Wrap first word' }));
    await waitFor(() => expect(canvas.getByTestId('apply-result')).toHaveTextContent('false'));
    expect(content.textContent).toBe('hello world');
  },
};

export const Languages: Story = {
  render: () => <LanguagesSample />,
  play: async ({ canvasElement }) => {
    await waitFor(() => expect(canvasElement.querySelectorAll('.cm-editor').length).toBe(3), { timeout: 30_000 });
    // Highlighting wraps tokens in spans once the language package has loaded.
    const python = canvasElement.querySelectorAll('.cm-editor')[2];
    await waitFor(() => expect(python.querySelector('.cm-line span')).not.toBeNull(), { timeout: 30_000 });
  },
};

export const ThemeSwitch: Story = {
  render: () => <ThemeSwitchSample />,
  play: async ({ canvasElement }) => {
    const root = document.documentElement;
    const original = root.getAttribute('data-theme');
    await editorContent(canvasElement);
    const background = () => getComputedStyle(canvasElement.querySelector('.cm-editor')!).backgroundColor;
    const before = background();
    try {
      root.setAttribute('data-theme', original === 'finster' ? 'sunrise' : 'finster');
      await waitFor(() => expect(background()).not.toBe(before), { timeout: 10_000 });
    } finally {
      if (original === null) root.removeAttribute('data-theme');
      else root.setAttribute('data-theme', original);
    }
  },
};

/**
 * Edit JSON Schemas (2020-12) on the left and a YAML or JSON document on the right. Schemas reference
 * each other by `$id`, so `"$ref": "person.schema.json"` in the study schema finds the person schema.
 * "Raw Ajv errors" shows what the validator reports before the editor turns it into diagnostics.
 * The controls set the starting content: `schemas` takes schema texts, the first one is the root.
 */
export const SchemaPlayground: Story = {
  args: { schemas: undefined, yamlDocument: undefined, jsonDocument: undefined },
  argTypes: {
    schemas: { control: 'object', description: 'JSON texts of the schemas. The first one is the root.' },
    yamlDocument: { control: 'text', description: 'The YAML document at the start.' },
    jsonDocument: { control: 'text', description: 'The JSON document at the start.' },
  },
  render: (args) => <SchemaPlaygroundSample key={JSON.stringify(args)} {...args} />,
  // Only checks, so the playground opens untouched in Storybook.
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await waitFor(() => expect(canvas.getByTestId('diagnostic-count')).toHaveTextContent('6 diagnostics'), {
      timeout: 30_000,
    });
    // The suggested values of the misspelled status and key.
    const list = canvas.getByTestId('diagnostic-list');
    expect(list).toHaveTextContent('"running"');
    expect(list).toHaveTextContent('"familyName"');
  },
};

/** The playground after switching to JSON and removing the person schema, which the study schema references. */
export const SchemaChanges: Story = {
  render: () => <SchemaPlaygroundSample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const count = () => canvas.getByTestId('diagnostic-count');
    await waitFor(() => expect(count()).toHaveTextContent('6 diagnostics'), { timeout: 30_000 });

    await userEvent.click(canvas.getByRole('button', { name: 'JSON' }));
    await waitFor(() => expect(count()).toHaveTextContent('6 diagnostics'), { timeout: 30_000 });

    // Without the person schema, the $ref of the study schema no longer resolves.
    await userEvent.click(canvas.getByRole('tab', { name: 'person.schema.json' }));
    await userEvent.click(canvas.getByRole('button', { name: 'Remove schema' }));
    await waitFor(() => expect(canvas.getByTestId('schema-status')).toHaveTextContent('person.schema.json'));
    await waitFor(() => expect(count()).toHaveTextContent('0 diagnostics'), { timeout: 30_000 });
  },
};
