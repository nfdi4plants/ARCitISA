import React from 'react';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { within, expect, userEvent, waitFor } from 'storybook/test';
import { Entry as ThemeSelector } from './ThemeSelector.fs.js';


const meta = {
  title: 'Composite Components/ThemeSelector',
  tags: ['autodocs'],
  parameters: {
    layout: 'centered',
  },
  component: ThemeSelector,
} satisfies Meta<typeof ThemeSelector>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
  play: async ({ canvasElement }) => {
    const select = within(canvasElement).getByRole("combobox");
    const root = document.documentElement;

    // The options are named after the look, the attribute after the daisyUI theme.
    await userEvent.selectOptions(select, "dark");
    await waitFor(() => expect(root).toHaveAttribute("data-theme", "finster"));
    await userEvent.selectOptions(select, "light");
    await waitFor(() => expect(root).toHaveAttribute("data-theme", "sunrise"));

    // Auto is the stored default, so later stories start without a leftover theme.
    await userEvent.selectOptions(select, "auto");
    await waitFor(() => expect(root).not.toHaveAttribute("data-theme"));
  },
};