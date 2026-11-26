import { describe, it, expect } from 'vitest';
import {
    update,
    Arcitisa_Components_Composite_Workspace_Types_WorkspaceModel$1__WorkspaceModel$1_Init_Static_40BE9FDD as WorkspaceModel_Init,
    Arcitisa_Components_Composite_Workspace_Types_WorkspaceModel$1__WorkspaceModel$1_AddTab_Static as WorkspaceModel_AddTab,
    Arcitisa_Components_Composite_Workspace_Types_WorkspaceModel$1__WorkspaceModel$1_RemoveTab_Static as WorkspaceModel_RemoveTab,
    Arcitisa_Components_Composite_Workspace_Types_WorkspaceModel$1__WorkspaceModel$1_RemoveOtherTabs_Static as WorkspaceModel_RemoveOtherTabs,
    Arcitisa_Components_Composite_Workspace_Types_WorkspaceModel$1__WorkspaceModel$1_RemoveAllTabs_Static_Z6E01D3F as WorkspaceModel_RemoveAllTabs,
    Arcitisa_Components_Composite_Workspace_Types_WorkspaceModel$1__WorkspaceModel$1_FocusTab_Static as WorkspaceModel_FocusTab,
    Arcitisa_Components_Composite_Workspace_Types_WorkspaceModel$1__WorkspaceModel$1_MoveTab_Static as WorkspaceModel_MoveTab,
    Arcitisa_Components_Composite_Workspace_Types_WorkspaceModel$1__WorkspaceModel$1_ReorderTabs_Static as WorkspaceModel_ReorderTabs,
    Arcitisa_Components_Composite_Workspace_Types_WorkspaceModel$1__WorkspaceModel$1_SplitPane_Static as WorkspaceModel_SplitPane,
    Arcitisa_Components_Composite_Workspace_Types_WorkspaceModel$1__WorkspaceModel$1_ClosePane_Static as WorkspaceModel_ClosePane,
    Arcitisa_Components_Composite_Workspace_Types_WorkspaceModel$1__WorkspaceModel$1_CleanupEmptyPanes_Static_Z6E01D3F as WorkspaceModel_CleanupEmptyPanes,
} from '../WorkspaceModel.fs.ts';
import {
    Tab,
    TabId,
    PaneId,
    Layout_Single,
    Layout_Split,
    Msg$1_AddTab as AddTab,
    Msg$1_RemoveTab as RemoveTab,
    Msg$1_RemoveOtherTabs as RemoveOtherTabs,
    Msg$1_RemoveAllTabs as RemoveAllTabs,
    Msg$1_FocusTab as FocusTab,
    Msg$1_MoveTab as MoveTab,
    Msg$1_ReorderTabs as ReorderTabs,
    Msg$1_SplitPaneByTabMove as SplitPaneByTabMove,
    Msg$1_ClosePane as ClosePane,
    Msg$1_SetSplitRatio as SetSplitRatio,
    type Tab as TabType,
    type WorkspaceModel$1 as WorkspaceModel,
    type EdgeDirection,
    type SplitDirection,
    type Msg$1_$union as Msg,
} from '../Types.fs.ts';

function createTab(id: string, label: string = id, payload: string = '{}'): TabType<string> {
    return new Tab<string>(id, label, payload);
}

function tabsOf(pane: any): TabType<string>[] {
    return [...pane.Tabs];
}

function getLayoutLeafCount(layout: any): number {
    function count(l: any): number {
        if (l.tag === 0) return 1;
        if (l.tag === 1) return count(l.fields[2]) + count(l.fields[3]);
        return 0;
    }
    return count(layout);
}

describe('WorkspaceModel.Init', () => {
    it('creates model with Layout.Single', () => {
        const model = WorkspaceModel_Init<string>();
        expect(model.Layout?.tag).toBe(0);
        expect(model.PanesMap.size).toBe(1);
        expect(model.FocusedPane).toBeTruthy();
    });

    it('initializes with tabs', () => {
        const tabs = [createTab('a'), createTab('b')];
        const model = WorkspaceModel_Init<string>(tabs);
        const pane = [...model.PanesMap.values()][0];
        expect(tabsOf(pane)).toHaveLength(2);
    });

    it('initializes with active tab', () => {
        const tabs = [createTab('a'), createTab('b')];
        const model = WorkspaceModel_Init<string>(tabs, 'a');
        const pane = [...model.PanesMap.values()][0];
        expect(pane.FocusedTab).toBe('a');
    });
});

describe('AddTab', () => {
    it('adds tab to focused pane', () => {
        const model = WorkspaceModel_Init<string>();
        const updated = WorkspaceModel_AddTab(createTab('new'), model);
        const pane = [...updated.PanesMap.values()][0];
        expect(tabsOf(pane)).toHaveLength(1);
        expect(pane.FocusedTab).toBe('new');
    });
});

describe('RemoveTab', () => {
    it('removes tab from pane', () => {
        const model = WorkspaceModel_Init<string>([createTab('a'), createTab('b')], 'a');
        const updated = WorkspaceModel_RemoveTab('a', model);
        const pane = [...updated.PanesMap.values()][0];
        expect(tabsOf(pane)).toHaveLength(1);
    });

    it('CleanupEmptyPanes collapses single empty pane', () => {
        const model = WorkspaceModel_Init<string>([createTab('a')]);
        const afterRemove = WorkspaceModel_RemoveTab('a', model);
        const cleaned = WorkspaceModel_CleanupEmptyPanes<string>(afterRemove);
        expect(cleaned.PanesMap.size).toBe(0);
        expect(cleaned.Layout?.tag).toBe(0); // back to Single
    });
});

describe('FocusTab', () => {
    it('updates focused tab', () => {
        const model = WorkspaceModel_Init<string>([createTab('a'), createTab('b')], 'a');
        const updated = WorkspaceModel_FocusTab('b', model);
        const pane = [...updated.PanesMap.values()][0];
        expect(pane.FocusedTab).toBe('b');
        expect(updated.FocusedPane).toBeTruthy();
    });
});

describe('MoveTab', () => {
    it('moves tab between panes', () => {
        const model = WorkspaceModel_Init<string>([createTab('a'), createTab('b')]);
        const sourcePaneId = model.FocusedPane;
        const afterSplit = WorkspaceModel_SplitPane(sourcePaneId, 'right' as EdgeDirection, model);
        const afterMove = WorkspaceModel_MoveTab('b', afterSplit.FocusedPane, afterSplit);
        expect(tabsOf(afterMove.PanesMap.get(sourcePaneId)).map(tab => tab.Id)).toEqual(['a']);
        expect(tabsOf(afterMove.PanesMap.get(afterSplit.FocusedPane)).map(tab => tab.Id)).toEqual(['b']);
    });
});

describe('ReorderTabs', () => {
    it('moves tab from index 0 to 1', () => {
        const model = WorkspaceModel_Init<string>([createTab('a'), createTab('b'), createTab('c')]);
        const updated = WorkspaceModel_ReorderTabs(model.FocusedPane, 0, 1, model);
        const pane = [...updated.PanesMap.values()][0];
        expect(tabsOf(pane).map(tab => tab.Id)).toEqual(['b', 'a', 'c']);
    });
});

describe('SplitPane', () => {
    it('creates new pane and updates layout', () => {
        const model = WorkspaceModel_Init<string>([createTab('a')]);
        const afterSplit = WorkspaceModel_SplitPane(model.FocusedPane, 'right' as EdgeDirection, model);
        expect(afterSplit.PanesMap.size).toBe(2);
        expect(afterSplit.Layout?.tag).toBe(1);
        expect(afterSplit.FocusedPane).not.toBe(model.FocusedPane);
    });
});

describe('ClosePane', () => {
    it('removes all tabs from pane', () => {
        const model = WorkspaceModel_Init<string>([createTab('a'), createTab('b')]);
        const afterSplit = WorkspaceModel_SplitPane(model.FocusedPane, 'right' as EdgeDirection, model);
        const closed = WorkspaceModel_ClosePane(afterSplit.FocusedPane, afterSplit);
        const cleaned = WorkspaceModel_CleanupEmptyPanes<string>(closed);
        expect(cleaned.PanesMap.size).toBe(1);
    });
});

describe('RemoveOtherTabs', () => {
    it('keeps only specified tab', () => {
        const model = WorkspaceModel_Init<string>([createTab('a'), createTab('b'), createTab('c')]);
        const updated = WorkspaceModel_RemoveOtherTabs('b', model);
        const pane = [...updated.PanesMap.values()][0];
        expect(tabsOf(pane).map(tab => tab.Id)).toEqual(['b']);
    });
});

describe('RemoveAllTabs', () => {
    it('resets to single empty pane', () => {
        const model = WorkspaceModel_Init<string>([createTab('a'), createTab('b')]);
        const updated = WorkspaceModel_RemoveAllTabs<string>(model);
        expect(updated.PanesMap.size).toBe(1);
        const pane = [...updated.PanesMap.values()][0];
        expect(tabsOf(pane)).toHaveLength(0);
    });
});

describe('update function', () => {
    it('AddTab via update', () => {
        const model = WorkspaceModel_Init<string>();
        const next = update(model, AddTab(createTab('x')));
        const pane = [...next.PanesMap.values()][0];
        expect(tabsOf(pane)[0].Id).toBe('x');
    });

    it('RemoveTab via update triggers cleanup', () => {
        const model = WorkspaceModel_Init<string>([createTab('a')]);
        const next = update(model, RemoveTab('a'));
        expect(next.PanesMap.size).toBe(0);
    });
});
