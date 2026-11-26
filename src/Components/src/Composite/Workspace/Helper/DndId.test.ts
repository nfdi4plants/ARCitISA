import { describe, it, expect } from 'vitest';
import {
    DndId_write_14F81B59 as DndId_write,
    DndId_read_Z721C83C5 as DndId_read,
    DndId_Tab,
    DndId_TabBar,
    DndId_EdgeZone,
    DndId__edgeToSplitDirection as DndId_edgeToSplitDirection,
} from './DndId.fs.ts';
import {
    EdgeDirection_fromString_Z721C83C5 as EdgeDirection_fromString,
    EdgeDirection_toString_Z3FAB5E84 as EdgeDirection_toString,
} from '../Types.fs.ts';

describe('DndId.write + DndId.read roundtrip', () => {
    it('Tab roundtrips correctly', () => {
        const id = DndId_Tab('pane-abc', 'tab-xyz');
        const str = DndId_write(id);
        const parsed = DndId_read(str);
        expect(parsed).not.toBeUndefined();
        expect(parsed!.fields[0]).toBe('pane-abc');
        expect(parsed!.fields[1]).toBe('tab-xyz');
    });

    it('Tab roundtrips with dash-free GUID-like pane IDs', () => {
        const id = DndId_Tab('abc123def456', 'tab-1');
        const str = DndId_write(id);
        const parsed = DndId_read(str);
        expect(parsed).not.toBeUndefined();
        expect(parsed!.fields[0]).toBe('abc123def456');
        expect(parsed!.fields[1]).toBe('tab-1');
    });

    it('TabBar roundtrips correctly', () => {
        const id = DndId_TabBar('pane-abc');
        const str = DndId_write(id);
        const parsed = DndId_read(str);
        expect(parsed).not.toBeUndefined();
        expect(parsed!.fields[0]).toBe('pane-abc');
    });

    it('EdgeZone roundtrips for all 4 directions', () => {
        const directions: Array<[ReturnType<typeof DndId_EdgeZone>, string]> = [
            [DndId_EdgeZone('pane-abc', 'top' as const), 'top'],
            [DndId_EdgeZone('pane-abc', 'bottom' as const), 'bottom'],
            [DndId_EdgeZone('pane-abc', 'left' as const), 'left'],
            [DndId_EdgeZone('pane-abc', 'right' as const), 'right'],
        ];
        for (const [id, dirStr] of directions) {
            const wire = DndId_write(id);
            const parsed = DndId_read(wire);
            expect(parsed).not.toBeUndefined();
            expect(parsed!.fields[0]).toBe('pane-abc');
            expect(EdgeDirection_toString(parsed!.fields[1] as any)).toBe(dirStr);
        }
    });
});

describe('DndId.read returns None for invalid strings', () => {
    it('returns undefined for empty string', () => {
        expect(DndId_read('')).toBeUndefined();
    });

    it('returns undefined for random text', () => {
        expect(DndId_read('not-a-valid-id')).toBeUndefined();
    });

    it('returns undefined for tab:: with missing tabId', () => {
        expect(DndId_read('tab::paneOnly')).toBeUndefined();
    });

    it('returns undefined for pane::edge:: with invalid direction', () => {
        expect(DndId_read('pane::abc::edge::north')).toBeUndefined();
    });

    it('returns undefined for pane:: without edge', () => {
        expect(DndId_read('pane::abc')).toBeUndefined();
    });
});

describe('DndId.edgeToSplitDirection', () => {
    it('returns Vertical for Top', () => {
        const id = DndId_EdgeZone('p', 'top' as const);
        expect(DndId_edgeToSplitDirection(id)).toBe('vertical');
    });

    it('returns Vertical for Bottom', () => {
        const id = DndId_EdgeZone('p', 'bottom' as const);
        expect(DndId_edgeToSplitDirection(id)).toBe('vertical');
    });

    it('returns Horizontal for Left', () => {
        const id = DndId_EdgeZone('p', 'left' as const);
        expect(DndId_edgeToSplitDirection(id)).toBe('horizontal');
    });

    it('returns Horizontal for Right', () => {
        const id = DndId_EdgeZone('p', 'right' as const);
        expect(DndId_edgeToSplitDirection(id)).toBe('horizontal');
    });

    it('returns undefined for Tab', () => {
        const id = DndId_Tab('p', 't');
        expect(DndId_edgeToSplitDirection(id)).toBeUndefined();
    });

    it('returns undefined for TabBar', () => {
        const id = DndId_TabBar('p');
        expect(DndId_edgeToSplitDirection(id)).toBeUndefined();
    });
});
