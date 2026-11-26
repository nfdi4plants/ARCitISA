// Builds the installers from the app that `electron-forge package` leaves in out/. electron-builder
// only wraps that app (--prepackaged). Forge sets up the fuses, the asar and the macOS ad-hoc
// signature, and electron-builder leaves them alone because it skips its own packaging step.
const { version } = require('./package.json');

module.exports = {
  // Never change appId or nsis.guid. macOS identifies the installed app by the bundle id, Windows
  // groups taskbar icons by the AppUserModelID and finds the uninstall entry by the guid. appId must
  // equal appBundleId in forge.config.js.
  appId: 'org.nfdi4plants.app-arc-management-core',
  productName: 'ARCitISA',
  directories: { output: 'out/installers' },
  // No auto updates in 4.0.0. Without this, electron-builder finds the GitHub remote and writes an
  // app-update.yml into the packaged app.
  publish: null,

  win: {
    target: 'nsis',
    icon: 'assets/icons/win/icon.ico',
  },
  nsis: {
    // The texts in packaging/nsis/installer.nsh are English only.
    multiLanguageInstaller: false,
    oneClick: false,
    // Shows the page that asks whether to install for this user or for all users.
    perMachine: false,
    allowToChangeInstallationDirectory: true,
    createStartMenuShortcut: true,
    // packaging/nsis/installer.nsh asks for the desktop shortcut and creates it.
    createDesktopShortcut: false,
    guid: '716c9faf-756d-4e23-94c8-6d9e8d665a44',
    include: 'packaging/nsis/installer.nsh',
    artifactName: '${productName}-Setup-${version}-${arch}.${ext}',
  },

  linux: {
    // Must match executableName in forge.config.js. Only set for Linux: on Windows it would also
    // rename the install folder and the uninstaller.
    executableName: 'arcitisa',
    category: 'Science',
    icon: 'assets/icons/png',
    artifactName: '${productName}-linux-${arch}-${version}.${ext}',
  },
  appImage: {
    artifactName: '${productName}-${version}-${arch}.${ext}',
  },
  // The 3.x deb and rpm packages were called swate. apt and dnf list it as removed before the user
  // confirms the install.
  deb: {
    fpm: ['--conflicts=swate', '--replaces=swate'],
    artifactName: '${name}_${version}_${arch}.${ext}',
  },
  rpm: {
    // fpm writes --replaces as Obsoletes into the rpm.
    fpm: ['--replaces=swate'],
    artifactName: '${name}-${version}-1.${arch}.${ext}',
  },
  // The dependencies of the PKGBUILD that built the 3.x pacman package.
  pacman: {
    depends: ['gtk3', 'nss', 'alsa-lib', 'libnotify', 'xdg-utils', 'mesa', 'libdrm', 'git'],
    compression: 'zstd',
    fpm: [
      '--pacman-optional-depends=git-lfs: large files in ARCs',
      // electron-builder turns 4.0.0-rc.1 into 4.0.0_rc.1, which pacman sorts above 4.0.0.
      // 4.0.0rc.1 sorts below it. fpm takes the last --version.
      `--version=${version.replace(/-/g, '')}`,
    ],
    artifactName: '${name}-${version}-1-${arch}.pkg.tar.zst',
  },

  mac: {
    target: ['dmg', 'zip'],
    icon: 'assets/icons/mac/icon.icns',
    artifactName: '${productName}-darwin-${arch}-${version}.${ext}',
  },
  dmg: {
    artifactName: '${productName}-${version}-${arch}.${ext}',
  },
};
