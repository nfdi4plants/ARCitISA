# Markdown Toolbar and Ontology References: Plan

Status: proposal, 2026-11-17. Nothing here is built yet.

This plan covers a formatting toolbar for Markdown files in the text editor page, and a search that inserts an ontology term as a reference-style link. Decisions that need an answer are listed at the end.

## Goals

- Markdown files in the text editor page get a toolbar with the formatting people use most, plus keyboard shortcuts for the most common ones.
- "Ontology reference" opens the term search. The chosen term is inserted as `[name][label]` at the cursor, and its link definition goes at the end of the file.
- The links resolve for every ontology the search can return, not only OBO ontologies.
- The Markdown edits stay independent of CodeMirror, so the note editor and the description fields of metadata forms can reuse them later.

Out of scope for now: moving `TextInputWithMarkdown` (notes) onto the new layer, images (the existing `AddImage` plugin covers that), and multiple cursors (the commands act on the main selection).

## What exists today

| Piece | Where | Relevance |
|---|---|---|
| CodeMirror 6 editor with the `markdown()` language | `src/Components/src/Composite/TextEditor` | Enter already continues lists and quotes (markdown keymap). Its handle (`TextEditorHandle`) has no selection or edit API yet. |
| Toolbar on a textarea | `MarkdownText/TextInputWithMarkdown.fs` | Uses `@uiw/react-md-editor` commands, which only work on a textarea. Not reusable for CodeMirror. |
| Prompt plugins `AddStep`, `AddImage`, `AddOntologyReference` | `MarkdownText/Plugins` | Their `Apply: text -> selStart -> selEnd -> input -> text * selection` is already a pure edit, the shape this plan generalizes. |
| `AddOntologyReference` | `MarkdownText/Plugins/AddOntologyReference.fs` | A free-text prompt (`name \| accession`) with no search. It inserts an inline link built by ARCtrl's `TermAccessionAndOntobeeUrlIfShort`, which turns every short accession into `http://purl.obolibrary.org/obo/PREFIX_ID`. |
| `TermSearch` component | `Composite/TermSearch` | In Electron it searches Swate DB (built in) and the TIB collections (`TIBQueryProvider` in `Renderer/App.fs`). `disableDefaultSearch` turns Swate DB off. |

What a search hit carries (`TermSearch.Types.Term`):

| Source | `id` | `href` |
|---|---|---|
| TIB (`Types.fs`, `ToMyTerm`) | `obo_id`, for example `MS:1000031` (can be empty for non-OBO-style IRIs) | `iri`, the term's real IRI |
| Swate DB (`TermSearch.fs`, `ToComponentTerm`) | `Accession`, for example `MS:1000031` | none |
| A hit found in both | merged | the TIB `iri` wins |

## Toolbar

### Commands

| Group | Command | Markdown | Shortcut | Behaviour |
|---|---|---|---|---|
| Text | Heading 1–3 (menu) | `# ` … `### ` | – | Sets the heading level of the touched lines. The same level again removes it. |
| | Bold | `**…**` | Mod-B | Wraps the selection, or unwraps it when the markers sit just inside or outside it. Without a selection it inserts `****` with the cursor in the middle. |
| | Italic | `_…_` | Mod-I | Like bold. Mod-I replaces CodeMirror's default *select parent syntax*, so the keymap needs high precedence. |
| | Strikethrough | `~~…~~` | – | Like bold (GFM). |
| Code and links | Inline code | `` `…` `` | Mod-E | Like bold. A selection containing backticks gets a longer fence. |
| | Code block | ```` ```lang ```` | – | Fences the touched lines, with the cursor after the opening fence to type the language. |
| | Link | `[text](url)` | Mod-K | The selection becomes the text and the `url` placeholder is selected. Without a selection, `text` is selected. |
| | Quote | `> ` | – | Toggles the prefix on the touched lines. |
| Lists | Bulleted list | `- ` | – | Toggles the prefix on the touched lines. |
| | Numbered list | `1. ` | – | Numbers the touched lines 1…n, or removes the numbers. |
| | Task list | `- [ ] ` | – | Toggles the prefix on the touched lines. |
| Insert | Table | GFM 2×2 | – | Inserts a header row, a separator and one row on new lines, with the first header cell selected. |
| Ontology | Ontology reference | see below | Mod-Shift-O (optional) | Opens the term search. |

Line commands remove their prefix when every touched line already has it, and add it otherwise. All commands leave the editor focused, and each one is a single undo step.

### Placement

- The toolbar shows only for Markdown files, in Edit mode. Preview hides it.
- For a read-only file (`TextReadOnlyReason`) it stays visible but disabled, with the reason as tooltip.
- It sits in a second row under the page header in `TextDocumentTarget.fs`, so the path, status and Save button keep their room.
- Icons use `swt:iconify` with `swt:fluent--…` classes (for example `fluent--text-bold-24-regular`). Groups are DaisyUI `join` blocks with `btn-ghost btn-sm` buttons, and every button has an `aria-label` and a tooltip that names its shortcut.

## Architecture

Three layers, so the Markdown logic knows nothing about CodeMirror (see the reuse plan for notes and metadata descriptions):

1. **Pure edits**: `MarkdownText/Editing/MarkdownEdits.fs`, new.
   ```fsharp
   type TextChange = { From: int; To: int; Insert: string }
   type MarkdownEdit = { Changes: TextChange list; Selection: int * int }

   // One function per command: current text and selection in, edit out.
   val toggleBold: text: string -> selection: int * int -> MarkdownEdit
   val toggleLinePrefix: prefix: string -> text: string -> selection: int * int -> MarkdownEdit
   val insertOntologyReference: reference: OntologyReference -> text: string -> selection: int * int -> Result<MarkdownEdit, string>
   ```
   Changes rather than a whole new text, so the editor keeps its scroll position and undo history. The existing prompt plugins can be wrapped by computing the changed range from their `Apply` result.
2. **Text surface**, a record of functions the toolbar works against:
   ```fsharp
   type MarkdownSurface = {
       getText: unit -> string
       getSelection: unit -> int * int
       apply: MarkdownEdit -> unit
       focus: unit -> unit
   }
   ```
   `TextEditorHandle` gains `getSelection` and `applyEdit`, which applies one CodeMirror transaction with `scrollIntoView`. A textarea surface for `TextInputWithMarkdown` can follow later with no change to layer 1.
3. **UI**: `MarkdownText/MarkdownToolbar.fs` (component, takes a surface and `disabled`) and `MarkdownText/OntologyReferenceDialog.fs`. The shortcuts come from the same command list as a CodeMirror `keymap`. `TextEditor` takes them as an extra extension, so the editor stays generic.

## Ontology reference

### Inserted text

With the cursor after "measured with an" and the term *instrument model* (`MS:1000031`) chosen:

```markdown
The sample was measured with an [instrument model][MS:1000031].

...rest of the document...

[MS:1000031]: http://purl.obolibrary.org/obo/MS_1000031 "instrument model"
```

- **Link text** is the selected text if there is one, otherwise the term name.
- **Label** is the CURIE (`id`). It's short, unique per term, and readable in the raw file. For a term without a CURIE, the label is the last segment of its IRI, after `#` or `/`. Characters `[`, `]` and `\` in labels are escaped.
- **Definition** goes at the end of the file. If the file already ends with link definitions, it joins that block; otherwise it starts a new block after a blank line. The term name goes in the title (`"…"`), so the preview shows it on hover and the raw file shows which term an IRI belongs to.
- **Repeats** reuse the definition. CommonMark matches labels without regard to case or extra whitespace.
- **Conflicts:** if the label already points to a different URL, the insert is refused and the dialog says so ("[MS:1000031] already links to …"). Nothing is replaced silently.
- **Undo:** the inline link and the definition are one transaction, so one undo removes both.

### Which URL: OBO only?

Yes, if the link is built from the accession. `TermAccessionAndOntobeeUrlIfShort` (ARCtrl, used by the current plugin and by `Term` built from an `OntologyAnnotation` in `TermSearch/Types.fs`) always produces `http://purl.obolibrary.org/obo/PREFIX_ID`. That only resolves for prefixes registered with the OBO PURL service. Checked on 2026-11-17, first response only:

| URL | First response |
|---|---|
| `purl.obolibrary.org/obo/MS_1000031` (PSI-MS, OBO) | 303 to the term in Ontobee |
| `purl.obolibrary.org/obo/EFO_0000001` (EFO, not OBO) | 302 to the legacy `purl.oclc.org/obo/…`, not a term page |
| `purl.obolibrary.org/obo/NFDI4PSO_0000064` | 302 to the legacy `purl.oclc.org/obo/…` |

Alternatives:

| Option | How | Covers | Drawbacks |
|---|---|---|---|
| **A. The term's IRI from the search hit** | `Term.href`, filled from the TIB `iri` | Every ontology TIB indexes, OBO or not. Example: TIB returns `http://purl.obolibrary.org/obo/MS_1000031` for MS. | Swate DB hits have no IRI. Some non-OBO IRIs are identifiers that don't open a page. |
| **B. Look up the IRI by CURIE at TIB** | `GET https://api.terminology.tib.eu/api/terms?obo_id=MS:1000031` → `iri` | Same as A, for hits without `href` | One request per insert. Finds nothing for ontologies TIB doesn't index (`NFDI4PSO:0000064` returned 0 results). |
| **C. Bioregistry resolver** | `https://bioregistry.io/{PREFIX}:{ID}` | About 3,000 registered prefixes. It redirects to the provider: MS → OBO PURL, EFO → `www.ebi.ac.uk/efo/EFO_0000001`, EDAM → `edamontology.org/data_0006`. | The link points at a resolver, not at the term's own identifier, and depends on bioregistry.io staying up. Unregistered prefixes (possibly DataPLANT's own) fail when clicked. |
| **D. identifiers.org** | `https://identifiers.org/{PREFIX}:{ID}` | A smaller set of ontology prefixes. Example: GO → AmiGO. | Same as C, with less coverage. Bioregistry covers its prefixes too. |
| **E. OBO PURL from the CURIE** (today) | `purl.obolibrary.org/obo/PREFIX_ID` | OBO prefixes only | Produces dead links for everything else without any warning. |

**Recommendation:** A, then B, then refuse.

1. Use `href` when the hit has one.
2. Otherwise look the CURIE up at TIB and use the `iri` it returns.
3. If neither gives an IRI, refuse with a visible reason: "No IRI found for NFDI4PSO:0000064." The dialog then offers to insert the name and CURIE as plain text.

This never guesses a URL, which matches how errors are handled elsewhere in the editor.

C is the fallback if dead ends turn out to be common for DataPLANT ontologies that TIB doesn't index. It's a one-line change in the URL step, but it's a policy choice (decision 1).

The same choice applies to `TermSearch/Types.fs` (`href` from `TermAccessionOntobeeUrl`) and to the old `AddOntologyReference` plugin. Both keep the OBO assumption until they move to the new layer.

### Dialog

- The toolbar button (and the optional shortcut) opens a DaisyUI modal containing `TermSearch` with `autoFocus`.
- Choosing a term shows its name, CURIE, ontology and the URL that will be written. **Insert** applies the edit; **Cancel** returns focus to the editor.
- Resolving the IRI (step 2 above) shows a spinner on the Insert button. A failed lookup or a refused insert shows the reason inside the dialog and inserts nothing.
- The search sources follow the app's `TermSearchConfigProvider`. To guarantee an IRI on every hit, the dialog could pass `disableDefaultSearch = true` for a TIB-only search, but that loses Swate DB-only terms (decision 5).

## Tests

- **Unit (`.test.fs`)** on `MarkdownEdits`:
  - wrap and unwrap for each inline command, with and without a selection, and at the start and end of the text
  - line prefix toggling over several lines, and renumbering of numbered lists
  - code fences around text that contains backticks
  - references: a new block, appending to an existing block, reusing a definition, refusing a conflicting label, escaping labels, IRI-segment labels for terms without a CURIE
  - URL selection: `href`, then lookup result, then refusal. The lookup goes in as a function, so tests don't touch the network.
- **RTL (`.test.rtl.fs`)**:
  - the toolbar disables its buttons when read-only
  - a click applies the edit to a fake surface
  - the dialog inserts on select and shows the conflict and no-IRI messages
- **Storybook (`.stories.tsx`)**: `MarkdownToolbar` on a real `TextEditor`, and the dialog with a mocked search.
- **By hand** in Electron: undo after every command, the shortcuts (Mod-I precedence), and the reference links rendering in Preview.

## Milestones

1. `MarkdownEdits` with the formatting commands, plus unit tests.
2. `getSelection` and `applyEdit` on `TextEditorHandle`; the extra keymap extension on `TextEditor`.
3. `MarkdownToolbar` with story and RTL tests; integration in `TextDocumentTarget` for Markdown files.
4. Reference insertion (`insertOntologyReference`, URL selection, TIB lookup by CURIE) with unit tests.
5. `OntologyReferenceDialog` with RTL tests, wired into the toolbar.
6. Later: `TextInputWithMarkdown` (notes, metadata descriptions) moves to the new layer with a textarea surface, and the old prompt plugin goes away.

## Decisions

1. **No IRI found:** refuse with a reason (recommended), fall back to Bioregistry (C), or keep the OBO PURL guess (E)?
2. **Definition title:** put the term name in the title (`"instrument model"`)? Recommended yes.
3. **Label:** CURIE (recommended) or the term name?
4. **Toolbar position:** a second row under the header (recommended), or inside the header row?
5. **Search sources in the dialog:** the app's providers, Swate DB plus TIB (recommended), or TIB only, so every hit has an IRI?
