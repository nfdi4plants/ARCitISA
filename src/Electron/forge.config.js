const { FusesPlugin } = require('@electron-forge/plugin-fuses');
const { FuseV1Options, FuseVersion } = require('@electron/fuses');
const { execFile } = require('node:child_process');
const fs = require('node:fs/promises');
const path = require('node:path');
const { promisify } = require('node:util');

const execFileAsync = promisify(execFile);

const platformIcon =
  process.platform === 'win32'
    ? 'assets/icons/win/icon'
    : process.platform === 'darwin'
      ? 'assets/icons/mac/icon'
      : 'assets/icons/png/1024x1024';

const adHocSignDarwinArm64App = async (_forgeConfig, packageResult) => {
  if (packageResult.platform !== 'darwin' || packageResult.arch !== 'arm64') {
    return;
  }

  let signedAppCount = 0;

  for (const outputPath of packageResult.outputPaths) {
    const entries = await fs.readdir(outputPath, { withFileTypes: true });
    const appEntry = entries.find((entry) => entry.isDirectory() && entry.name.endsWith('.app'));

    if (!appEntry) {
      continue;
    }

    const appPath = path.join(outputPath, appEntry.name);
    await execFileAsync('codesign', ['--force', '--deep', '--sign', '-', appPath]);
    await execFileAsync('codesign', ['--verify', '--deep', '--strict', '--verbose=2', appPath]);
    signedAppCount++;
  }

  if (signedAppCount === 0) {
    throw new Error(`No .app bundle found to ad-hoc sign in: ${packageResult.outputPaths.join(', ')}`);
  }
};

module.exports = {
  packagerConfig: {
    // The main process starts the text diff worker from the unpacked copy next to app.asar.
    // asar matches this pattern with minimatch and matchBase, so a bare file name matches the
    // worker in any folder. A pattern with ** skips dot folders, which include .vite and any
    // dot folder in the build path (C:\Users\x\.cache, for example).
    asar: { unpack: 'text-diff-worker.cjs' },
    executableName: 'swate',
    // Keeps the bundle id from before the rename, so macOS treats ARCitISA as the same app.
    appBundleId: 'com.electron.swate',
    icon: platformIcon,
    extraResource: [
        "./assets"
    ]
  },
  rebuildConfig: {},
  hooks: {
    postPackage: adHocSignDarwinArm64App,
  },
  makers: [
    {
      name: '@electron-forge/maker-squirrel',
      config: {
        setupIcon: 'assets/icons/win/icon.ico',
      },
    },
    {
      name: '@electron-forge/maker-zip',
      // On Linux the zip holds the unpacked app for distributions that cannot install the deb or rpm (#1433).
      platforms: ['darwin', 'linux'],
    },
    {
      name: '@electron-forge/maker-deb',
      config: {},
    },
    {
      name: '@electron-forge/maker-rpm',
      config: {},
    },
    {
      name: '@electron-forge/maker-dmg',
      config: { }
    }
  ],
  plugins: [
    {
      name: '@electron-forge/plugin-vite',
      config: {
        // `build` can specify multiple entry builds, which can be Main process, Preload scripts, Worker process, etc.
        // If you are familiar with Vite configuration, it will look really familiar.
        build: [
          {
            // `entry` is just an alias for `build.lib.entry` in the corresponding file of `config`.
            entry: 'src/fable_output/Main/main.fs.jsx',
            config: 'vite.main.config.mjs',
            target: 'main',
          },
          {
            entry: 'src/fable_output/Preload/preload.fs.jsx',
            config: 'vite.preload.config.mjs',
            target: 'preload',
          },
          {
            entry: 'src/fable_output/Main/Workers/TextDiffWorkerEntry.fs.jsx',
            config: 'vite.worker.config.mjs',
            target: 'main',
          },
        ],
        renderer: [
          {
            name: 'main_window',
            config: 'vite.renderer.config.mjs',
          },
        ],
      },
    },
    // Fuses are used to enable/disable various Electron functionality
    // at package time, before code signing the application
    new FusesPlugin({
      version: FuseVersion.V1,
      [FuseV1Options.RunAsNode]: false,
      [FuseV1Options.EnableCookieEncryption]: true,
      [FuseV1Options.EnableNodeOptionsEnvironmentVariable]: false,
      [FuseV1Options.EnableNodeCliInspectArguments]: false,
      [FuseV1Options.EnableEmbeddedAsarIntegrityValidation]: true,
      [FuseV1Options.OnlyLoadAppFromAsar]: true,
    }),
  ],
};
