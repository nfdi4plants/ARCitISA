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
    await expect(canvas.queryByTestId('settings-large-files')).toBeNull();
  },
};

export const GitDefaults: PropsStory = {
  render: renderWithProps,
  args: gitDefaultsArgs(),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await expect(within(canvas.getByTestId('settings-large-files')).getByTestId(lfsId)).toHaveValue(10);
    await expect(within(canvas.getByTestId('settings-large-files')).getByTestId(downloadId)).not.toBeChecked();
    await expect(within(canvas.getByTestId('settings-git')).getByTestId(diffId)).toHaveValue(1024);
    await expect(within(canvas.getByTestId('settings-git')).getByTestId(uploadId)).toBeChecked();

    // Only the field that differs from the built-in value is marked.
    await expect(canvas.getByTestId(`${downloadId}Changed`)).toBeInTheDocument();
    await expect(canvas.getByTestId(`${downloadId}Reset`)).toBeInTheDocument();
    for (const id of [lfsId, diffId, uploadId]) {
      await expect(canvas.queryByTestId(`${id}Changed`)).toBeNull();
      await expect(canvas.queryByTestId(`${id}Reset`)).toBeNull();
    }

    // An unchanged number has nothing to update.
    await expect(canvas.getByTestId(`${lfsId}Update`)).toBeDisabled();
    await expect(canvas.getByTestId(`${diffId}Update`)).toBeDisabled();
  },
};

export const GitDefaultsSwitchesApplyRightAway: PropsStory = {
  render: renderWithProps,
  args: gitDefaultsArgs(),
  play: async ({ canvasElement, args }) => {
    const canvas = within(canvasElement);
    const onChange = args.gitDefaults.OnChange;

    await userEvent.click(canvas.getByTestId(uploadId));
    await expect(onChange).toHaveBeenLastCalledWith({ ...currentGitDefaults, UploadRightAway: false });

    // Reset applies the built-in value right away as well.
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

    await replaceNumber(canvas.getByTestId(lfsId), '25');
    await expect(onChange).not.toHaveBeenCalled();
    await userEvent.click(canvas.getByTestId(`${lfsId}Update`));
    await expect(onChange).toHaveBeenLastCalledWith({ ...currentGitDefaults, LfsAutoTrackThresholdMb: 25 });

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
    const lfs = canvas.getByTestId(lfsId);
    const diff = canvas.getByTestId(diffId);

    await expect(canvas.queryByTestId(`${lfsId}Range`)).toBeNull();

    for (const value of ['101', '0', '1.5', '']) {
      await replaceNumber(lfs, value);
      await expect(canvas.getByTestId(`${lfsId}Update`)).toBeDisabled();
      await expect(canvas.getByTestId(`${lfsId}Range`)).toBeInTheDocument();
    }

    await replaceNumber(lfs, '100');
    await expect(canvas.getByTestId(`${lfsId}Update`)).toBeEnabled();
    await expect(canvas.queryByTestId(`${lfsId}Range`)).toBeNull();

    for (const value of ['1048577', '0', '']) {
      await replaceNumber(diff, value);
      await expect(canvas.getByTestId(`${diffId}Update`)).toBeDisabled();
      await expect(canvas.getByTestId(`${diffId}Range`)).toBeInTheDocument();
    }

    await replaceNumber(diff, '1048576');
    await expect(canvas.getByTestId(`${diffId}Update`)).toBeEnabled();
  },
};

// The navigation shows only where the page is wide enough. Vitest's browser starts narrow.
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

const navigationLink = (canvas: ReturnType<typeof within>, sectionId: string) =>
  canvas.getByTestId(`SettingsNavigation-${sectionId}`);

const expectMarked = async (canvas: ReturnType<typeof within>, sectionId: string) =>
  waitFor(() => expect(navigationLink(canvas, sectionId)).toHaveAttribute('aria-current', 'location'));

const scrollTop = (canvasElement: HTMLElement) => canvasElement.ownerDocument.scrollingElement!.scrollTop;

export const NavigationJumpsToCategory: PropsStory = {
  render: renderWithProps,
  args: allCategoriesArgs(),
  play: async ({ canvasElement }) =>
    withViewport(canvasElement, 1280, 600, async () => {
      const canvas = within(canvasElement);
      await expect(canvas.getByTestId('SettingsNavigation')).toBeVisible();
      await expectMarked(canvas, 'settings-general');

      await userEvent.click(navigationLink(canvas, 'settings-large-files'));

      await expectMarked(canvas, 'settings-large-files');
      await waitFor(() => expect(scrollTop(canvasElement)).toBeGreaterThan(0));
    }),
};

export const NavigationMarksCategoryInView: PropsStory = {
  render: renderWithProps,
  args: allCategoriesArgs(),
  play: async ({ canvasElement }) =>
    withViewport(canvasElement, 1280, 600, async () => {
      const canvas = within(canvasElement);
      await expectMarked(canvas, 'settings-general');

      // Scrolling without the navigation moves the mark along.
      canvas.getByTestId('settings-git').scrollIntoView({ block: 'start' });
      await expectMarked(canvas, 'settings-git');

      // At the end of the page the last box can't reach the top, so the last category takes the mark.
      const scroller = canvasElement.ownerDocument.scrollingElement!;
      scroller.scrollTop = scroller.scrollHeight;
      await expectMarked(canvas, 'settings-term-search');

      scroller.scrollTop = 0;
      await expectMarked(canvas, 'settings-general');
    }),
};

export const OpensAtCategory: PropsStory = {
  render: renderWithProps,
  args: { ...allCategoriesArgs(), category: 'largeFiles' } as any,
  play: async ({ canvasElement }) =>
    withViewport(canvasElement, 1280, 600, async () => {
      const canvas = within(canvasElement);

      await expectMarked(canvas, 'settings-large-files');
      await expect(scrollTop(canvasElement)).toBeGreaterThan(0);
    }),
};

export const NavigationHiddenWhenNarrow: PropsStory = {
  render: renderWithProps,
  args: allCategoriesArgs(),
  play: async ({ canvasElement }) =>
    withViewport(canvasElement, 600, 800, async () => {
      const canvas = within(canvasElement);

      await expect(canvas.getByTestId('SettingsNavigation')).not.toBeVisible();
      await expect(canvas.getByTestId('settings-large-files')).toBeVisible();
    }),
};

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
    await expect(args.gitExecutable.OnChoose).toHaveBeenCalledTimes(1);
    await userEvent.click(canvas.getByTestId(`${gitExecutableId}CheckAgain`));
    await expect(args.gitExecutable.OnCheckAgain).toHaveBeenCalledTimes(1);
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
    await expect(args.gitExecutable.OnUseAutomatic).toHaveBeenCalledTimes(1);
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
