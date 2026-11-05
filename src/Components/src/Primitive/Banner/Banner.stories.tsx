import type { Meta, StoryObj } from '@storybook/react-vite';
import React, { useState } from 'react';
import { expect, userEvent, within } from 'storybook/test';
import { Banner } from './Banner.fs.js';
import Layout from '../../Composite/Layout/Layout.fs.js';

const OneLineExample = () => {
  const [lastAction, setLastAction] = useState('none');

  return (
    <div className="swt:w-full swt:space-y-3">
      <Banner
        message="Git could not be started."
        actions={[
          { Label: 'Choose...', OnClick: () => setLastAction('Choose'), Icon: 'swt:fluent--folder-open-24-regular', Disabled: false },
          { Label: 'Check again', OnClick: () => setLastAction('Check again'), Icon: 'swt:fluent--arrow-sync-24-regular', Disabled: false },
        ]}
        testId="BannerOneLine"
      />
      <span data-testid="BannerLastAction">Last action: {lastAction}</span>
    </div>
  );
};

const meta = {
  title: 'Primitive Components/Banner',
  tags: ['autodocs'],
  parameters: {
    layout: 'fullscreen',
  },
  component: OneLineExample,
} satisfies Meta<typeof OneLineExample>;

export default meta;

type Story = StoryObj<typeof meta>;

export const OneLineTwoActions: Story = {
  render: () => <OneLineExample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const banner = canvas.getByRole('alert');

    await expect(banner).toBe(canvas.getByTestId('BannerOneLine'));
    await expect(canvas.getByTestId('BannerOneLineLine0')).toHaveTextContent('Git could not be started.');
    await expect(canvas.queryByTestId('BannerOneLineLine1')).toBeNull();

    // The banner has no close control, so the two actions are its only buttons.
    await expect(within(banner).getAllByRole('button')).toHaveLength(2);

    await userEvent.click(canvas.getByTestId('BannerOneLineAction1'));
    await expect(canvas.getByTestId('BannerLastAction')).toHaveTextContent('Last action: Check again');
  },
};

export const MultiLine: Story = {
  render: () => (
    <Banner
      message={'Git is not installed or not on the PATH.\nInstall Git, then check again.\nGit LFS is optional.'}
      actions={[{ Label: 'Check again', OnClick: () => {}, Disabled: false }]}
      testId="BannerMultiLine"
    />
  ),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await expect(canvas.getByTestId('BannerMultiLineLine0')).toHaveTextContent('Git is not installed or not on the PATH.');
    await expect(canvas.getByTestId('BannerMultiLineLine1')).toHaveTextContent('Install Git, then check again.');
    await expect(canvas.getByTestId('BannerMultiLineLine2')).toHaveTextContent('Git LFS is optional.');
    await expect(canvas.queryByTestId('BannerMultiLineLine3')).toBeNull();

    // Each line sits below the previous one.
    const first = canvas.getByTestId('BannerMultiLineLine0').getBoundingClientRect();
    const second = canvas.getByTestId('BannerMultiLineLine1').getBoundingClientRect();
    await expect(second.top).toBeGreaterThanOrEqual(first.bottom - 1);
  },
};

export const DisabledAction: Story = {
  render: () => {
    const onClick = () => {};
    return (
      <Banner
        message="Checking the Git executable."
        actions={[
          { Label: 'Check again', OnClick: onClick, Disabled: true },
          { Label: 'Choose...', OnClick: onClick, Disabled: false },
        ]}
        testId="BannerDisabled"
      />
    );
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await expect(canvas.getByTestId('BannerDisabledAction0')).toBeDisabled();
    await expect(canvas.getByTestId('BannerDisabledAction1')).toBeEnabled();
  },
};

export const NarrowWidth: Story = {
  render: () => (
    <div className="swt:w-72">
      <Banner
        message="Git could not be started and the last check failed."
        actions={[
          { Label: 'Choose...', OnClick: () => {}, Disabled: false },
          { Label: 'Check again', OnClick: () => {}, Disabled: false },
        ]}
        testId="BannerNarrow"
        onClose={() => {}}
      />
    </div>
  ),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const banner = canvas.getByTestId('BannerNarrow');

    // Nothing is cut off or sticks out when the width runs out.
    await expect(banner.scrollWidth).toBeLessThanOrEqual(banner.clientWidth);
    for (const button of within(banner).getAllByRole('button')) {
      await expect(button.getBoundingClientRect().right).toBeLessThanOrEqual(banner.getBoundingClientRect().right + 1);
    }
  },
};

export const Variants: Story = {
  render: () => (
    <div className="swt:w-full swt:space-y-3">
      <Banner message="A newer version is available." variant="info" testId="BannerInfo" />
      <Banner message="All files were downloaded." variant="success" testId="BannerSuccess" />
      <Banner message="Git LFS is too old." variant="warning" testId="BannerWarning" />
      <Banner message="Git could not be started." variant="error" testId="BannerError" />
      <Banner
        message="A custom icon replaces the icon of the variant."
        variant="info"
        icon="swt:fluent--cloud-arrow-down-24-regular"
        testId="BannerCustomIcon"
      />
    </div>
  ),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const background = (testId: string) => getComputedStyle(canvas.getByTestId(testId)).backgroundColor;

    // Each variant has its own colour.
    const colours = new Set(['BannerInfo', 'BannerSuccess', 'BannerWarning', 'BannerError'].map(background));
    await expect(colours.size).toBe(4);

    await expect(canvas.getByTestId('BannerCustomIconIcon')).toHaveClass('swt:fluent--cloud-arrow-down-24-regular');
    await expect(canvas.getByTestId('BannerCustomIconIcon')).not.toHaveClass('swt:fluent--info-24-regular');
  },
};

export const InLayoutMain: Story = {
  name: 'In Layout.Main',
  parameters: { isolated: true },
  render: () => (
    <Layout
      banners={[
        { Key: 'BannerInLayoutFirst', Message: 'Git could not be started.', Variant: 'warning', Actions: [{ Label: 'Check again', OnClick: () => {}, Disabled: false }] },
        { Key: 'BannerInLayoutSecond', Message: 'A newer version is available.', Variant: 'info', Actions: [] },
      ]}
      navbar={<div className="swt:flex swt:items-center swt:justify-center swt:h-full swt:grow">Navbar</div>}
    >
      <div className="swt:flex swt:items-center swt:justify-center swt:h-full">Main Content</div>
    </Layout>
  ),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const shown = within(canvas.getByTestId('layout-main-banners')).getAllByRole('alert').map(b => b.dataset.testid);

    // The banners show in the order of the list.
    await expect(shown).toEqual(['BannerInLayoutFirst', 'BannerInLayoutSecond']);
  },
};

const ToggleBannersInLayout = () => {
  const [shown, setShown] = useState(true);
  const banners = shown ? [{ Key: 'BannerToggled', Message: 'Git could not be started.', Variant: 'warning', Actions: [] }] : [];

  return (
    <Layout
      banners={banners}
      navbar={
        <button type="button" data-testid="BannerToggle" onClick={() => setShown(value => !value)}>
          Toggle banner
        </button>
      }
    >
      <div data-testid="BannerToggleContent">Main Content</div>
    </Layout>
  );
};

export const InLayoutMainKeepsContent: Story = {
  name: 'In Layout.Main keeps the content when banners come and go',
  parameters: { isolated: true },
  render: () => <ToggleBannersInLayout />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const content = canvas.getByTestId('BannerToggleContent');

    // A remount would put a new content node on screen.
    await userEvent.click(canvas.getByTestId('BannerToggle'));
    await expect(canvas.queryByTestId('BannerToggled')).toBeNull();
    await expect(canvas.getByTestId('BannerToggleContent')).toBe(content);

    await userEvent.click(canvas.getByTestId('BannerToggle'));
    await expect(canvas.getByTestId('BannerToggled')).toBeInTheDocument();
    await expect(canvas.getByTestId('BannerToggleContent')).toBe(content);
  },
};

const NewsAndWarningInLayout = () => {
  const [newsShown, setNewsShown] = useState(true);
  const warning = {
    Key: 'BannerWarningEntry',
    Message: 'Git could not be started.',
    Variant: 'warning',
    Actions: [{ Label: 'Check again', OnClick: () => {}, Disabled: false }],
  };
  const news = {
    Key: 'BannerNewsEntry',
    Message: 'Version 2.0 is out. See what is new.',
    Variant: 'info',
    Actions: [{ Label: 'Read more', OnClick: () => {}, Disabled: false }],
    OnClose: () => setNewsShown(false),
  };

  return (
    <Layout
      banners={newsShown ? [warning, news] : [warning]}
      navbar={<div className="swt:flex swt:items-center swt:justify-center swt:h-full swt:grow">Navbar</div>}
    >
      <div data-testid="BannerNewsContent">Main Content</div>
    </Layout>
  );
};

export const NewsAndWarningInLayoutMain: Story = {
  name: 'News and warning in Layout.Main',
  parameters: { isolated: true },
  render: () => <NewsAndWarningInLayout />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const content = canvas.getByTestId('BannerNewsContent');
    const warning = canvas.getByTestId('BannerWarningEntry');

    // Only the news has a close handler, so only the news can be closed.
    await expect(canvas.queryByTestId('BannerWarningEntryClose')).toBeNull();
    await expect(canvas.getByTestId('BannerNewsEntryClose')).toBeInTheDocument();

    await userEvent.click(canvas.getByTestId('BannerNewsEntryClose'));

    await expect(canvas.queryByTestId('BannerNewsEntry')).toBeNull();
    await expect(canvas.getByTestId('BannerWarningEntry')).toBe(warning);
    await expect(canvas.getByTestId('BannerNewsContent')).toBe(content);
  },
};

export const InLayoutMainWithoutBanners: Story = {
  name: 'In Layout.Main without banners',
  parameters: { isolated: true },
  render: () => (
    <Layout
      banners={[]}
      navbar={<div className="swt:flex swt:items-center swt:justify-center swt:h-full swt:grow">Navbar</div>}
    >
      <div className="swt:flex swt:items-center swt:justify-center swt:h-full">Main Content</div>
    </Layout>
  ),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await expect(canvas.queryByTestId('layout-main-banners')).toBeNull();
  },
};
