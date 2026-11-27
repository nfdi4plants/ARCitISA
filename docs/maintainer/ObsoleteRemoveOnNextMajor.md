# Obsolete: remove on next major

Names from the time ARCitISA was called Swate that are kept only so that existing installs, user data or other tools keep working. Remove or rename them in the next major version. When you add an entry, say where the name lives, why it is still there and what breaks when it goes.

## Kept for compatibility

Added in 4.0.0, to remove in 5.0.0. The account id key, the browser storage keys, the clipboard format and the `.swate-import-` / `.swate-save-` prefixes were renamed in 4.0.0 without a transition.

| Name | Where | Why it is kept | Effect of removing it |
| --- | --- | --- | --- |
| User data folder `Swate` | `moveLegacyUserDataFolder` and the types before it in `SettingsStore.fs`, called first in `main.fs` | 4.0.0 moves the 3.x folder `Swate` to `ARCitISA` on the first start (settings, signed-in accounts, browser profile). | Users who skip 4.x and come straight from 3.x start with empty settings and signed out. |
| Squirrel installs in `%LocalAppData%\swate` and `%LocalAppData%\arcitisa` | `customInit` and `removeOldSquirrelInstall` in `src/Electron/packaging/nsis/installer.nsh` | The 4.x setup finds a 3.x install, asks once and removes it. | Users who come straight from 3.x have both versions installed side by side. |
| Linux package `swate` | `--conflicts=swate` and `--replaces=swate` in the deb and rpm config of `src/Electron/electron-builder.config.js` | apt and dnf remove the 3.4.0-rc.2 package `swate` when the new `arcitisa` package is installed. | Users who come straight from 3.4.0-rc.2 or older have both packages installed. |

## Waiting on infrastructure, not on a major version

| Name | Where | Switch when |
| --- | --- | --- |
| Query parameter `is_swatehost` | `src/Client/Routing.fs` (accepted next to `is_arcitisahost`) | The manifests send `is_arcitisahost` since 4.0.0. Excel add-ins installed from an older manifest still send `?is_swatehost=2`. Drop it once no installed add-in uses an older manifest. ARCitect, which sent `=1`, is no longer supported. |
| Add-in icons `src/Client/public/assets/swate_*.png` | Copies of `arcitisa_*.png` | Excel add-ins installed from an older manifest load these URLs. Remove them together with `is_swatehost`. |
| `swate-alpha.nfdi4plants.org`, `swate.nfdi4plants.org` | `src/Shared/URLs.fs`, `build/manifest.production.xml`, `README.md`, `.db/*.yml`, `src/Server/Server.fs` (comment) | `arcitisa-alpha.nfdi4plants.org` and `arcitisa.nfdi4plants.org` resolve to the same server, but on 2026-11-25 they did not serve the app yet (403 on `/`, 404 on the API). Switch once they do, and keep the old names working for installed add-ins. |
| Helpdesk topics `Tools_Swate`, `Metadata_SwateTemplate` | `src/Shared/URLs.fs` | The helpdesk has matching ARCitISA topics. |
| `SWATE_API` | `.db/*.yml` | The swobup image reads another variable name. |
| `nfdi4plants/SWATE_templates`, `nfdi4plants/swate-template-registry` | `.db/*.yml`, `src/Server/Server.fs` | Those repositories are renamed. |
| Packages `@nfdi4plants/swate-components`, `Swate.Components`, `Swate.Components.Core`, image `ghcr.io/nfdi4plants/swate` | Registries only | Releases now publish `@nfdi4plants/arcitisa-components`, `ARCitISA.Components`, `ARCitISA.Components.Core` and `ghcr.io/nfdi4plants/arcitisa`. Deprecate the old packages once the new ones have a release. |
