import type { Meta, StoryObj } from '@storybook/react-vite';
import { screen, fn, within, expect, userEvent, waitFor, fireEvent } from 'storybook/test';
import TermSearch from "./TermSearch.fs.js";
import * as Provider from "./ConfigProvider.fs.js";
import { TIBApi } from '../../Api/TIBApi.fs.js';
import React from 'react';
import {
  Swate_Components_Api_TIBApi_TIBTypes_SearchApi__SearchApi_ToMyTerm as toMyTerm,
  type Term,
} from './Types.fs.js';

// Waits for a live term search response get more time, because the API can be slow.
const LIVE_API_TIMEOUT = 15_000;

const TERMSEARCH_INPUT_TESTID = 'term-search-input'

const TERMSEARCH_DETAILSMODAL_TESTID = 'modal_termsearch_details_modal'

const toTerms = (searchApi: any): Term[] => (searchApi ? Array.from(toMyTerm(searchApi)) : []);

const mockInstrumentModelSearch = async (query: string): Promise<Term[]> =>
  query.trim()
    ? [
      {
        name: 'LECO instrument model',
        id: 'MS:1001800',
        description: 'Instrument model used by LECO.',
        source: 'MS',
      },
    ]
    : [];

function renderTermSearch(args: any) {
  const [term, setTerm] = React.useState(undefined as Term | undefined);

  return (
    <div className='swt:container swt:mx-auto swt:flex swt:flex-col swt:p-2 swt:gap-4 swt:h-100'>
      <TermSearch
        {...args}
        term={term}
        onTermChange={(selectedTerm) => {
          setTerm(selectedTerm as Term | undefined);
          args.onTermChange(selectedTerm); // Call mock or external handler
        }}
      />
    </div>
  );
};

const meta = {
  title: "Composite Components/TermSearch",
  tags: ["autodocs"],
  parameters: {
    // Optional parameter to center the component in the Canvas. More info: https://storybook.js.org/docs/configure/story-layout
    layout: 'centered',
  },
  component: TermSearch,
} satisfies Meta<typeof TermSearch>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
  render: renderTermSearch,
  args: {
    onTermChange: fn(),
    term: undefined,
  },
  play: async ({ args, canvasElement }) => {
    const input = within(canvasElement).getByTestId(TERMSEARCH_INPUT_TESTID);
    expect(input).toBeInTheDocument();
    await userEvent.type(input, "instrument model", {delay: 50});

    await waitFor(() => expect(args.onTermChange).toHaveBeenCalled());
  }
}

export const ParentSearch: Story = {
  render: renderTermSearch,
  parameters: {isolated: true},
  args: {
    onTermChange: fn(),
    term: undefined,
    parentId: "MS:1000031",

  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);
    const input = canvas.getByTestId(TERMSEARCH_INPUT_TESTID);
    expect(input).toBeInTheDocument();

    await userEvent.type(input, "SCIEX instrument", {delay: 50});

    await waitFor(() => expect(args.onTermChange).toHaveBeenCalled());

    await waitFor(() => { // await api call response
      const directedOutput = screen.getByText("SCIEX instrument model");
      expect(directedOutput).toBeInTheDocument();
      const expectedIcons = screen.getAllByTitle("Directed Search");
      expect(expectedIcons.length).toBeGreaterThan(0);
    }, { timeout: LIVE_API_TIMEOUT });
  }
}

export const DoesNotSelectFirstResultOnEnter: Story = {
  render: renderTermSearch,
  parameters: {isolated: true},
  args: {
    onTermChange: fn(),
    term: undefined,
    disableDefaultSearch: true,
    termSearchQueries: [["mock_search", mockInstrumentModelSearch]],
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const input = canvas.getByTestId(TERMSEARCH_INPUT_TESTID);
    expect(input).toBeInTheDocument();

    await userEvent.type(input, "leco instrument", {delay: 50});

    await waitFor(() => {
      expect(screen.getByText("LECO instrument model")).toBeVisible();
    });

    await userEvent.keyboard("{Enter}");

    expect(input).toHaveValue("leco instrument");
  }
}


export const DefaultAdvancedSearch: Story = {
  render: renderTermSearch,
  parameters: {isolated: true},
  args: {
    onTermChange: fn(),
    term: undefined,
    parentId: "MS:1000031",

  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);
    const input = canvas.getByTestId(TERMSEARCH_INPUT_TESTID);

    expect(input).toBeInTheDocument();

    await userEvent.click(input);
    await fireEvent.keyDown(input, { key: 'F2', code: 'F2' })

    const modal = await waitFor(() => screen.getByTestId(TERMSEARCH_DETAILSMODAL_TESTID));
    expect(modal).toBeInTheDocument();

    await userEvent.click(screen.getByTestId('advanced_search_btn'))

    const modalInput = within(modal).getByTestId("advanced-search-term-name-input");
    expect(modalInput).toBeInTheDocument();

    await userEvent.type(modalInput, "instrument model", {delay: 50});
    await fireEvent.keyDown(modalInput, { key: "Enter", code: "Enter" });

    await waitFor(() => {
      const results = screen.getByText(/Results: \d+/);
      expect(results).toBeInTheDocument();
    }, { timeout: LIVE_API_TIMEOUT });
  }
}

export const TIBSearch: Story = {
  render: renderTermSearch,
  args: {
    term: undefined,
    parentId: "MS:1000031",
    onTermChange: fn(),
    disableDefaultSearch: true,
    disableDefaultParentSearch: true,
    disableDefaultAllChildrenSearch: true,
    termSearchQueries: [
      [
        "tib_search",
        (query): Promise<Term[]> =>
          TIBApi.defaultSearch(query, 10, "DataPLANT").then((searchApi) => toTerms(searchApi)),
      ]
    ],
    parentSearchQueries: [
      [
        "tib_search",
        ([parentId, query]): Promise<Term[]> =>
          TIBApi.searchChildrenOf(query, parentId, 10, "DataPLANT").then((searchApi) => toTerms(searchApi)),
      ]
    ],
    allChildrenSearchQueries: [
      [
        "tib_search",
        (parentId): Promise<Term[]> =>
          TIBApi.searchAllChildrenOf(parentId, 500, "DataPLANT").then((searchApi) => toTerms(searchApi)),
      ]
    ]
  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);
    const input = canvas.getByTestId('term-search-input');
    expect(input).toBeInTheDocument();

    await userEvent.type(input, "SCIEX instrument", {delay: 50});

    await waitFor(() => expect(args.onTermChange).toHaveBeenCalled());

    await waitFor(() => { // await api call response
      const directedOutput = screen.getByText("SCIEX instrument model");
      expect(directedOutput).toBeInTheDocument();
      const expectedIcons = screen.getAllByTitle("Directed Search");
      expect(expectedIcons.length).toBeGreaterThan(0);
    }, { timeout: LIVE_API_TIMEOUT });

    await userEvent.clear(input);

    await userEvent.keyboard("{Escape}");

    await userEvent.click(input);

    await userEvent.keyboard("{ArrowDown}")

    await waitFor(() => { // await api call response
      const debugValue = input.getAttribute("data-debugresultcount")
      expect(debugValue ? parseInt(debugValue, 10) : 0).toBeGreaterThan(0);
    }, { timeout: LIVE_API_TIMEOUT });
  }
}

export const WithSearchConfigProvider: Story = {
  render: renderTermSearch,
  args: {
    onTermChange: fn(),
    term: undefined,
    parentId: undefined,
    disableDefaultSearch: true,
    disableDefaultParentSearch: true,
    disableDefaultAllChildrenSearch: true,
  },
  decorators: [
    (Story) => (
      <Provider.TIBQueryProvider>
        <Story />
      </Provider.TIBQueryProvider>
    )
  ],
  play: async ({ args, canvasElement }) => {
    const input = within(canvasElement).getByTestId(TERMSEARCH_INPUT_TESTID);
    expect(input).toBeInTheDocument();
    await userEvent.type(input, "instrument model", {delay: 50});

    await waitFor(() => expect(args.onTermChange).toHaveBeenCalled());
  }
}
