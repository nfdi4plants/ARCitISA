import type { Meta, StoryObj } from '@storybook/react-vite';
import { screen, fn, within, expect, userEvent, waitFor, fireEvent } from 'storybook/test';
import TermSearch from "./TermSearch.fs.js";
// Use the explicit provider that accepts injected queries
import { TermSearchConfigProvider as TermSearchConfigProviderComponent } from "./ConfigProvider.fs.js";
import type { Term } from '../Util/Types.fs.js';
import React from 'react';

const TERMSEARCH_INPUT_TESTID = 'term-search-input'

// Simple mock dataset for deterministic tests
const MOCK_TERMS: Term[] = [
  {
    name: "Instrument model",
    id: "MS:1000031",
    description: "An instrument model term",
    source: "MS",
    href: "https://www.ebi.ac.uk/ols/ontologies/ms",
    isObsolete: false,
  },
  {
    name: "SCIEX instrument model",
    id: "MS:1001832",
    description: "SCIEX instrument model term",
    source: "MS",
    href: "https://www.ebi.ac.uk/ols/ontologies/ms",
    isObsolete: false,
  },
];

const mockTermSearch = async (query: string): Promise<Term[]> => {
  const q = (query || "").toLowerCase();
  if (!q) return [];
  return MOCK_TERMS.filter(t => (t.name ?? "").toLowerCase().includes(q));
};

const mockParentSearch = async ([parentId, query]: [string, string]): Promise<Term[]> => {
  const q = (query || "").toLowerCase();
  if (!q) return [];
  // Return terms that include query and pretend they are children of parentId
  return MOCK_TERMS.filter(t => (t.name ?? "").toLowerCase().includes(q)).map(t => ({ ...t }));
};

const mockAllChildrenSearch = async (parentId: string): Promise<Term[]> => {
  // Return all known terms as children for simplicity
  return MOCK_TERMS.map(t => ({ ...t }));
};

const LOCAL_STORAGE_KEY = 'arcitisa-termsearchconfig-ctx-mock';

const meta = {
  title: "Composite Components/TermSearch/TermSearchConfigProvider",
  tags: ["autodocs"],
  parameters: {
    // Optional parameter to center the component in the Canvas. More info: https://storybook.js.org/docs/configure/story-layout
    layout: 'padded',
  },
  async beforeEach() {
    localStorage.clear();
    // Activate our mock query key so provider uses it immediately
    localStorage.setItem(LOCAL_STORAGE_KEY, JSON.stringify({
      disableDefault: false,
      activeKeys: ["mock_search", "mock_parent", "mock_children"],
    }));
  },
  component: TermSearchConfigProviderComponent,
  subcomponents: {TermSearch},
} satisfies Meta<typeof TermSearchConfigProviderComponent>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
  args: {
    // Inject mocked queries into the provider and ensure they are active
    allTermSearchQueries: [["mock_search", mockTermSearch]],
    allParentSearchQueries: [["mock_parent", mockParentSearch]],
    allAllChildrenSearchQueries: [["mock_children", mockAllChildrenSearch]],
    localStorageKey: LOCAL_STORAGE_KEY,
    children: (
      <TermSearch
        onTermChange={fn()}
        disableDefaultSearch={true}
        disableDefaultParentSearch={true}
        disableDefaultAllChildrenSearch={true}
      />
    ),
  },
  play: async ({ args, canvasElement }) => {

    const input = within(canvasElement).getByTestId(TERMSEARCH_INPUT_TESTID);
    expect(input).toBeInTheDocument();

    await userEvent.type(input, "instrument model", {delay: 50});

    await waitFor(() => {
      const debugValue = input.getAttribute("data-debugresultcount")
      console.log(debugValue)
      expect(debugValue ? parseInt(debugValue) : 0).toBeGreaterThan(0);
    }, {timeout: 10000});

  }
}
