import React, { Fragment } from 'react';
import TermSearch from '../src/Composite/TermSearch/TermSearch.fs.ts';
import {Entry as Table} from '../src/Composite/Table/Table.fs.ts';
import {Entry as AnnotationTable} from '../src/Composite/AnnotationTable/AnnotationTable.fs.ts';
import AnnotationTableCtxProvider from '../src/Composite/AnnotationTable/ContextProvider.fs.ts';
import {Example as ContextMenuExample, ContextMenu} from '../src/Primitive/ContextMenu/ContextMenu.fs.ts';
import {TIBApi} from '../src/Api/TIBApi.fs.ts';
import {Entry as TemplateFilter} from '../src/Composite/TemplateBrowser/TemplateFilter.fs.ts';
import {Entry as ComboBox} from '../src/Primitive/ComboBox/ComboBox.fs.ts';
// import {Entry as Select} from '../src/Primitive/Select/Select.fs.ts';
import {Entry as BaseModal} from '../src/Primitive/BaseModal/BaseModal.fs.ts';
import { Wizard as LandingWizard } from '../src/Page/Landing/Landing.fs.ts';
import { Exports_createLandingDraft as createLandingDraft, Exports_createLandingUiState as createLandingUiState } from '../src/Page/Landing/Types.fs.ts';
import {TIBQueryProvider as TermSearchConfigProvider} from '../src/Composite/TermSearch/ConfigProvider.fs.ts';
import {Entry as TermSearchConfigSetter} from '../src/Composite/TermSearch/ConfigSetter.fs.ts';
import { Term } from '../../Shared/Database.fs.ts';
import {Entry as DataMapTable} from '../src/Composite/DataMapTable/DataMapTable.fs.ts';
import {Entry as Layout} from '../src/Composite/Layout/Layout.fs.js'
import {FileExplorerExample_Example} from '../src/Page/FileExplorer/FileExplorer.fs.ts'
import {Entry as WidgetController} from '../src/Composite/Widgets/Widgets.fs.ts';
import {Entry as TextInputWithMarkdown} from '../src/Composite/MarkdownText/TextInputWithMarkdown.fs.ts';
import {Entry as AuthButton} from '../src/Composite/Authentication/Authentication.fs.ts';
import {GitLabEntry as DataHubBrowser} from '../src/Page/DataHubBrowser/DataHubBrowser.fs.ts';
import {Entry as ARCSelectorEntry} from '../src/Composite/ArcSelector/ArcSelector.fs.ts';
import {Entry as ArcFileEditor} from '../src/Page/ArcFileEditor/ArcFileEditor.fs.ts';
import {Entry as SettingsPage} from '../src/Page/SettingsPage/SettingsPage.fs.ts';
import WorkspaceEntry from '../src/Composite/Workspace/Workspace.fixture.fs.ts';
import ValidationPackageBrowserFixture from '../src/Page/ValidationPackageBrowser/ValidationPackageBrowser.sample.fs.ts';
import SortableListFixture from '../src/Composite/SortableList/SortableList.sample.fs.ts'
import DataAnnotator from '../src/Composite/Widgets/DataAnnotator/DataAnnotator.fs.ts'
import {QuickAccessButton} from '../src/Primitive/Buttons/Buttons.fs.ts';
import ChangelogSample from '../src/Page/Changelog/Changelog.sample.fs.ts';

function TermSearchContainer() {
  const [term, setTerm] = React.useState(undefined);
  const [term2, setTerm2] = React.useState(undefined);
  const [term3, setTerm3] = React.useState(undefined);
  const [counter, setCounter] = React.useState(0)
  return <Fragment>
    <h2 className='swt:text-3xl'>TermSearch</h2>
    <div className='swt:max-w-2xl swt:flex swt:flex-col swt:gap-4'>
      <div className='swt:flex swt:flex-col'>
        <label className='swt:text-gray-200'>
          TIB Search
        </label>
        <TermSearch
          onTermChange={(term) => setTerm2(term)}
          term={term2}
          disableDefaultSearch
          disableDefaultParentSearch
          disableDefaultAllChildrenSearch
          termSearchQueries={[
            ["tib_search", (query) => TIBApi.defaultSearch(query, 10, "DataPLANT")]
          ]}
          parentSearchQueries={[
            ["tib_search", ([parentId, query]) => TIBApi.searchChildrenOf(query, parentId, 10, "DataPLANT")]
          ]}
          allChildrenSearchQueries={[
            ["tib_search", (parentId) => TIBApi.searchAllChildrenOf(parentId, 500, "DataPLANT")]
          ]}
          onBlur={() => console.log("TermSearch blurred")}
        />
      </div>
      <div className='swt:flex swt:flex-col'>
        <label className='swt:text-gray-200'>
          Term Search
        </label>
        <TermSearch
          onTermChange={(term) => setTerm(term as Term | undefined)}
          term={term}
          parentId="MS:1000031"
          debug={true}
          onBlur={() => console.log("TermSearch blurred")}
        />
      </div>
      <div className='swt:flex swt:flex-col swt:border swt:p-2'>
        <label className='swt:text-gray-200'>
          Term Search with Provider
        </label>
        <TermSearchConfigProvider>
          <>
            <TermSearchConfigSetter />
            <TermSearch
              onTermChange={(term) => setTerm3(term as Term | undefined)}
              term={term3}
            />
          </>
        </TermSearchConfigProvider>
      </div>
    </div>
  </Fragment>
}

function TableContainer() {
  return <div className='swt:flex swt:flex-col swt:gap-4'>
    <h2 className='swt:text-3xl'>Table</h2>
    <Table />
  </div>
}

function AnnoTableContainer() {
  return <div className='swt:flex swt:flex-col swt:gap-4'>
    <h2 className='swt:text-3xl'>Annotation Table</h2>
    <AnnotationTableCtxProvider>
      <AnnotationTable />
    </AnnotationTableCtxProvider>
  </div>
}

function ContextMenuContainer() {
  return <div className='swt:flex swt:flex-col swt:gap-4'>
    <h2 className='swt:text-3xl'>Context Menu</h2>
    <ContextMenuExample />
  </div>
}

function TemplateFilterContainer() {
  return <div className='swt:flex swt:flex-col swt:gap-4'>
    <h2 className='swt:text-3xl'>Template Filter</h2>
    <TemplateFilter />
  </div>
}

function ComboBoxContainer() {
  return <div className='swt:flex swt:flex-col swt:gap-4'>
    <h2 className='swt:text-3xl'>Combo Box</h2>
    <ComboBox />
  </div>
}

// function SelectContainer() {
//   return <div className='swt:flex swt:flex-col swt:gap-4'>
//     <h2 className='swt:text-3xl'>Select</h2>
//     <Select />
//   </div>
// }

function BaseModalContainer() {
  return <div className='swt:flex swt:flex-col swt:gap-4'>
    <h2 className='swt:text-3xl'>Base Modal</h2>
    <BaseModal />
  </div>
}

function DataMapTableContainer() {
  return <div className='swt:flex swt:flex-col swt:gap-4'>
    <h2 className='swt:text-3xl'>Data Map Table</h2>
    <DataMapTable />
  </div>
}

function FileExplorerContainer() {
  return <div className='swt:flex swt:flex-col swt:gap-4 swt:max-w-md'>
    <h2 className='swt:text-5xl swt:font-bold swt:mb-4'>File Explorer</h2>
    <FileExplorerExample_Example />
  </div>
}

function WidgetControllerContainer() {
  return <div className='swt:flex swt:flex-col swt:gap-4'>
    <h2 className='swt:text-5xl swt:font-bold swt:mb-4'>Widget Controller</h2>
    <WidgetController />
  </div>
}

function LandingContainer() {
  const [draft, setDraft] = React.useState(createLandingDraft());
  const [uiState, setUiState] = React.useState(createLandingUiState());

  return <div className='swt:flex swt:flex-col swt:gap-4 swt:w-full'>
    <h2 className='swt:text-5xl swt:font-bold swt:mb-4'>Landing</h2>
    <LandingWizard
      draft={draft}
      setDraft={setDraft}
      uiState={uiState}
      setUiState={setUiState}
      onSubmit={(payload) => console.log('Landing submit payload', payload)}
    />
  </div>
}

function MarkdownTextContainer() {
  return <div className='swt:flex swt:flex-col swt:gap-4 swt:w-full'>
    <h2 className='swt:text-5xl swt:font-bold swt:mb-4'>Markdown Editor</h2>
    <TextInputWithMarkdown />
  </div>
}

function AuthButtonContainer() {
  return <div className='swt:flex swt:flex-col swt:gap-4 swt:w-full'>
    <h2 className='swt:text-5xl swt:font-bold swt:mb-4'>Authentication Button</h2>
    <AuthButton />
  </div>
}

function DataHubBrowserContainer() {
  return <div className='swt:flex swt:flex-col swt:gap-4 swt:w-full'>
    <h2 className='swt:text-5xl swt:font-bold swt:mb-4'>DataHub Browser</h2>
    <DataHubBrowser />
  </div>
}

function ARCSelectorContainer() {
  return <div className='swt:flex swt:flex-col swt:gap-4 swt:w-full'>
    <h2 className='swt:text-5xl swt:font-bold swt:mb-4'>ARC Selector</h2>
    <ARCSelectorEntry />
  </div>
}

function ArcFileEditorContainer() {
  return <div className='swt:flex swt:flex-col swt:gap-4 swt:h-screen swt:w-screen swt:overflow-hidden'>
    <ArcFileEditor />
  </div>
}

function WorkspaceContainer() {
  return (
    <div className='swt:flex swt:flex-col swt:h-screen swt:w-screen swt:overflow-hidden'>
      <WorkspaceEntry />
    </div>
  );
}

function ValidationPackageBrowserFixtureContainer() {
  return (
    <div className='swt:flex swt:flex-col swt:h-screen swt:w-screen swt:overflow-hidden'>
      <ValidationPackageBrowserFixture />
    </div>
  );
}

function SortableListContainer() {
  return (
    <div className='swt:flex swt:flex-col swt:gap-4 swt:w-full swt:p-10 swt:h-screen'>
      <div className="swt:border-2 swt:border-amber-300 swt:p-4 swt:w-md swt:overflow-auto">
        <SortableListFixture />
      </div>
    </div>
  )
}

function DataAnnotatorContainer() {
  return (
    <div className='swt:flex swt:flex-col swt:gap-4 swt:w-full swt:p-10 swt:h-screen'>
      <div className="swt:border-2 swt-border-amber-300 swt:p-4 swt:w-md swt:overflow-auto">
        <DataAnnotator />
      </div>
    </div>
  )
}

const App = () => {
    return (
        <FileExplorerContainer />
    );
};

export default App;
