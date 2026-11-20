import React, { useState } from 'react';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, userEvent, waitFor, within } from 'storybook/test';
import { PersonsInput } from './PersonsInput.fs.ts';
import { emptyPerson, importedPersons, importedPersonDetails } from './PersonsInput.sample.fs.js';

type Person = ReturnType<typeof emptyPerson>;

const orcid = '0000-0002-1825-0097';
const fullName = 'Jane Smith';

function PersonEditor({ onImportPersons }: { onImportPersons?: () => Promise<Person[]> }) {
  const [persons, setPersons] = useState(() => [emptyPerson()]);
  return (
    <div className="swt:max-w-3xl swt:mx-auto swt:p-4">
      <PersonsInput persons={persons} setter={(next: Person[]) => setPersons([...next])} label="Persons" onImportPersons={onImportPersons} />
    </div>
  );
}

function installPersonSearch() {
  const originalFetch = globalThis.fetch;
  globalThis.fetch = async (input) => {
    const url = new URL(String(input));
    if (url.hostname !== 'pub.orcid.org') throw new Error(`Unexpected person search URL: ${url}`);
    if (url.pathname === '/v3.0/expanded-search/') {
      return Response.json({
        'expanded-result': [{
          'orcid-id': orcid, 'given-names': 'Jane', 'family-names': 'Smith',
          'credit-name': null, email: ['jane@example.org'], 'institution-name': ['Plant Research Institute'],
        }],
        'num-found': 1,
      });
    }
    if (url.pathname === `/v3.0/${orcid}/person`) {
      return Response.json({
        name: { 'given-names': { value: 'Jane' }, 'family-name': { value: 'Smith' } },
        emails: { email: [{ email: 'jane@example.org', visibility: 'PUBLIC' }] },
      });
    }
    if (url.pathname === `/v3.0/${orcid}/employments`) {
      return Response.json({
        'affiliation-group': [{ summaries: [{ 'employment-summary': { organization: { name: 'Plant Research Institute' }, 'end-date': null } }] }],
      });
    }
    throw new Error(`Unexpected ORCID endpoint: ${url}`);
  };
  return () => { globalThis.fetch = originalFetch; };
}

const meta = {
  title: 'Page Components/Metadata/PersonsInput',
  component: PersonEditor,
  tags: ['autodocs'],
  parameters: { layout: 'padded' },
  beforeEach: installPersonSearch,
} satisfies Meta<typeof PersonEditor>;

export default meta;
type Story = StoryObj<typeof meta>;

async function openSearch(canvasElement: HTMLElement, mode: 'ORCID' | 'Name' = 'ORCID') {
  const canvas = within(canvasElement);
  await userEvent.click(canvas.getAllByRole('checkbox')[0]);
  await userEvent.click(canvas.getByRole('button', { name: mode === 'ORCID' ? 'Search by ORCID' : 'Search by name' }));
  const portal = within(canvasElement.ownerDocument.body);
  return { canvas, portal, dialog: within(await portal.findByRole('dialog', { name: 'Search person' })) };
}

async function previewPerson(canvasElement: HTMLElement, mode: 'ORCID' | 'Name', query: string) {
  const context = await openSearch(canvasElement, mode);
  const { dialog } = context;
  await expect(dialog.getByRole('button', { name: mode, pressed: true })).toBeInTheDocument();
  await userEvent.type(dialog.getByRole('textbox', { name: mode === 'ORCID' ? 'ORCID iD or URL' : 'Person name' }), `${query}{Enter}`);
  await expect(await dialog.findByRole('heading', { name: 'Autofill preview' })).toBeInTheDocument();
  await expect(dialog.getAllByText(fullName)).toHaveLength(1);
  await expect(dialog.queryByRole('button', { name: `Preview ${fullName}` })).not.toBeInTheDocument();
  return context;
}

async function confirmPerson(canvasElement: HTMLElement, mode: 'ORCID' | 'Name', query: string) {
  const { canvas, portal, dialog } = await previewPerson(canvasElement, mode, query);
  await expect(canvas.queryByDisplayValue('Jane')).not.toBeInTheDocument();
  await userEvent.click(dialog.getByRole('button', { name: 'Apply person' }));
  await waitFor(() => expect(portal.queryByRole('dialog')).not.toBeInTheDocument());
  await expect(canvas.getByDisplayValue('Jane')).toBeInTheDocument();
  await expect(canvas.getByDisplayValue('Smith')).toBeInTheDocument();
  await expect(canvas.getByDisplayValue(orcid)).toBeInTheDocument();
  await expect(canvas.getByDisplayValue('jane@example.org')).toBeInTheDocument();
  await expect(canvas.getByDisplayValue('Plant Research Institute')).toBeInTheDocument();
  await expect(canvas.getByRole('heading', { name: fullName })).toBeInTheDocument();
}

export const ManualEntry: Story = {};

const loadPersons = async () => importedPersons();

export const ImportPersons: Story = {
  args: { onImportPersons: loadPersons },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(canvas.getByRole('button', { name: 'Import Persons' }));
    const portal = within(canvasElement.ownerDocument.body);
    const dialog = within(await portal.findByRole('dialog', { name: 'Import persons' }));
    await expect(dialog.getByRole('button', { name: 'Import selected' })).toBeDisabled();
    await userEvent.click(await dialog.findByRole('checkbox', { name: 'Import Ada Lovelace' }));
    await userEvent.click(dialog.getByRole('button', { name: 'Import selected' }));
    await waitFor(() => expect(portal.queryByRole('dialog')).not.toBeInTheDocument());
    await expect(canvas.getByRole('heading', { name: 'Ada Lovelace' })).toBeInTheDocument();
    await expect(canvas.queryByRole('heading', { name: 'Grace Hopper' })).not.toBeInTheDocument();
  },
};

export const EmptyImport: Story = {
  args: { onImportPersons: async () => [] },
  play: async ({ canvasElement }) => {
    await userEvent.click(within(canvasElement).getByRole('button', { name: 'Import Persons' }));
    const dialog = within(await within(canvasElement.ownerDocument.body).findByRole('dialog', { name: 'Import persons' }));
    await expect(await dialog.findByText('No persons available to import.')).toBeInTheDocument();
  },
};

export const ImportPersonDetails: Story = {
  args: {
    onImportPersons: async () => importedPersonDetails(),
  },
  play: async ({ canvasElement }) => {
    await userEvent.click(within(canvasElement).getByRole('button', { name: 'Import Persons' }));
    const portal = within(canvasElement.ownerDocument.body);
    const dialog = within(await portal.findByRole('dialog', { name: 'Import persons' }));
    const moreInfo = await dialog.findByTitle('More info about Ada A Lovelace');
    await userEvent.click(moreInfo);
    const details = within(await portal.findByRole('dialog', { name: 'Person details' }));
    for (const label of ['First name', 'Last name', 'Mid initials', 'ORCID', 'Address', 'Affiliation', 'Email', 'Phone', 'Fax', 'Roles']) {
      await expect(details.getByText(label, { exact: true })).toBeInTheDocument();
    }
    for (const value of ['Ada', 'Lovelace', 'A', orcid, 'Research Institute', 'ada@example.org', '+44 123', '+44 456', 'Investigator']) {
      await expect(details.getByText(value, { exact: true })).toBeInTheDocument();
    }
    await expect(details.getByText('CREDIT:0000001')).toBeInTheDocument();
    await expect(details.getByText('Primary role')).toBeInTheDocument();
    await expect(details.getByText('Study design')).toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    await waitFor(() => expect(portal.queryByRole('dialog', { name: 'Person details' })).not.toBeInTheDocument());
    await expect(portal.getByRole('dialog', { name: 'Import persons' })).toBeInTheDocument();
    await expect(dialog.getByRole('checkbox', { name: 'Import Ada A Lovelace' })).not.toBeChecked();
    await expect(dialog.getByRole('button', { name: 'Import selected' })).toBeDisabled();
    await userEvent.click(moreInfo);
  },
};

export const ImportError: Story = {
  args: { onImportPersons: async () => { throw new Error('Could not load persons from the source.'); } },
  play: async ({ canvasElement }) => {
    await userEvent.click(within(canvasElement).getByRole('button', { name: 'Import Persons' }));
    const dialog = within(await within(canvasElement.ownerDocument.body).findByRole('dialog', { name: 'Import persons' }));
    await expect(await dialog.findByRole('alert')).toHaveTextContent('Could not load persons from the source.');
    await expect(dialog.getByRole('button', { name: 'Retry' })).toBeEnabled();
  },
};

export const OrcidIdLookup: Story = {
  play: async ({ canvasElement }) => confirmPerson(canvasElement, 'ORCID', orcid),
};

export const OrcidUrlLookup: Story = {
  play: async ({ canvasElement }) => confirmPerson(canvasElement, 'ORCID', `https://orcid.org/${orcid}`),
};

export const NameSearch: Story = {
  play: async ({ canvasElement }) => confirmPerson(canvasElement, 'Name', fullName),
};

export const CancelPreview: Story = {
  play: async ({ canvasElement }) => {
    const { canvas, portal, dialog } = await previewPerson(canvasElement, 'ORCID', orcid);
    await userEvent.click(dialog.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(portal.queryByRole('dialog')).not.toBeInTheDocument());
    await expect(canvas.queryByDisplayValue('Jane')).not.toBeInTheDocument();
    await expect(canvas.getByRole('heading', { name: '<name>' })).toBeInTheDocument();
  },
};
