# Obsolete: remove on next major

Names from the time ARCitISA was called Swate that are kept only so that existing installs, user data or other tools keep working. Remove or rename them in the next major version. When you add an entry, say where the name lives, why it is still there and what breaks when it goes.

## Kept for compatibility

| Name | Where | Why it is kept | Effect of removing it |
| --- | --- | --- | --- |
| JSON key `localSwateAccountId` | `SecureAuthStore.fs` (`legacyAccountIdJsonKey`, the anonymous records in `store` and `setActiveLocalArcitisaAccountId`) | Written next to `localArcitisaAccountId` in `active-account.json` and every `*-meta.json`. `active-account.json` falls back to it when the new key is missing. | An `active-account.json` last written before the rename no longer selects an account, and the user has to pick one again. Stored credentials are not affected, because they are found by file name. Remove the two tests that cover the old key too. |
| Browser storage keys `swate-theme-ctx`, `swate-termsearchconfig-ctx`, `swate-settings-auto-create-notes-folder`, the prefix `swate-` of `Keys.mkLocalStorageKey` | `src/Components` (`ThemeProvider.fs`, `TermSearch/ConfigProvider.fs`, `SettingsPage.fs`, `Util/Extensions.fs`) | The values live in the users' localStorage. | Theme, term search configuration and settings go back to their defaults once. |
| Browser storage keys `Swate_Search_Config`, `_SwateDefaultSearch`, `swate_local_spreadsheet_key`, `swate_session_history_*`, `swate_history_*`, `swate-table-history` | `src/Client/LocalStorage/ArcitisaSearchConfig.fs`, `src/Client/States/LocalHistory.fs` | The values live in localStorage, sessionStorage and IndexedDB of web and Excel users. | Web and Excel users lose their local table history and search configuration once. |
| Clipboard format `web application/x-swate-cells+json` and HTML attribute `data-swate-cells` | `src/Components/src/ARCtrl/ClipboardCodec.fs` | Cells copied in an older version or in ARCitect paste as cells, and the other way round. | Copy and paste between old and new versions falls back to plain text. Rename it together with ARCitect. |
| Folder prefix `.swate-import-<32 hex>` and file prefix `.swate-save-<32 hex>.tmp` | `FileSystemIO.fs`, `ArcVaultHelper.fs` (watcher ignore pattern), `FileIOHelper.fs`, `RenamePathRules.fs`, `docs/FileImportRules.md` | Leftovers of an interrupted import or save in users' ARCs are still recognised and ignored. | Such leftovers show up in the file explorer as normal folders and files. |
| Add-in icons `src/Client/public/assets/swate_*.png` | Copies of `arcitisa_*.png` | Excel add-ins installed from the old manifest load these URLs. | Installed add-ins that still use the old manifest show no icon. |
| User data folder `Swate` | `moveLegacyUserDataFolder` and the types before it in `SettingsStore.fs`, called first in `main.fs` | 4.0.0 moves the 3.x folder `Swate` to `ARCitISA` on the first start (settings, signed-in accounts, browser profile). | Users who skip 4.x and come straight from 3.x start with empty settings and signed out. |
| Query parameter `is_swatehost` | `src/Client/Routing.fs`, `build/manifest*.xml` | ARCitect opens the web client with `?is_swatehost=1`, the Excel add-in with `=2`. | ARCitect and installed add-ins open the client as a plain browser host. Accept both names for a while before dropping the old one. |

## Waiting on infrastructure, not on a major version

| Name | Where | Switch when |
| --- | --- | --- |
| `swate-alpha.nfdi4plants.org`, `swate.nfdi4plants.org` | `src/Shared/URLs.fs`, `build/manifest.production.xml`, `README.md`, `.db/*.yml`, `src/Server/Server.fs` (comment) | `arcitisa-alpha.nfdi4plants.org` and `arcitisa.nfdi4plants.org` resolve to the same server, but on 2026-11-25 they did not serve the app yet (403 on `/`, 404 on the API). Switch once they do, and keep the old names working for installed add-ins. |
| Helpdesk topics `Tools_Swate`, `Metadata_SwateTemplate` | `src/Shared/URLs.fs` | The helpdesk has matching ARCitISA topics. |
| `SWATE_API` | `.db/*.yml` | The swobup image reads another variable name. |
| `nfdi4plants/SWATE_templates`, `nfdi4plants/swate-template-registry` | `.db/*.yml`, `src/Server/Server.fs` | Those repositories are renamed. |
| Packages `@nfdi4plants/swate-components`, `Swate.Components`, `Swate.Components.Core`, image `ghcr.io/nfdi4plants/swate` | Registries only | Releases now publish `@nfdi4plants/arcitisa-components`, `ARCitISA.Components`, `ARCitISA.Components.Core` and `ghcr.io/nfdi4plants/arcitisa`. Deprecate the old packages once the new ones have a release. |
