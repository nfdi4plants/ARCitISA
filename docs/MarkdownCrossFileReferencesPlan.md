# Cross-File References in Markdown: Plan

Status: milestones 1 and 2 implemented on 2026-11-18, not committed yet. Milestone 3 is not started (see "Differences from the implementation").

Markdown in ARCitISA cannot link to other files of the ARC or show images stored in it. This plan adds relative links and images to the Markdown preview of text files, notes and ISA description fields. It has to stay fast in ARCs with millions of files, of which only a handful are referenced from Markdown. The scope is deliberately small: references resolve when they are shown or clicked, and nothing else happens.

## Goals

- `/path` resolves against the ARC root. `./path`, `../path` and a bare `path` resolve against the folder of the open file. Raw HTML `<a href>` and `<img src>` follow the same rules.
- A click on a link to a file of the ARC opens it in the app, through the leave guard. External links open in the browser. `#heading` scrolls inside the preview.
- Images of the ARC show in the preview. An image that cannot be shown says why: missing, Git LFS pointer, too large, unsupported type or outside the ARC.
- Raw HTML renders, sanitized.
- The editor warns about broken links and about links whose case differs from the file on disk.
- The cost depends only on the references in the open document. Nothing walks the ARC, keeps an index or watches more files.
- All Markdown file access in the main process lives in one service with its own IPC contract.
- The Markdown components in `src/Components` stay app-agnostic. The host supplies file access.

## Not planned

- **Updating references.** Renaming or moving a file never rewrites URLs in Markdown. A reference that breaks shows up as a warning the next time the document is edited.
- **Git LFS.** LFS files are never downloaded or resolved for the user. A pointer file shows a notice, with no download action.
- **Writing help.** No path completion, no quick fixes, no image insertion.
- **Anchors in other files.** `other.md#section` opens `other.md` at the top.
- **HTML preview.** Relative URLs stay blocked in its frame.
- **Image page.** It keeps loading images as base64.

## What exists today

| Piece | Where | Relevance |
|---|---|---|
| Markdown preview | `MarkdownText/Preview.fs`, used by `TextDocumentTarget.fs`, the notes editor (`TextInputWithMarkdown`) and the Changelog | One static `components` object (only `code`) and one rehype plugin (`rehype-sanitize`). The preview does not know which file it shows. |
| URL handling | `@uiw/react-markdown-preview` 5.2.1 over `react-markdown` 10.1.0 | The wrapper sets `urlTransform` to `url => url`, so the sanitizer is the only URL filter. It keeps relative URLs and drops every scheme except `http`, `https` and `mailto` (and a few chat schemes). `urlTransform` runs in `post()`, after the rehype plugins, so it can rewrite URLs after sanitizing. |
| Relative URLs | Renderer URL (`file:///…/renderer/main_window/` packaged, the Vite server in dev) | Images break. A clicked relative link reaches `onWillNavigate` (`ArcVaultHelper.fs`), which hands the bogus URL to `shell.openExternal`. In the packaged app a `../../..` image path can show local files outside the ARC. |
| Heading anchors | `rehype-slug` and `rehype-autolink-headings` from the wrapper, then `rehype-sanitize` | The sanitizer prefixes ids with `user-content-`, so `#heading` does not match its heading. |
| Raw HTML | Wrapper default `skipHtml` | Raw HTML shows as text. `rehype-raw` 7.0.0 is already installed through the wrapper. |
| File tree | `Main/IndexedFileTree.fs` | Lazy: it knows only the directories that were opened, so it cannot answer "does this path exist". |
| Vault registry | `ARC_VAULTS.Vaults: Dictionary<int, ArcVault>`, keyed by `BrowserWindow.id` | The routing key for asset requests. |
| Path checks | `ArcPathValidation` and `tryResolveArcRelativePath` in `IPC/FileSystemIO.fs`, `checkTargetAsync` in `IPC/TextFileSave.fs` | Both files compile after `ArcVault/ArcVaultState.fs`, so a service held by the vault cannot call them yet. They depend only on `Bindings` and `Shared`. |
| Protocol bindings | `Fable.Electron` 40.9.2 | `protocol.registerSchemesAsPrivileged` and `protocol.handle` are bound. |

## Resolving a reference

A pure function, `LinkResolution.resolve (baseFolder: string) (raw: string)`, in `MarkdownText/Links`. No I/O, so it is unit tested.

```fsharp
type LinkTarget =
    | External of url: string                              // http, https, mailto
    | Fragment of id: string                               // "#x" in this document
    | ArcPath of path: string * fragment: string option    // ARC-relative, normalized, decoded
    | OutsideArc of raw: string                            // climbs above the ARC root
    | Unsupported of raw: string * reason: string          // other schemes, bad escapes, backslashes
```

Rules, in order:

1. A URL with a scheme is `External` for `http`, `https` and `mailto`, and `Unsupported` for any other scheme (`file:`, `javascript:` and so on).
2. `#x` is `Fragment`.
3. A backslash is `Unsupported`, with the hint "Use / to separate folders." Backslashes only work on Windows and break on DataHUB.
4. Remove `?query`, split off `#fragment` and percent-decode the path (`my%20image.png`). A bad escape is `Unsupported`.
5. `/path` starts at the ARC root.
6. `./path`, `../path` and a bare `path` start at the folder of the open file.
7. `.` and `..` are resolved with POSIX rules. A result above the ARC root is `OutsideArc`, never clamped.

The open file for each host:

| Host | Open file | Base folder |
|---|---|---|
| Text page | The Markdown file | Its folder |
| Notes | The note file | Its folder. A new note is created as a file first and opens in the text page. |
| Investigation description | `isa.investigation.xlsx` | ARC root |
| Study, assay, workflow or run description | Its ISA file | The entity folder, such as `studies/<id>/` (folder names from `ArcPathHelper`, already mapped in `Shared/PathHelpers.fs`) |

## Main process: `MarkdownResolverService`

### Layout

```
Main/Helper/ArcPathContainment.fs                         (moved, see below)
Main/Services/MarkdownResolverService/AssetUrl.fs         build and parse asset URLs (pure)
Main/Services/MarkdownResolverService/AssetPolicy.fs      type allowlist, size cap, LFS pointer check, refusal → status
Main/Services/MarkdownResolverService/MarkdownResolverService.fs
Main/Services/MarkdownResolverService/AssetProtocol.fs    scheme registration and protocol.handle
Main/IPC/IMarkdownResolverApi.fs                          thin IPC layer
Swate.Electron.Shared/IPCTypes.fs                         IMarkdownResolverApi contract
```

- **Move-only refactor first.** `ArcPathValidation`, `tryResolveArcRelativePath`, `checkTargetAsync` and the small helpers they use move to `Main/Helper/ArcPathContainment.fs`, which compiles right after `Bindings`. `FileSystemIO.fs` and `TextFileSave.fs` use it from there. Behavior does not change. This lets the service and the existing save path share one containment check.
- **The service files compile before `ArcVault/ArcVaultState.fs`.** The service knows nothing about `ArcVault`.

### Service

```fsharp
type MarkdownResolverService(windowId: int, getArcRoot: unit -> string option) =
    /// arc-asset://w<windowId>-<nonce>/
    member AssetBaseUrl: string
    /// A new nonce. The vault calls it whenever it opens or creates an ARC. A root rename keeps the nonce.
    member Renew: unit -> unit
    member ServeAsset: request: Request -> JS.Promise<Response>
    member DescribeAsset: arcPath: string -> JS.Promise<AssetRefusal option>
    member CheckPaths: arcPaths: string[] -> JS.Promise<PathCheck[]>
```

- **`ArcVault` owns one instance** as a field: `MarkdownResolverService(window.id, fun () -> arcPath)`. `OpenARC` and `CreateARC` call `Renew`.
- **`AssetProtocol.install`** takes a lookup, `int -> MarkdownResolverService option`. `main.fs` passes `fun id -> ARC_VAULTS.TryGetVault id |> Option.map _.MarkdownResolver`. The service folder therefore never references `ARC_VAULTS`.
  - The scheme registration runs before app ready.
  - `protocol.handle` runs after app ready.
- **`IMarkdownResolverApi`** is its own IPC contract, registered in `main.fs` like the others. `ArcVaultsApi` does not change. Each call takes the window from `windowIdFromIpcEvent`, looks up the vault and forwards to its service.
  - `getAssetBaseUrl: unit -> JS.Promise<Result<string, exn>>`
  - `describeAsset: string -> JS.Promise<Result<AssetRefusal option, exn>>`
  - `checkPaths: string[] -> JS.Promise<Result<PathCheck[], exn>>`
- **Renderer side.** `Renderer/Api.fs` gets `ipcMarkdownResolverApi`. The renderer asks for the asset base URL once per opened ARC, and again after a path change event.

### Asset URL and nonce

```
arc-asset://w<windowId>-<nonce>/<percent-encoded ARC path>
```

- **The window id routes** the request to the vault with one dictionary lookup.
- **The nonce authorizes it.** 16 random bytes, hex.
  - Window ids are small sequential integers. The HTML preview frame runs the scripts of ARC HTML files. Without the nonce, those scripts could load another window's images to see which exist and how large they are, and send that out over the network.
  - After an ARC switch, an `<img>` still on screen would otherwise load a file of the new ARC.
- **Literal URLs fail.** An `arc-asset:` URL written in a document is dropped by the sanitizer. Only URLs the renderer builds after sanitizing reach the page.

### Serving an image

Register the scheme with `standard` and `secure`. Do not set `supportFetchAPI` or `corsEnabled`, so scripts cannot read the bytes. For each request:

1. Parse the host into the window id and nonce, find the service and compare the nonce.
2. Decode the path and check it with `tryResolveArcRelativePath`. Never trust the URL.
3. Run `checkTargetAsync`. A target outside the real ARC root, a symbolic link or anything other than a regular file is refused.
4. Check the extension against the allowlist and the size against the cap.
5. A file under 1 KB is read once to check for a Git LFS pointer. Pointer files are about 130 bytes. A pointer is refused and never resolved.
6. Stream the file with `Cache-Control: no-store` and the `Content-Type` of its type.

**Allowlist:** `png`, `jpg`/`jpeg`, `gif`, `svg`, `webp`, `bmp`.
- SVG is safe here: it loads as an image, so its scripts never run.
- **Size cap:** 50 MB.

| Refusal | Status | Notice in the preview |
|---|---|---|
| Unknown window or wrong nonce | 403 | "This image link is from an ARC that is no longer open." |
| Outside the ARC, or a symbolic link | 403 | "This image lies outside the ARC." |
| Missing | 404 | "Image not found: `<path>`" |
| Git LFS pointer | 409 | "This image is stored in Git LFS and is not downloaded." |
| Larger than 50 MB | 413 | "This image is larger than 50 MB." |
| Type not on the allowlist | 415 | "`.tif` images cannot be shown." |

- **The `img` renderer** sets `loading="lazy"` and `decoding="async"`. On `onError` it calls `describeAsset` and replaces the image with the notice. Only failed images cost a round trip.
- **Freshness.** `no-store` plus the remount of the preview when it is shown keeps images current. A file renamed while the preview is open shows as missing on the next render. Nothing follows the rename.

## Renderer: context and preview

`Preview` gets an optional context. It is an interface (a plain JS object), so JS consumers of the npm package can supply it too. No F# union crosses the boundary.

```fsharp
type IMarkdownLinkContext =
    /// ARC-relative folder of the open file. "" is the ARC root.
    abstract baseFolder: string
    /// The URL an <img> loads for an ARC path, or None when the host cannot serve files.
    abstract assetUrl: arcPath: string -> string option
    /// Why an ARC path cannot be shown, for the notice of a failed image.
    abstract describeAsset: arcPath: string -> JS.Promise<string option>
    abstract openArcPath: arcPath: string -> unit
    abstract openExternal: url: string -> unit
```

- `Preview.components` becomes `Preview.componentsFor context`. The host memoizes it per base folder. A new object on each render makes react-markdown rebuild every element and reload every image.
- Without a context (web Swate, the Changelog, a note draft without a file), ARC paths render as inert links with a title that says file links need an open ARC. Images become a placeholder. External links keep working.

### Links

| Target | Click in the preview |
|---|---|
| `External` | `openExternal` |
| `Fragment` | Scroll to `user-content-<id>` inside the preview container |
| `ArcPath`, file | `openArcPath`. In Electron this is the flow of `NotesSearchTarget.openNote`: `openFile`, select it in the tree, `setPageState`. `setPageState` asks the leave guard, and `openFile` already picks the right page. A fragment in the link is ignored. |
| `ArcPath`, folder | Show the File Explorer, open the folder and focus its row |
| `OutsideArc`, `Unsupported` | Not a link. The title gives the reason. |

- **Which events.** The `a` renderer handles click, middle click (`auxclick`) and Enter. The `href` keeps the original text, so hover shows what the author wrote.
- **Main-process hardening** (in `ArcVaultHelper.createWindow`, not part of the service). `setWindowOpenHandler` and `onWillNavigate` pass only `http`, `https` and `mailto` to `shell.openExternal` and drop everything else. This also stops today's bogus `file:` hand-off.
- **Id prefix.** The sanitizer's `user-content-` prefix stays. Without it, a heading named "root" would get `id="root"`, the id of the app's mount element.

### Raw HTML

- Put `rehype-raw` before `rehype-sanitize` in `Preview.rehypePlugins`.
- Do **not** use the wrapper's `skipHtml={false}`. It appends `rehype-raw` after the caller's plugins, so HTML would be parsed after the sanitizer ran and reach the page unsanitized.
- **What the default schema allows:** `width`, `height` and `align` on every element, plus `details`/`summary`, `sub`/`sup` and `kbd`.
- **What it strips:** `script`, `iframe`, `video`, `picture` and event-handler attributes.
- Raw `<img>` and `<a>` go through the same `urlTransform` and renderers as Markdown syntax.

## Editor: link warnings

- **When.** After each change, debounced to 500 ms, parse with `Mdast.parse`. Collect the URLs of links, images and definitions. Raw HTML nodes give their `src` and `href` through the HTML parser `rehype-raw` uses.
- **One call.** Resolve the URLs, keep the unique `ArcPath` targets (cap 500), and send one `checkPaths` call. It returns one result per path: `Missing`, `File`, `Directory` or `CaseDiffers(onDisk)`.
- **Diagnostics,** through the lint infrastructure of the text editor:
  - **Errors:** missing targets, `OutsideArc` and `Unsupported`, including the backslash hint.
  - **Warnings:** `CaseDiffers`: "On disk this is `images/a.png`. The link breaks on systems that compare case, such as DataHUB." There is no quick fix.
- **Case check.** `fs.realpath.native` returns the case on disk on Windows. Its ARC-relative part is compared with the resolved path segment by segment, case-sensitively.
  - When the real path differs by more than case (a symbolic link in the chain), that link gets no case warning.
  - On Linux a case mismatch already shows up as `Missing`.
  - macOS needs a short spike to confirm that `realpath.native` returns the case on disk there.
- **No cache.** One run costs at most 500 `stat` calls plus the `realpath` calls.

## ISA description fields

- **Base folder:** the folder of the ISA file (see the table under "Resolving a reference"). The metadata form host builds the context. Web Swate passes none.
- **Caveat:** DataHUB and the ISA-JSON and RO-Crate exports show descriptions without a base folder, so relative references in descriptions only resolve in ARCitISA.
- **Order:** this milestone depends on the description fields rendering through the shared Markdown preview.

## Performance

| Operation | Cost |
|---|---|
| Render | Nodes of the document. Images load when they scroll into view. |
| Image request | One lookup, `realpath`, `lstat`, then a stream. |
| Link warnings | Unique targets of the document, capped at 500, debounced. |
| Files nobody references | Nothing. No index, no tree walk, no extra watcher. |

## Tests

- **Unit, components (`.test.fs`):** the resolution rules, table-driven. Cover `/`, `./`, `../` and bare paths, `..` above the root, escapes, backslashes, fragments and schemes.
- **Unit, main (`tests/Electron.Core/MarkdownResolverService.test.fs`):**
  - Building and parsing asset URLs, including a wrong nonce and a renewed nonce.
  - The refusal → status mapping, and the segment-wise case comparison.
  - `ServeAsset` and `CheckPaths` against a temp ARC folder, next to `TextDocumentIO.test.fs`: a symbolic link out of the ARC, an LFS pointer, the size cap and an unsupported type.
- **RTL (`.test.rtl.fs`):**
  - The `a` renderer calls `openArcPath` or `openExternal`, and a fragment scrolls.
  - `img` gets the asset URL with `loading="lazy"`, and a failed image shows its notice.
  - Raw HTML is sanitized: `<img src=x onerror=…>` loses `onerror`, `<script>` is gone.
  - Without a context, links are inert.
- **Storybook (`.stories.tsx`):** the preview with a fake context that shows every image state.

## Milestones

1. **Links and images.**
   - The move-only refactor to `ArcPathContainment.fs`.
   - `MarkdownResolverService` with the protocol, `IMarkdownResolverApi` (`getAssetBaseUrl`, `describeAsset`) and the vault field.
   - Link resolution, the context, the `a` and `img` renderers, and raw HTML.
   - The open-handler hardening. The text page and the notes editor get their context.
2. **Link warnings.** `checkPaths` and the warnings, including case mismatches.
3. **ISA description fields.** Once they render through the shared preview.

## Differences from the implementation

- **URL format.** `arc-asset` URLs are built and parsed in `Swate.Electron.Shared/MarkdownAssetUrl.fs`, so the main process and the renderer share one encoder. The service folder holds `AssetPolicy.fs`, `MarkdownResolverService.fs` and `AssetProtocol.fs`.
- **No `urlTransform`.** The `a` and `img` renderers resolve the sanitized `href` and `src` themselves. They read the link context from a React context, so the `components` object stays the same across renders and images never remount. A failed image still knows its ARC path for `describeAsset`.
- **Sanitizer schema.** It drops `picture` and `source`. The default schema keeps `<source srcset>` and never checks the protocols in `srcset`.
- **File or folder links.** `checkPaths` already runs in milestone 1. A link click uses it to tell a file from a folder. A folder link shows the File Explorer, opens the folder, scrolls to it and focuses its row. The selection stays as it is. A link to a Git LFS pointer file shows a message and opens nothing.
- **Raw HTML in link warnings.** `LinkCollection` parses raw HTML with `hast-util-from-html` and reports a broken `src` or `href` on the range of the whole HTML node.
- **Fragment scroll.** A `#heading` link scrolls to the element with the id `user-content-<heading>` in the whole page. The sanitizer gives every id in the preview that prefix.
- **Lint delay.** Link warnings use the shared CodeMirror lint delay of 300 ms.
- **Notes.** A new note asks only for a title, is created as a file and opens in the text page, so it gets the link context of that file.
- **Milestone 3.** The ISA description fields are plain text areas today, so they get no context yet.
- **Nonce.** It also renews when a failed initialization resets the vault. Closing a window disposes its vault, so its URLs stop resolving.
- **macOS case check.** The spike did not happen. On macOS the case warning works only if `realpath` returns the case on disk there.

## Manual tests

Restart `npm start` in src/Electron first, because the watch does not pick up new files in the .fsproj files. Then open an ARC and create these files:

- `docs/guide.md` for the links and images below, plus `docs/other.md` with a heading.
- `images/ok.png` (any small image), `images/big.png` (larger than 50 MB), `images/scan.tif`, `data.csv`.
- `images/pointer.png` and `pointer.csv`, each holding only an LFS pointer text:

  ```
  version https://git-lfs.github.com/spec/v1
  oid sha256:0000000000000000000000000000000000000000000000000000000000000000
  size 123
  ```

Open `docs/guide.md` and switch to Preview, unless a test says otherwise.

| # | Put in docs/guide.md | Expected |
|---|---|---|
| 1 | (any text) | The app starts, the page opens, the developer console shows no "No API found on window" error. |
| 2 | `![ok](../images/ok.png)` and `![root](/images/ok.png)` | Both images show. |
| 3 | `<img src="../images/ok.png" width="80">` | The image shows 80 px wide. |
| 4 | `![m](../images/missing.png)` | Notice "Image not found: images/missing.png". |
| 5 | `![b](../images/big.png)` | Notice "This image is larger than 50 MB." |
| 6 | `![t](../images/scan.tif)` | Notice ".tif images cannot be shown." |
| 7 | `![p](../images/pointer.png)` | Notice "This image is stored in Git LFS and is not downloaded." Nothing is downloaded. |
| 8 | `![o](../../x.png)`, `![w](..\images\ok.png)`, `![c](C:/x.png)` | Notices "This image lies outside the ARC.", "Use / to separate folders." and the scheme message. |
| 9 | `[other](./other.md)` | A click opens other.md and selects it in the tree. A middle click does the same. |
| 10 | Same link, with unsaved edits in guide.md | The leave dialog appears. Cancel keeps guide.md open with its edits. |
| 11 | `[imgs](../images/)` | A click shows the File Explorer, opens images/, scrolls to it and focuses its row. The page and the selection stay. |
| 12 | `[gone](missing.md)` and `[root](/)` | A dismissible alert: "The link target does not exist: docs/missing.md", and "This link points to the ARC root folder." |
| 13 | `[ptr](/pointer.csv)` | Alert "This file is stored in Git LFS and is not downloaded." The pointer does not open. |
| 14 | `[web](https://example.org)` | Opens in the system browser. The app does not navigate. |
| 15 | `## My Heading`, then `[go](#my-heading)` and `<a id="spot"></a>` with `[spot](#spot)` | Both clicks scroll the preview to their target. |
| 16 | `[x](../../x.md)` and `[j](javascript:alert(1))` | Plain text with a dotted underline. The tooltip says why. |
| 17 | `<img src="x.png" onerror="alert(1)">`, `<script>alert(1)</script>`, `<details><summary>S</summary>B</details>` | No alert box. The details block opens and closes. |
| 18 | Editor (Edit mode): `[gone](missing.md)` | A red underline with "Not found: docs/missing.md" after about half a second. Fixing the path removes it. |
| 19 | Editor on Windows: `![c](../Images/OK.png)` | The preview shows the image. The editor shows a warning "On disk this is \"images/ok.png\"...". |
| 20 | Editor: `[j](javascript:alert(1))` and `[o](../../x.md)` | Red underlines with the scheme and the outside-the-ARC messages. |
| 21 | Open another ARC in the same window, then paste an old image URL from the developer tools into an `<img>` in an HTML file of the new ARC | The image does not load. |
| 22 | Rename the ARC root folder from the app, then preview guide.md again | The images still show. |
| 23 | Add Note on a study folder, title "Lab day", then `[x](a.md)` in the opened note | The note opens as studies/<id>/protocols/Lab_day/Lab_day.md. The link resolves against that folder. |
| 24 | A large ARC (many folders), open guide.md and preview it | Opening and previewing stay instant. The tree does not expand on its own. No CPU spike. |

## Decisions

Decided on 2026-11-18:

- `/path` always resolves against the ARC root. `./path`, `../path` and a bare `path` resolve against the folder of the open file.
- Case mismatches show a warning, without a quick fix.
- References in ISA descriptions resolve against the folder of the ISA file.
- Raw HTML is allowed, sanitized.
- Asset URLs carry the window id and a nonce. All Markdown file access goes through `MarkdownResolverService`, a field of `ArcVault`, with its own IPC contract.
- Images: at most 50 MB. Types: png, jpg/jpeg, gif, svg, webp, bmp.
- Paths with backslashes are refused with a hint to use `/`.
- Git LFS files are never resolved or downloaded for the user.
- Renames and moves never update references.

Still open: whether `fs.realpath.native` returns the case on disk on macOS. The spike planned for milestone 2 did not happen.
