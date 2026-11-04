import React from 'react';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { within, expect, userEvent, waitFor, fn } from 'storybook/test';
import { Entry as SettingsPage, SettingsPage as SettingsPageWithProps } from './SettingsPage.fs.js';
import { ThemeProvider } from '../../Composite/ThemeSelector/ThemeProvider.fs.js';
import { TIBQueryProvider } from '../../Composite/TermSearch/ConfigProvider.fs.js';


const meta = {
  title: 'Page Components/SettingsPage',
  tags: ['autodocs'],
  parameters: {
    layout: 'fullscreen',
  },
  component: SettingsPage,
} satisfies Meta<typeof SettingsPage>;

export default meta;

type Story = StoryObj<typeof meta>;
type PropsStory = StoryObj<typeof SettingsPageWithProps>;

const renderWithProps = (args: React.ComponentProps<typeof SettingsPageWithProps>) => (
  <ThemeProvider>
    <TIBQueryProvider>
      <SettingsPageWithProps {...args} />
    </TIBQueryProvider>
  </ThemeProvider>
);

const autoCreateNotesFolderKey = 'swate-settings-auto-create-notes-folder';

const lfsId = 'SettingsGitDefaultLfsThreshold';
const downloadId = 'SettingsGitDefaultDownloadLargeFiles';
const diffId = 'SettingsGitDefaultDiffIndexingLimit';
const uploadId = 'SettingsGitDefaultUploadRightAway';

const builtInGitDefaults = {
  LfsAutoTrackThresholdMb: 10,
  DownloadLargeFiles: true,
  DiffIndexingLimitMb: 1024,
  UploadRightAway: true,
};

// Only "download large files" differs from the built-in values.
const currentGitDefaults = {
  ...builtInGitDefaults,
  DownloadLargeFiles: false,
};

const gitDefaultsArgs = () => ({
  gitDefaults: currentGitDefaults,
  gitBuiltInDefaults: builtInGitDefaults,
  onGitDefaultsSave: fn(),
});

const replaceNumber = async (input: HTMLElement, value: string) => {
  await userEvent.clear(input);
  if (value !== '') {
    await userEvent.type(input, value);
  }
};

export const Default: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await expect(canvas.getByTestId('SettingsAutoCreateNotesFolder')).toBeInTheDocument();
    await expect(canvas.queryByTestId('SettingsGitDefaultsSection')).toBeNull();
  },
};

export const GitDefaults: PropsStory = {
  render: renderWithProps,
  args: gitDefaultsArgs(),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await expect(canvas.getByTestId('SettingsGitDefaultsSection')).toBeInTheDocument();
    await expect(canvas.getByTestId(lfsId)).toHaveValue(10);
    await expect(canvas.getByTestId(downloadId)).not.toBeChecked();
    await expect(canvas.getByTestId(diffId)).toHaveValue(1024);
    await expect(canvas.getByTestId(uploadId)).toBeChecked();

    // Only the field that differs from the built-in value is marked.
    await expect(canvas.getByTestId(`${downloadId}Changed`)).toBeInTheDocument();
    await expect(canvas.getByTestId(`${downloadId}Reset`)).toBeInTheDocument();
    for (const id of [lfsId, diffId, uploadId]) {
      await expect(canvas.queryByTestId(`${id}Changed`)).toBeNull();
      await expect(canvas.queryByTestId(`${id}Reset`)).toBeNull();
    }

    await expect(canvas.getByTestId('SettingsGitDefaultsSave')).toBeDisabled();
  },
};

export const GitDefaultsChangeAndReset: PropsStory = {
  render: renderWithProps,
  args: gitDefaultsArgs(),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const save = canvas.getByTestId('SettingsGitDefaultsSave');

    await replaceNumber(canvas.getByTestId(lfsId), '25');
    await expect(canvas.getByTestId(`${lfsId}Changed`)).toBeInTheDocument();
    await expect(save).toBeEnabled();

    await userEvent.click(canvas.getByTestId(`${lfsId}Reset`));
    await expect(canvas.getByTestId(lfsId)).toHaveValue(10);
    await waitFor(() => expect(canvas.queryByTestId(`${lfsId}Changed`)).toBeNull());
    await expect(save).toBeDisabled();

    // Resetting a toggle that differs from the built-in value sets it back to the built-in value.
    await userEvent.click(canvas.getByTestId(`${downloadId}Reset`));
    await expect(canvas.getByTestId(downloadId)).toBeChecked();
    await waitFor(() => expect(canvas.queryByTestId(`${downloadId}Changed`)).toBeNull());
    await expect(save).toBeEnabled();
  },
};

export const GitDefaultsOutOfRange: PropsStory = {
  render: renderWithProps,
  args: gitDefaultsArgs(),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const save = canvas.getByTestId('SettingsGitDefaultsSave');
    const lfs = canvas.getByTestId(lfsId);
    const diff = canvas.getByTestId(diffId);

    await userEvent.click(canvas.getByTestId(uploadId));
    await expect(save).toBeEnabled();
    await expect(canvas.queryByTestId(`${lfsId}Range`)).toBeNull();

    for (const value of ['101', '0', '1.5', '']) {
      await replaceNumber(lfs, value);
      await expect(save).toBeDisabled();
      await expect(canvas.getByTestId(`${lfsId}Range`)).toBeInTheDocument();
    }

    await replaceNumber(lfs, '100');
    await expect(save).toBeEnabled();
    await expect(canvas.queryByTestId(`${lfsId}Range`)).toBeNull();

    for (const value of ['1048577', '0', '']) {
      await replaceNumber(diff, value);
      await expect(save).toBeDisabled();
      await expect(canvas.getByTestId(`${diffId}Range`)).toBeInTheDocument();
    }

    await replaceNumber(diff, '1048576');
    await expect(save).toBeEnabled();
  },
};

export const GitDefaultsSave: PropsStory = {
  render: renderWithProps,
  args: gitDefaultsArgs(),
  play: async ({ canvasElement, args }) => {
    const canvas = within(canvasElement);

    await replaceNumber(canvas.getByTestId(lfsId), '25');
    await userEvent.click(canvas.getByTestId(uploadId));
    await userEvent.click(canvas.getByTestId('SettingsGitDefaultsSave'));

    await waitFor(() => expect(args.onGitDefaultsSave).toHaveBeenCalledTimes(1));
    await expect(args.onGitDefaultsSave).toHaveBeenCalledWith({
      LfsAutoTrackThresholdMb: 25,
      DownloadLargeFiles: false,
      DiffIndexingLimitMb: 1024,
      UploadRightAway: false,
    });
  },
};

export const GitDefaultsIncompleteProps: PropsStory = {
  render: renderWithProps,
  args: {
    gitDefaults: currentGitDefaults,
    onGitDefaultsSave: fn(),
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await expect(canvas.getByTestId('SettingsAutoCreateNotesFolder')).toBeInTheDocument();
    await expect(canvas.queryByTestId('SettingsGitDefaultsSection')).toBeNull();
  },
};

const gitExecutableId = 'SettingsGitExecutable';

type GitExecutableSetting = {
  ChosenPath?: string;
  PathInUse?: string;
  GitVersion?: string;
  GitLfsVersion?: string;
  Problem?: string;
  IsChecking: boolean;
};

const gitExecutableArgs = (setting: GitExecutableSetting) => ({
  gitExecutable: setting as any,
  onGitExecutableChoose: fn(),
  onGitExecutableUseAutomatic: fn(),
  onGitExecutableCheckAgain: fn(),
});

export const GitExecutableAutomatic: PropsStory = {
  render: renderWithProps,
  args: gitExecutableArgs({
    PathInUse: '/usr/bin/git',
    GitVersion: '2.47.1',
    GitLfsVersion: '3.6.1',
    IsChecking: false,
  }),
  play: async ({ canvasElement, args }) => {
    const canvas = within(canvasElement);

    await expect(canvas.getByTestId(`${gitExecutableId}PathInUse`)).toHaveTextContent('/usr/bin/git');
    await expect(canvas.getByTestId(`${gitExecutableId}GitVersion`)).toHaveTextContent('2.47.1');
    await expect(canvas.getByTestId(`${gitExecutableId}GitLfsVersion`)).toHaveTextContent('3.6.1');
    await expect(canvas.queryByTestId(`${gitExecutableId}Problem`)).toBeNull();
    await expect(canvas.queryByTestId(`${gitExecutableId}Checking`)).toBeNull();

    // Without a chosen path there is nothing to switch back from.
    await expect(canvas.queryByTestId(`${gitExecutableId}UseAutomatic`)).toBeNull();
    await expect(canvas.getByTestId(`${gitExecutableId}CheckAgain`)).toBeEnabled();

    await userEvent.click(canvas.getByTestId(`${gitExecutableId}Choose`));
    await expect(args.onGitExecutableChoose).toHaveBeenCalledTimes(1);
    await userEvent.click(canvas.getByTestId(`${gitExecutableId}CheckAgain`));
    await expect(args.onGitExecutableCheckAgain).toHaveBeenCalledTimes(1);
  },
};

export const GitExecutableChosenPath: PropsStory = {
  render: renderWithProps,
  args: gitExecutableArgs({
    ChosenPath: '/opt/tools/git/bin/git',
    PathInUse: '/opt/tools/git/bin/git',
    GitVersion: '2.45.0',
    GitLfsVersion: '3.5.1',
    IsChecking: false,
  }),
  play: async ({ canvasElement, args }) => {
    const canvas = within(canvasElement);

    await expect(canvas.getByTestId(`${gitExecutableId}Source`)).toHaveTextContent('/opt/tools/git/bin/git');
    await expect(canvas.getByTestId(`${gitExecutableId}PathInUse`)).toHaveTextContent('/opt/tools/git/bin/git');

    await userEvent.click(canvas.getByTestId(`${gitExecutableId}UseAutomatic`));
    await expect(args.onGitExecutableUseAutomatic).toHaveBeenCalledTimes(1);
  },
};

export const GitExecutableProblem: PropsStory = {
  render: renderWithProps,
  args: gitExecutableArgs({
    ChosenPath: '/opt/tools/git/bin/git',
    Problem: 'The program could not be started.',
    IsChecking: false,
  }),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await expect(canvas.getByTestId(`${gitExecutableId}Problem`)).toHaveTextContent(
      'The program could not be started.',
    );
    // Versions and the path in use are unknown, so their rows stay out.
    await expect(canvas.queryByTestId(`${gitExecutableId}PathInUse`)).toBeNull();
    await expect(canvas.queryByTestId(`${gitExecutableId}GitVersion`)).toBeNull();
    await expect(canvas.queryByTestId(`${gitExecutableId}GitLfsVersion`)).toBeNull();
  },
};

export const GitExecutableChecking: PropsStory = {
  render: renderWithProps,
  args: gitExecutableArgs({
    ChosenPath: '/opt/tools/git/bin/git',
    PathInUse: '/opt/tools/git/bin/git',
    IsChecking: true,
  }),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await expect(canvas.getByTestId(`${gitExecutableId}Checking`)).toBeInTheDocument();
    await expect(canvas.getByTestId(`${gitExecutableId}CheckAgain`)).toBeDisabled();
    await expect(canvas.getByTestId(`${gitExecutableId}Choose`)).toBeEnabled();
  },
};

export const ControlledAutoCreateNotesFolder: PropsStory = {
  render: renderWithProps,
  args: {
    autoCreateNotesFolder: false,
    onAutoCreateNotesFolderChange: fn(),
    onAutoCreateNotesFolderEnabled: fn(),
  },
  beforeEach: () => {
    // Stories share one localStorage. Clear what an earlier story wrote before the page mounts.
    window.localStorage.removeItem(autoCreateNotesFolderKey);
  },
  play: async ({ canvasElement, args }) => {
    const canvas = within(canvasElement);
    const toggle = canvas.getByTestId('SettingsAutoCreateNotesFolder');

    // A write on mount would show up here.
    await expect(window.localStorage.getItem(autoCreateNotesFolderKey)).toBeNull();
    await expect(toggle).not.toBeChecked();
    await userEvent.click(toggle);

    await expect(args.onAutoCreateNotesFolderChange).toHaveBeenCalledWith(true);
    await expect(args.onAutoCreateNotesFolderEnabled).toHaveBeenCalledTimes(1);
    // The parent owns the value, so the toggle shows the prop again.
    await expect(toggle).not.toBeChecked();
    await expect(window.localStorage.getItem(autoCreateNotesFolderKey)).toBeNull();
  },
};
