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

const autoCreateNotesFolderKey = 'arcitisa-settings-auto-create-notes-folder';

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
  gitExecutable: {
    Setting: setting,
    OnChoose: fn(),
    OnUseAutomatic: fn(),
    OnCheckAgain: fn(),
  } as any,
});

// The Git executable is on the Git page.
const onGitPage = (setting: GitExecutableSetting) => ({ ...gitExecutableArgs(setting), category: 'git' }) as any;

const gitDefaultsArgs = () => ({
  gitDefaults: {
    Current: currentGitDefaults,
    BuiltIn: builtInGitDefaults,
    OnChange: fn(),
  } as any,
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
    await expect(canvas.queryByTestId('settings-git')).toBeNull();
    await expect(canvas.queryByTestId('settings-largeFiles')).toBeNull();
  },
};

const openCategory = async (canvas: ReturnType<typeof within>, sectionId: string) => {
  await userEvent.click(canvas.getByTestId(`SettingsNavigation-${sectionId}`));
  await canvas.findByTestId(sectionId);
};

export const GitDefaults: PropsStory = {
  render: renderWithProps,
  args: gitDefaultsArgs(),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await openCategory(canvas, 'settings-git');
    await expect(canvas.getByTestId(diffId)).toHaveValue(1024);
    await expect(canvas.getByTestId(uploadId)).toBeChecked();
    // An unchanged number has nothing to update.
    await expect(canvas.getByTestId(`${diffId}Update`)).toBeDisabled();
    for (const id of [diffId, uploadId]) {
      await expect(canvas.queryByTestId(`${id}Changed`)).toBeNull();
      await expect(canvas.queryByTestId(`${id}Reset`)).toBeNull();
    }

    await openCategory(canvas, 'settings-largeFiles');
    await expect(canvas.getByTestId(lfsId)).toHaveValue(10);
    await expect(canvas.getByTestId(downloadId)).not.toBeChecked();
    await expect(canvas.getByTestId(`${lfsId}Update`)).toBeDisabled();

    // Only the field that differs from the built-in value is marked.
    await expect(canvas.getByTestId(`${downloadId}Changed`)).toBeInTheDocument();
    await expect(canvas.getByTestId(`${downloadId}Reset`)).toBeInTheDocument();
    await expect(canvas.queryByTestId(`${lfsId}Changed`)).toBeNull();
    await expect(canvas.queryByTestId(`${lfsId}Reset`)).toBeNull();
  },
};

export const GitDefaultsSwitchesApplyRightAway: PropsStory = {
  render: renderWithProps,
  args: gitDefaultsArgs(),
  play: async ({ canvasElement, args }) => {
    const canvas = within(canvasElement);
    const onChange = args.gitDefaults.OnChange;

    await openCategory(canvas, 'settings-git');
    await userEvent.click(canvas.getByTestId(uploadId));
    await expect(onChange).toHaveBeenLastCalledWith({ ...currentGitDefaults, UploadRightAway: false });

    // Reset applies the built-in value right away as well.
    await openCategory(canvas, 'settings-largeFiles');
    await userEvent.click(canvas.getByTestId(`${downloadId}Reset`));
    await expect(onChange).toHaveBeenLastCalledWith({ ...currentGitDefaults, DownloadLargeFiles: true });
    await expect(onChange).toHaveBeenCalledTimes(2);
  },
};

export const GitDefaultsNumbersApplyWithUpdate: PropsStory = {
  render: renderWithProps,
  args: gitDefaultsArgs(),
  play: async ({ canvasElement, args }) => {
    const canvas = within(canvasElement);
    const onChange = args.gitDefaults.OnChange;

    await openCategory(canvas, 'settings-largeFiles');
    await replaceNumber(canvas.getByTestId(lfsId), '25');
    await expect(onChange).not.toHaveBeenCalled();
    await userEvent.click(canvas.getByTestId(`${lfsId}Update`));
    await expect(onChange).toHaveBeenLastCalledWith({ ...currentGitDefaults, LfsAutoTrackThresholdMb: 25 });

    await openCategory(canvas, 'settings-git');
    await replaceNumber(canvas.getByTestId(diffId), '2048');
    await userEvent.click(canvas.getByTestId(`${diffId}Update`));
    await expect(onChange).toHaveBeenLastCalledWith({ ...currentGitDefaults, DiffIndexingLimitMb: 2048 });
    await expect(onChange).toHaveBeenCalledTimes(2);
  },
};

export const GitDefaultsOutOfRange: PropsStory = {
  render: renderWithProps,
  args: gitDefaultsArgs(),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await openCategory(canvas, 'settings-largeFiles');
    const lfs = canvas.getByTestId(lfsId);
    await expect(canvas.queryByTestId(`${lfsId}Range`)).toBeNull();

    for (const value of ['101', '0', '1.5', '']) {
      await replaceNumber(lfs, value);
      await expect(canvas.getByTestId(`${lfsId}Update`)).toBeDisabled();
      await expect(canvas.getByTestId(`${lfsId}Range`)).toBeInTheDocument();
    }

    await replaceNumber(lfs, '100');
    await expect(canvas.getByTestId(`${lfsId}Update`)).toBeEnabled();
    await expect(canvas.queryByTestId(`${lfsId}Range`)).toBeNull();

    await openCategory(canvas, 'settings-git');
    const diff = canvas.getByTestId(diffId);

    for (const value of ['1048577', '0', '']) {
      await replaceNumber(diff, value);
      await expect(canvas.getByTestId(`${diffId}Update`)).toBeDisabled();
      await expect(canvas.getByTestId(`${diffId}Range`)).toBeInTheDocument();
    }

    await replaceNumber(diff, '1048576');
    await expect(canvas.getByTestId(`${diffId}Update`)).toBeEnabled();
  },
};

// The navigation sits beside the page where it is wide enough and above it otherwise.
const withViewport = async (canvasElement: HTMLElement, width: number, height: number, body: () => Promise<void>) => {
  let restoreViewport = async () => {};
  if ('__vitest_browser__' in globalThis) {
    const { page } = await import('vitest/browser');
    const view = canvasElement.ownerDocument.defaultView!;
    const previousWidth = view.innerWidth;
    const previousHeight = view.innerHeight;
    restoreViewport = () => page.viewport(previousWidth, previousHeight);
    await page.viewport(width, height);
  }
  try {
    await body();
  } finally {
    await restoreViewport();
  }
};

const allCategoriesArgs = () => ({
  ...gitDefaultsArgs(),
  ...gitExecutableArgs({ PathInUse: '/usr/bin/git', GitVersion: '2.47.1', IsChecking: false }),
  dataHubs: { Urls: ['https://gitlab.example.org'], OnChange: fn() } as any,
});

const expectMarked = async (canvas: ReturnType<typeof within>, sectionId: string) =>
  waitFor(() => expect(canvas.getByTestId(`SettingsNavigation-${sectionId}`)).toHaveAttribute('aria-current', 'page'));

const navigationPlay = (width: number): NonNullable<PropsStory['play']> => async ({ canvasElement }) =>
  withViewport(canvasElement, width, 700, async () => {
    const canvas = within(canvasElement);
    await expect(canvas.getByTestId('SettingsNavigation')).toBeVisible();
    await expect(canvas.getByTestId('settings-general')).toBeVisible();
    await expectMarked(canvas, 'settings-general');

    await openCategory(canvas, 'settings-largeFiles');
    await expect(canvas.queryByTestId('settings-general')).toBeNull();
    await expectMarked(canvas, 'settings-largeFiles');

    // The arrow expands Git without leaving the page, and a Git setting then opens the Git page.
    await userEvent.click(canvas.getByTestId('SettingsNavigationToggle-settings-git'));
    await expect(canvas.getByTestId('settings-largeFiles')).toBeVisible();
    const gitSetting = canvas.getByTestId('SettingsNavigation-settings-git-0');
    await expect(gitSetting).toBeVisible();
    await userEvent.click(gitSetting);
    await expect(await canvas.findByTestId('settings-git')).toBeVisible();
    await expect(canvas.queryByTestId('settings-largeFiles')).toBeNull();
    await expectMarked(canvas, 'settings-git');

    // Collapsing Git keeps its page.
    await userEvent.click(canvas.getByTestId('SettingsNavigationToggle-settings-git'));
    await expect(gitSetting).not.toBeVisible();
    await expect(canvas.getByTestId('settings-git')).toBeVisible();
  });

export const NavigationBesidePage: PropsStory = {
  render: renderWithProps,
  args: allCategoriesArgs(),
  play: navigationPlay(1280),
};

export const NavigationAbovePageWhenNarrow: PropsStory = {
  render: renderWithProps,
  args: allCategoriesArgs(),
  play: navigationPlay(600),
};

// SettingsPage.test.rtl.fs checks that the page opens at the given category.
export const OpensAtCategory: PropsStory = {
  render: renderWithProps,
  args: { ...allCategoriesArgs(), category: 'largeFiles' } as any,
};

export const GitExecutableAutomatic: PropsStory = {
  render: renderWithProps,
  args: onGitPage({
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
    await expect(args.gitExecutable.OnChoose).toHaveBeenCalledTimes(1);
    await userEvent.click(canvas.getByTestId(`${gitExecutableId}CheckAgain`));
    await expect(args.gitExecutable.OnCheckAgain).toHaveBeenCalledTimes(1);
  },
};

export const GitExecutableChosenPath: PropsStory = {
  render: renderWithProps,
  args: onGitPage({
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
    await expect(args.gitExecutable.OnUseAutomatic).toHaveBeenCalledTimes(1);
  },
};

export const GitExecutableProblem: PropsStory = {
  render: renderWithProps,
  args: onGitPage({
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
  args: onGitPage({
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
