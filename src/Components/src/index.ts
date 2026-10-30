import '../tailwind.css';
import type { SortableList as SortableListComponent } from './dist/Composite/SortableList/SortableList.fs';

export type SortableListItem<A> = Parameters<typeof SortableListComponent<A>>[0]['items'][number];
export type SortableListRowRender<A> = Parameters<NonNullable<Parameters<typeof SortableListComponent<A>>[0]['renderRow']>>[0];

// ---------------------------------------------------------------------------
// Primitive
// ---------------------------------------------------------------------------

export { default as BaseModal } from './dist/Primitive/BaseModal/BaseModal.fs';
export { default as Blankslate } from './dist/Primitive/Blankslate/Blankslate.fs';
export { default as CardGrid } from './dist/Primitive/CardGrid/CardGrid.fs';
export { default as ComboBox } from './dist/Primitive/ComboBox/ComboBox.fs';
export { default as ContextMenu } from './dist/Primitive/ContextMenu/ContextMenu.fs';
export { default as Dropdown } from './dist/Primitive/Dropdown/Dropdown.fs';
export { default as ErrorModal } from './dist/Primitive/ErrorModal/ErrorModal.fs';
export { default as ErrorModalProvider } from './dist/Primitive/ErrorModal/Provider.fs';
export { default as LoadingSpinner } from './dist/Primitive/LoadingSpinner/LoadingSpinner.fs';
export { default as Navbar } from './dist/Primitive/Navbar/Navbar.fs';
export { default as Popover } from './dist/Primitive/Popover/Popover.fs';
export { default as MultiSelect } from './dist/Primitive/Select/MultiSelect.fs';
export { default as SingleSelect } from './dist/Primitive/Select/SingleSelect.fs';
export * as Icons from './dist/Primitive/Icons.fs';

export { DeleteButton, CircularExitButton, CollapseButton, QuickAccessButton } from './dist/Primitive/Buttons/Buttons.fs';
export { LayoutComponents } from './dist/Primitive/LayoutComponents/LayoutComponents.fs';
export { Dialog, StringSubmissionDialog } from './dist/Primitive/Dialog/Dialog.fs';

// ---------------------------------------------------------------------------
// Composite
// ---------------------------------------------------------------------------

export { default as Actionbar } from './dist/Composite/Actionbar/Actionbar.fs';

export { default as AccountManager } from './dist/Composite/Authentication/AccountManager.fs';
export { default as AnnotationTable } from './dist/Composite/AnnotationTable/AnnotationTable.fs';
export { default as ArcSelector } from './dist/Composite/ArcSelector/ArcSelector.fs';
export { default as ArcVaultActions } from './dist/Composite/ArcVaultActions/ArcVaultActions.fs';
export { default as Authentication } from './dist/Composite/Authentication/Authentication.fs';
export { default as BuildingBlockWidget } from './dist/Composite/Widgets/BuildingBlockWidget/BuildingBlockWidget.fs';
export { default as DataAnnotator } from './dist/Composite/Widgets/DataAnnotator/DataAnnotator.fs';
export { default as DataMapTable } from './dist/Composite/DataMapTable/DataMapTable.fs';
export { default as FilePickerWidget } from './dist/Composite/Widgets/FilePickerWidget.fs';
export { default as JsonExport } from './dist/Composite/Widgets/JsonExport/JsonExport.fs';
export { default as JsonImport } from './dist/Composite/Widgets/JsonImport/JsonImport.fs';
export { default as Layout } from './dist/Composite/Layout/Layout.fs';
export { default as MarkdownTextInput } from './dist/Composite/MarkdownText/TextInputWithMarkdown.fs';
export { default as Notes } from './dist/Composite/Notes/Editor/Notes.fs';
export { default as SortableList } from './dist/Composite/SortableList/SortableList.fs';
export { DragHandle, MoveUpButton, MoveDownButton, RemoveButton, DefaultRow } from './dist/Composite/SortableList/RowComponents.fs';
export { default as Table } from './dist/Composite/Table/Table.fs';
export { default as TemplateBrowser } from './dist/Composite/TemplateBrowser/TemplateBrowser.fs';
export { default as TemplateCacheProvider } from './dist/Composite/TemplateBrowser/TemplateCacheProvider.fs';
export { default as TemplateFilter } from './dist/Composite/TemplateBrowser/TemplateFilter.fs';
export { default as TemplateImportModal } from './dist/Composite/TemplateBrowser/TemplateImportModal.fs';
export { default as ImportModeSelect } from './dist/Composite/TableImport/ImportModeSelect.fs';
export { default as TableImportPreview } from './dist/Composite/TableImport/TableImportPreview.fs';
export { default as TemplatesDisplay } from './dist/Composite/TemplateBrowser/TemplatesDisplay.fs';
export { default as TemplateWidget } from './dist/Composite/Widgets/TemplateWidget.fs';
export { default as TermSearch } from './dist/Composite/TermSearch/TermSearch.fs';
export { default as TermSearchConfigProvider } from './dist/Composite/TermSearch/ConfigProvider.fs';
export { default as ThemeProvider } from './dist/Composite/ThemeSelector/ThemeProvider.fs';
export { default as ThemeSelector } from './dist/Composite/ThemeSelector/ThemeSelector.fs';
export { default as TutorialOverlay } from './dist/Composite/TutorialOverlay/TutorialOverlay.fs';

export { Main as NoteSearch, SearchSuggestion } from './dist/Composite/Notes/NoteSearch/NoteSearch.fs';
export { WidgetController, Entry as WidgetEntry } from './dist/Composite/Widgets/Widgets.fs';

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export { default as ArcFileEditor } from './dist/Page/ArcFileEditor/ArcFileEditor.fs';
export { default as EmptyTableView } from './dist/Page/ArcFileEditor/EmptyTableView/Main.fs';
export { default as DataHubBrowser } from './dist/Page/DataHubBrowser/DataHubBrowser.fs';
export { default as FileExplorer } from './dist/Page/FileExplorer/FileExplorer.fs';
export { default as GitPagedDiffViewer } from './dist/Page/GitComparison/GitPagedDiffViewer.fs';
export * from './dist/Page/GitComparison/GitPagedDiff.Types.fs';
export { default as GitSidebar } from './dist/Page/GitSidebar/GitSidebar.fs';
export { default as Landing } from './dist/Page/Landing/Landing.fs';
export { default as Changelog } from './dist/Page/Changelog/Changelog.fs';
export { default as SettingsPage } from './dist/Page/SettingsPage/SettingsPage.fs';
export { default as ProvenanceGrouping } from './dist/Page/ProvenanceGrouping/ProvenanceGrouping.fs';
export { default as ValidationPackageBrowser } from './dist/Page/ValidationPackageBrowser/ValidationPackageBrowser.fs';

export { Viewer as GitMergeConflictViewer } from './dist/Page/GitComparison/GitMergeConflictViewer.fs';
export { TitleStack, HeaderRow, PanelShell, SectionCard } from './dist/Page/GitComparison/GitComparisonView.fs';

// -- Metadata --
export { default as ArcFileMetadata } from './dist/Page/Metadata/ArcFileMetadata.fs';
export { default as AssayMetadata } from './dist/Page/Metadata/AssayMetadata.fs';
export { default as DataMapMetadata } from './dist/Page/Metadata/DataMapMetadata.fs';
export { default as InvestigationMetadata } from './dist/Page/Metadata/InvestigationMetadata.fs';
export { default as RunMetadata } from './dist/Page/Metadata/RunMetadata.fs';
export { default as StudyMetadata } from './dist/Page/Metadata/StudyMetadata.fs';
export { default as TemplateMetadata } from './dist/Page/Metadata/TemplateMetadata.fs';
export { default as WorkflowMetadata } from './dist/Page/Metadata/WorkflowMetadata.fs';

// ---------------------------------------------------------------------------
// Api
// ---------------------------------------------------------------------------

export { SwateApi } from './dist/Api/SwateApi.fs';
export { TIBApi } from './dist/Api/TIBApi.fs';
export { GitLabApi } from './dist/Api/GitLabApi.fs';
