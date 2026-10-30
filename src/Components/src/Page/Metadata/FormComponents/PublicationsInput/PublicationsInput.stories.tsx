import React, { useState } from 'react';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, userEvent, waitFor, within } from 'storybook/test';
import { PublicationsInput } from './PublicationsInput.fs.ts';
import { emptyPublication } from './PublicationsInput.sample.fs.js';

type Publication = ReturnType<typeof emptyPublication>;

const title = 'A practical atlas of plant biology';
const doi = '10.1234/plant-atlas';

function PublicationEditor() {
  const [publications, setPublications] = useState(() => [emptyPublication()]);
  return (
    <div className="swt:max-w-3xl swt:mx-auto swt:p-4">
      <PublicationsInput
        publications={publications}
        setter={(next: Publication[]) => setPublications([...next])}
        label="Publications"
      />
    </div>
  );
}

function installPublicationSearch() {
  const originalFetch = globalThis.fetch;
  globalThis.fetch = async (input) => {
    const url = new URL(String(input));
    if (url.hostname === 'api.crossref.org') {
      return Response.json({
        message: {
          title: [title], DOI: doi,
          author: [{ given: 'Ada', family: 'Lovelace' }, { given: 'Alan', family: 'Turing' }],
          'container-title': ['Plant Methods'], published: { 'date-parts': [[2025]] },
        },
      });
    }
    if (url.hostname === 'www.ebi.ac.uk') {
      return Response.json({
        resultList: { result: [{
          id: '12345678', source: 'MED', pmid: '12345678', pmcid: 'PMC123456',
          doi, title, authorString: 'Lovelace A, Turing A',
          journalTitle: 'Plant Methods', pubYear: '2025',
        }] },
        nextCursorMark: '*',
      });
    }
    throw new Error(`Unexpected publication search URL: ${url}`);
  };
  return () => { globalThis.fetch = originalFetch; };
}

const meta = {
  title: 'Page Components/Metadata/PublicationsInput',
  component: PublicationEditor,
  tags: ['autodocs'],
  parameters: { layout: 'padded' },
  beforeEach: installPublicationSearch,
} satisfies Meta<typeof PublicationEditor>;

export default meta;
type Story = StoryObj<typeof meta>;

async function openSearch(canvasElement: HTMLElement, mode: 'DOI' | 'Europe PMC' = 'DOI') {
  const canvas = within(canvasElement);
  await userEvent.click(canvas.getAllByRole('checkbox')[0]);
  await userEvent.click(canvas.getByRole('button', { name: mode === 'DOI' ? 'Search by DOI' : 'Search by PubMed ID' }));
  const portal = within(canvasElement.ownerDocument.body);
  return { canvas, portal, dialog: within(await portal.findByRole('dialog', { name: 'Search publication' })) };
}

async function confirmPublication(canvasElement: HTMLElement, mode: 'DOI' | 'Europe PMC') {
  const { canvas, portal, dialog } = await openSearch(canvasElement, mode);
  const query = mode === 'DOI' ? `https://doi.org/${doi}` : 'plant atlas Lovelace';
  await userEvent.type(dialog.getByRole('textbox', { name: mode === 'DOI' ? 'DOI' : 'Europe PMC query' }), mode === 'DOI' ? `${query}{Enter}` : query);
  if (mode === 'Europe PMC') await userEvent.click(dialog.getByRole('button', { name: 'Search', exact: true }));
  await expect(await dialog.findByRole('heading', { name: 'Autofill preview' })).toBeInTheDocument();
  await expect(dialog.getAllByText(title)).toHaveLength(1);
  await expect(dialog.queryByRole('button', { name: `Preview ${title}` })).not.toBeInTheDocument();
  await expect(canvas.queryByRole('heading', { name: title })).not.toBeInTheDocument();
  await userEvent.click(dialog.getByRole('button', { name: 'Apply publication' }));
  await waitFor(() => expect(portal.queryByRole('dialog')).not.toBeInTheDocument());
  await expect(canvas.getByRole('heading', { name: title })).toBeInTheDocument();
  await expect(canvas.getByDisplayValue(title)).toBeInTheDocument();
  await expect(canvas.getByDisplayValue(doi)).toBeInTheDocument();
  if (mode === 'Europe PMC') await expect(canvas.getByDisplayValue('12345678')).toBeInTheDocument();
}

export const ManualEntry: Story = {};

export const DoiLookup: Story = {
  play: async ({ canvasElement }) => confirmPublication(canvasElement, 'DOI'),
};

export const EuropePmcSearch: Story = {
  play: async ({ canvasElement }) => confirmPublication(canvasElement, 'Europe PMC'),
};

export const CancelPreview: Story = {
  play: async ({ canvasElement }) => {
    const { canvas, portal, dialog } = await openSearch(canvasElement);
    await userEvent.type(dialog.getByRole('textbox', { name: 'DOI' }), doi);
    await userEvent.click(dialog.getByRole('button', { name: 'Search', exact: true }));
    await expect(await dialog.findByRole('heading', { name: 'Autofill preview' })).toBeInTheDocument();
    await expect(dialog.getAllByText(title)).toHaveLength(1);
    await userEvent.click(dialog.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(portal.queryByRole('dialog')).not.toBeInTheDocument());
    await expect(canvas.queryByDisplayValue(title)).not.toBeInTheDocument();
    await expect(canvas.getByRole('heading', { name: '<title>' })).toBeInTheDocument();
  },
};
