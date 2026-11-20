import type { Meta, StoryObj } from '@storybook/react-vite';
import { within, expect, waitFor } from 'storybook/test';
import {
  GitHubFlavored as GitHubFlavoredSample,
  WithContext as WithContextSample,
  WithoutContext as WithoutContextSample,
} from './MarkdownTextViewer.sample.fs.js';

const meta = {
  title: 'Composite Components/MarkdownTextViewer',
  parameters: { layout: 'fullscreen' },
} satisfies Meta;

export default meta;
type Story = StoryObj<typeof meta>;

/** Images of the ARC load, or explain why they do not. */
export const WithContext: Story = {
  render: () => <WithContextSample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await waitFor(
      () => expect(canvas.getByText('The file docs/missing.png does not exist in the ARC.')).toBeInTheDocument(),
      { timeout: 30_000 },
    );
    await waitFor(() =>
      expect(canvas.getByText('The file docs/lfs.png is stored in Git LFS and is not downloaded.')).toBeInTheDocument(),
    );
    expect(canvas.getByText('This image lies outside the ARC.')).toBeInTheDocument();
    expect(canvasElement.querySelector('img[src^="data:image/svg+xml"]')).not.toBeNull();
  },
};

/** Without a context, the images of the ARC are notices and the links to files are plain text. */
export const WithoutContext: Story = {
  render: () => <WithoutContextSample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await waitFor(() =>
      expect(canvas.getAllByText('Images of the ARC are shown only for a file of an open ARC.').length).toBeGreaterThan(0),
    );
    expect(canvas.getByRole('link', { name: 'external' })).toBeInTheDocument();
  },
};

/** Alerts get a title and an icon, and the link of a heading shows while the pointer is over the heading. */
export const GitHubFlavored: Story = {
  render: () => <GitHubFlavoredSample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    for (const title of ['Note', 'Tip', 'Important', 'Warning', 'Caution']) {
      expect(canvas.getByText(title, { selector: 'p' })).toBeVisible();
    }
    expect(canvas.getByRole('table')).toBeVisible();
    expect(canvas.getAllByRole('checkbox')).toHaveLength(2);

    // The link is hidden until the pointer is over the heading, which a synthetic hover does not trigger.
    const heading = canvas.getByRole('heading', { name: 'Lists and tables' });
    const headingLink = heading.querySelector('a')!;
    expect(headingLink.getAttribute('href')).toBe(`#${heading.id}`);
    expect(getComputedStyle(headingLink).opacity).toBe('0');
  },
};
