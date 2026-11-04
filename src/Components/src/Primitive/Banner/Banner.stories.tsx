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
      />
    </div>
  ),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const message = canvas.getByTestId('BannerNarrowMessage').getBoundingClientRect();
    const actions = canvas.getByTestId('BannerNarrowActions').getBoundingClientRect();

    // The actions wrap below the message when the width runs out.
    await expect(actions.top).toBeGreaterThanOrEqual(message.bottom - 1);
  },
};

export const InLayoutMain: Story = {
  name: 'In Layout.Main',
  parameters: { isolated: true },
  render: () => (
    <Layout
      banner={
        <Banner
          message="Git could not be started."
          actions={[{ Label: 'Check again', OnClick: () => {}, Disabled: false }]}
          testId="BannerInLayout"
        />
      }
      navbar={<div className="swt:flex swt:items-center swt:justify-center swt:h-full swt:grow">Navbar</div>}
    >
      <div className="swt:flex swt:items-center swt:justify-center swt:h-full">Main Content</div>
    </Layout>
  ),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const banner = canvas.getByTestId('layout-main-banner');
    const navbar = canvas.getByTestId('layout-main-navbar');

    await expect(banner).toContainElement(canvas.getByTestId('BannerInLayout'));
    // The banner sits above the navbar and spans the full width.
    await expect(banner.getBoundingClientRect().bottom).toBeLessThanOrEqual(navbar.getBoundingClientRect().top + 1);
    await expect(banner.getBoundingClientRect().width).toBe(canvasElement.getBoundingClientRect().width);
  },
};
