import type { Meta, StoryObj } from '@storybook/react-vite';
import React, { useState } from 'react';
import { expect, userEvent, within } from 'storybook/test';
import { Blankslate } from './Blankslate.fs.js';
import { listOfArray as ofArray } from '../../Util/Interop.fs.js';

const BasicExample = () => (
  <div className="swt:w-105 swt:max-w-full">
    <Blankslate
      title="No files found"
      description="Add files to this workspace to continue."
      iconClassName="swt:fluent--document-folder-24-regular"
      fullHeight={false}
      testId="BlankslateBasic"
    />
  </div>
);

const ActionExample = () => {
  const [lastAction, setLastAction] = useState('none');

  const primaryActions = ofArray([
    {
      Label: 'Create file',
      OnClick: () => setLastAction('Create file'),
      IconClassName: 'swt:fluent--document-add-24-regular',
      Disabled: false,
      Color: "secondary",
    },
  ]) as any;

  const secondaryActions = ofArray([
    {
      Label: 'Open docs',
      OnClick: () => setLastAction('Open docs'),
      IconClassName: 'swt:fluent--book-information-24-regular',
      Disabled: false,
    },
  ]) as any;

  return (
    <div className="swt:w-105 swt:max-w-full swt:space-y-3">
      <Blankslate
        title="Repository is empty"
        description="Create the first file or open docs for setup guidance."
        iconClassName="swt:fluent--branch-fork-24-regular"
        textSize="large"
        primaryActions={primaryActions}
        secondaryActions={secondaryActions}
        trailingElement={
          <div className="swt:alert swt:alert-warning swt:px-3 swt:py-2 swt:text-sm">
            <span className="swt:iconify swt:fluent--warning-shield-20-regular swt:size-4" />
            <span>You need write permissions to push changes.</span>
          </div>
        }
        fullHeight={false}
      />
      <span>Last action: {lastAction}</span>
    </div>
  );
};

const RepositorySetupExample = () => {
  const [projectName, setProjectName] = useState('my-arc');
  const [lastAction, setLastAction] = useState('none');

  const trimmedProjectName = projectName.trim();
  const canInitialize = trimmedProjectName.length > 0;

  const primaryActions = ofArray([
    {
      Label: 'Initialize repository',
      OnClick: () => setLastAction(`Initialize: ${trimmedProjectName}`),
      IconClassName: 'swt:fluent--branch-fork-24-regular',
      Disabled: !canInitialize,
      Color: "secondary",
    },
    {
      Label: 'Open setup docs',
      OnClick: () => setLastAction('Open setup docs'),
      IconClassName: 'swt:fluent--book-open-24-regular',
      Disabled: false,
      Color: "primary",
    },
  ]) as any;

  const secondaryActions = ofArray([
    {
      Label: 'Skip for now',
      OnClick: () => setLastAction('Skip for now'),
      IconClassName: 'swt:fluent--dismiss-circle-24-regular',
      Disabled: false,
    },
  ]) as any;

  return (
    <div className="swt:w-105 swt:max-w-full swt:space-y-3">
      <Blankslate
        title="Initialize Git for this ARC"
        description="The selected ARC folder is not a Git repository yet."
        iconClassName="swt:fluent--branch-fork-24-regular"
        primaryActions={primaryActions}
        secondaryActions={secondaryActions}
        leadingElement={
          <div className="swt:w-full swt:max-w-sm swt:text-left swt:space-y-1">
            <label
              className="swt:text-xs swt:font-medium swt:text-base-content/70"
              htmlFor="repository-name-input"
            >
              DataHub repository name
            </label>
            <input
              id="repository-name-input"
              aria-label="DataHub repository name"
              className="swt:input swt:input-bordered swt:w-full"
              placeholder="my-arc"
              value={projectName}
              onChange={(event) => setProjectName((event.target as HTMLInputElement).value)}
            />
          </div>
        }
        fullHeight={false}
      />
      <span>Last action: {lastAction}</span>
    </div>
  );
};

const meta = {
  title: 'Primitive Components/Blankslate',
  tags: ['autodocs'],
  parameters: {
    layout: 'centered',
    viewport: { defaultViewport: 'responsive' },
  },
  component: BasicExample,
} satisfies Meta<typeof BasicExample>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Basic: Story = {
  render: () => <BasicExample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    expect(canvas.getByText('No files found')).toBeInTheDocument();
    expect(canvas.getByText('Add files to this workspace to continue.')).toBeInTheDocument();
  },
};

export const WithActions: Story = {
  render: () => <ActionExample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await userEvent.click(canvas.getByRole('button', { name: /create file/i }));

    expect(canvas.getByText('Last action: Create file')).toBeInTheDocument();
    expect(canvas.getByText('You need write permissions to push changes.')).toBeInTheDocument();
  },
};

export const RepositorySetup: Story = {
  render: () => <RepositorySetupExample />,
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const projectNameInput = canvas.getByRole('textbox', { name: /datahub repository name/i });

    await userEvent.clear(projectNameInput);
    await userEvent.type(projectNameInput, 'plant-arc');
    await userEvent.click(canvas.getByRole('button', { name: /initialize repository/i }));

    expect(canvas.getByText('Last action: Initialize: plant-arc')).toBeInTheDocument();
  },
};
