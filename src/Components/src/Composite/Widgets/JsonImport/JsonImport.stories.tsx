import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, userEvent, within } from 'storybook/test';
import {
  ReviewSample,
  ReplacementSample,
  ImportingSample,
  SuccessSample,
  ErrorSample,
} from './JsonImport.sample.fs.js';

// The modal renders in a portal, so the play functions query the document body.
const meta = {
  title: 'Composite Components/JsonImport',
  component: ReviewSample,
  tags: ['autodocs'],
  parameters: { layout: 'fullscreen' },
} satisfies Meta<typeof ReviewSample>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Review: Story = {
  render: () => <ReviewSample />,
  play: async () => {
    const body = within(document.body);
    expect(await body.findByTestId('json-import-summary')).toBeInTheDocument();
    expect(body.getByTestId('json-import-confirm')).toHaveTextContent('Import');
  },
};

export const Replacement: Story = {
  render: () => <ReplacementSample />,
  play: async () => {
    const body = within(document.body);
    expect(await body.findByTestId('json-import-replace-warning')).toBeInTheDocument();
    expect(body.getByTestId('json-import-confirm')).toHaveTextContent('Replace');
  },
};

export const Importing: Story = {
  render: () => <ImportingSample />,
  play: async () => {
    const body = within(document.body);
    await userEvent.click(await body.findByTestId('json-import-confirm'));
    expect(await body.findByTestId('json-import-progress')).toBeInTheDocument();
  },
};

export const Success: Story = {
  render: () => <SuccessSample />,
  play: async () => {
    const body = within(document.body);
    await userEvent.click(await body.findByTestId('json-import-confirm'));
    expect(await body.findByTestId('json-import-success')).toBeInTheDocument();
  },
};

export const Failure: Story = {
  render: () => <ErrorSample />,
  play: async () => {
    const body = within(document.body);
    await userEvent.click(await body.findByTestId('json-import-confirm'));
    expect(await body.findByTestId('json-import-error')).toBeInTheDocument();
  },
};
