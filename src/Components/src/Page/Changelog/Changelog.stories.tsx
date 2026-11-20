import React from 'react';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, userEvent, within, waitFor } from 'storybook/test';
import { Release } from '../../Api/GitHubReleases.fs.ts';
import { Changelog } from './Changelog.fs.ts';
import { ChangelogSample } from './Changelog.sample.fs.ts';

const meta = {
  title: 'Page Components/Changelog',
  component: ChangelogSample,
  parameters: { layout: 'fullscreen' },
  args: { currentRelease: '1.0.0' },
} satisfies Meta<typeof ChangelogSample>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByRole('status')).toHaveTextContent('Loading release notes...');
    await expect(canvas.getByRole('checkbox', { name: 'Use GitHub releases' })).toBeDisabled();
    await expect(await canvas.findByRole('heading', { name: 'Welcome', exact: true }, { timeout: 5000 })).toBeVisible();
    await expect(canvas.getByRole('button', { name: 'Next', exact: true })).toBeDisabled();
    await expect(canvas.getByText('2 / 2')).toBeVisible();
    await expect(canvas.getByText('The first release.')).toBeVisible();
    await userEvent.click(canvas.getByRole('button', { name: 'Previous' }));
    await expect(canvas.getByRole('heading', { name: 'Improvements', exact: true })).toBeVisible();
    await expect(canvas.getByRole('button', { name: 'Previous' })).toBeDisabled();
    await userEvent.click(canvas.getByRole('button', { name: 'Next', exact: true }));
    await expect(canvas.getByRole('heading', { name: 'Welcome', exact: true })).toBeVisible();
    await userEvent.selectOptions(canvas.getByRole('combobox', { name: 'Release version' }), '0');
    await expect(canvas.getByRole('heading', { name: 'Improvements', exact: true })).toBeVisible();
    await userEvent.type(canvas.getByRole('textbox', { name: 'GitHub repository' }), 'nfdi4plants/ARCitISA');
    await expect(canvas.getByRole('checkbox', { name: 'Use GitHub releases' })).toBeEnabled();
    // Leave live fetching to manual experimentation; this story stays deterministic.
    await expect(canvas.getByRole('checkbox', { name: 'Use GitHub releases' })).not.toBeChecked();
  },
};

export const MissingRelease: Story = {
  args: { currentRelease: '0.9.0' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(await canvas.findByRole('heading', { name: 'Improvements', exact: true }, { timeout: 5000 })).toBeVisible();
  },
};

const failedFetch = async () => { throw new Error('Release service unavailable.'); };

export const FetchError: Story = {
  render: () => <Changelog fetchReleases={failedFetch} currentRelease="1.0.0" />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(await canvas.findByRole('alert')).toHaveTextContent('Release service unavailable.');
    await expect(canvas.getByRole('button', { name: 'Retry' })).toBeVisible();
  },
};

const emptyFetch = async () => [];

export const EmptyReleases: Story = {
  render: () => <Changelog fetchReleases={emptyFetch} currentRelease="1.0.0" />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(await canvas.findByText('No published releases found.')).toBeVisible();
    await expect(canvas.queryByRole('navigation', { name: 'Changelog pagination' })).not.toBeInTheDocument();
  },
};

const categoryFetch = async () => [
  new Release('v2.0.0', undefined, [
    '## Added',
    ...Array.from({ length: 25 }, (_, i) => `- Added capability ${i + 1}`),
    '## Fixed', '- Corrected release navigation',
    '## Changed', '- Updated defaults',
  ].join('\n\n'), false, false),
  new Release('v1.0.0', undefined, 'Older notes without headings.\n\n- First improvement\n- Second improvement', false, false),
];

export const SectionNavigation: Story = {
  render: () => (
    <div style={{ height: 500, display: 'flex' }}>
      <Changelog fetchReleases={categoryFetch} currentRelease="2.0.0" />
    </div>
  ),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const nav = within(await canvas.findByRole('navigation', { name: 'Release note sections' }));
    await expect(nav.getByRole('button', { name: 'Added 25' })).toBeVisible();
    await expect(canvas.getByRole('heading', { name: 'Fixed', exact: true })).toBeVisible();
    const scrollPane = canvas.getByRole('region', { name: 'Release notes' });
    await expect(scrollPane.scrollTop).toBe(0);
    await userEvent.click(nav.getByRole('button', { name: 'Fixed 1' }));
    await waitFor(() => expect(scrollPane.scrollTop).toBeGreaterThan(0));
    await userEvent.click(canvas.getByRole('button', { name: 'Next', exact: true }));
    await expect(nav.getByRole('button', { name: 'Release notes 2' })).toBeVisible();
    await expect(nav.queryByRole('button', { name: 'Fixed 1' })).not.toBeInTheDocument();
  },
};

const hierarchyFetch = async () => [
  new Release('v2.0.0', undefined, [
    '## Added', '- Direct addition',
    '### Editor', '- Editing improvement',
    '#### Shortcuts', '- Keyboard shortcut',
    '### Import', '- New format',
    '## Fixed', '- Bug fix',
  ].join('\n\n'), false, false),
];

export const HeadingHierarchy: Story = {
  render: () => <Changelog fetchReleases={hierarchyFetch} currentRelease="2.0.0" />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const nav = within(await canvas.findByRole('navigation', { name: 'Release note sections' }));
    await expect(nav.getByText('Counts include subsections.')).toBeVisible();
    const added = nav.getByRole('button', { name: 'Added 4' });
    const editor = nav.getByRole('button', { name: 'Editor 2' });
    const shortcuts = nav.getByRole('button', { name: 'Shortcuts 1' });
    const addedChildren = added.parentElement!.querySelector('ul')!;
    await expect(addedChildren).toContainElement(editor);
    await expect(addedChildren).toContainElement(nav.getByRole('button', { name: 'Import 1' }));
    await expect(addedChildren).not.toContainElement(nav.getByRole('button', { name: 'Fixed 1' }));
    await expect(editor.parentElement!.querySelector('ul')).toContainElement(shortcuts);
    await expect(editor.getBoundingClientRect().left).toBeGreaterThan(added.getBoundingClientRect().left);
    await expect(shortcuts.getBoundingClientRect().left).toBeGreaterThan(editor.getBoundingClientRect().left);
  },
};
