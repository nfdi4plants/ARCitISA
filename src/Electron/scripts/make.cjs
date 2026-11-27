// Builds the installers for this computer's platform: `electron-forge package` (Vite build, fuses,
// asar, macOS ad-hoc signature), then electron-builder wraps the packaged app (--prepackaged).
// Run `npm run fable` first. The release (Release.electronBinaries in build/Actions/Release.fs) runs
// this script too.
//
// Usage: node scripts/make.cjs [--arch=x64|arm64]   (default: the architecture of this Node)
const { spawnSync } = require('node:child_process');
const path = require('node:path');
const { productName } = require('../package.json');

const archArg = process.argv.slice(2).find((arg) => arg.startsWith('--arch='));
const arch = archArg ? archArg.slice('--arch='.length) : process.arch;
const platform = process.platform;

// The electron-builder targets of each release build. Release.electronExtensions in
// build/Actions/Release.fs lists the files they must produce.
const targetsByBuild = {
  'win32-x64': ['--win', 'nsis'],
  'darwin-x64': ['--mac', 'dmg', 'zip'],
  'darwin-arm64': ['--mac', 'dmg', 'zip'],
  'linux-x64': ['--linux', 'deb', 'rpm', 'pacman', 'AppImage', 'zip'],
  'linux-arm64': ['--linux', 'deb', 'rpm', 'zip'],
};

const targets = targetsByBuild[`${platform}-${arch}`];
if (!targets) {
  console.error(`No installers are made for ${platform} ${arch}.`);
  process.exit(1);
}

const electronDir = path.join(__dirname, '..');

// Runs a Node entry point and stops on failure. A run killed by a signal (an out of memory abort,
// for example) counts as failed too: the forge CLI wrapper exits with 0 then, which hid the failed
// macOS build of release 2.6.0 (#1421). Calling the package entry directly also skips the CLI's
// Node version and package manager checks.
function runNode(entry, args, name) {
  const result = spawnSync(process.execPath, [...process.execArgv, entry, ...args], {
    cwd: electronDir,
    stdio: 'inherit',
  });

  if (result.error) {
    throw result.error;
  }

  if (result.signal) {
    console.error(`${name} was killed by ${result.signal}`);
    process.exit(1);
  }

  if (result.status !== 0) {
    console.error(`${name} exited with code ${result.status}`);
    process.exit(result.status ?? 1);
  }
}

runNode(require.resolve('@electron-forge/cli/dist/electron-forge-package.js'), [`--arch=${arch}`], 'electron-forge package');

// Where electron-forge package leaves the app. On macOS electron-builder takes the .app bundle.
const packagedApp = path.join('out', `${productName}-${platform}-${arch}`, platform === 'darwin' ? `${productName}.app` : '');

runNode(
  require.resolve('electron-builder/cli.js'),
  ['--prepackaged', packagedApp, ...targets, `--${arch}`, '--config', 'electron-builder.config.js', '--publish', 'never'],
  'electron-builder',
);
