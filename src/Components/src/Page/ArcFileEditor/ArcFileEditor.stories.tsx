import React from 'react';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { within, expect, userEvent, waitFor } from 'storybook/test';
import { Entry as ArcFileEditor } from './ArcFileEditor.fs.js';

const STORY_TEMPLATE_NAME = 'Story Import Template';

const parseColumnCount = (text: string | null) => {
  const match = text?.match(/\d+/);
  return match ? Number(match[0]) : 0;
};

const FullSizeArcEditor = () => {
  return (
    <div className='swt:flex swt:flex-col swt:gap-4 swt:h-screen swt:w-screen swt:overflow-hidden'>
      <ArcFileEditor debug/>
    </div>
  );
};


const meta = {
  title: 'Page Components/ArcFileEditor',
  tags: ['autodocs'],
  parameters: {
    layout: 'fullscreen',
  },
  component: FullSizeArcEditor,
} satisfies Meta<typeof FullSizeArcEditor>;

export default meta;

type Story = StoryObj<typeof meta>;

const getWidgetButton = (canvas: ReturnType<typeof within>, label: string) =>
  canvas.getByRole('button', { name: new RegExp(label, 'i') });

export const IntegratedNavbar: Story = {
  parameters: { isolated: true },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    const navbar = canvas.getByRole('navigation', { name: 'arc navigation' });
    expect(navbar).toBeInTheDocument();

    expect(getWidgetButton(canvas, 'Add Building Block')).toBeEnabled();
    expect(getWidgetButton(canvas, 'Add Template')).toBeEnabled();
    expect(getWidgetButton(canvas, 'File Picker')).toBeEnabled();
    expect(getWidgetButton(canvas, 'Data Annotator')).toBeEnabled();
    expect(getWidgetButton(canvas, 'Import JSON')).toBeEnabled();
    expect(getWidgetButton(canvas, 'Export JSON')).toBeEnabled();
  },
};

export const NavbarWidgetToggle: Story = {
  parameters: { isolated: true },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await userEvent.click(getWidgetButton(canvas, 'Add Building Block'));

    await waitFor(() => {
      expect(canvas.getByRole('button', { name: 'Add Column' })).toBeInTheDocument();
    });

    await userEvent.click(getWidgetButton(canvas, 'Add Building Block'));

    await waitFor(() => {
      expect(canvas.queryByRole('button', { name: 'Add Column' })).not.toBeInTheDocument();
    });
  },
};

export const DeleteThenAddTable: Story = {
  parameters: { isolated: true },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const portal = within(canvasElement.ownerDocument.body);

    const deletedTable = canvas.getByRole('button', { name: 'Table 2' });
    await userEvent.click(deletedTable);
    await userEvent.pointer({ keys: '[MouseRight]', target: deletedTable });
    await userEvent.click(await portal.findByText('Delete Table'));

    await waitFor(() => {
      expect(canvas.queryByRole('button', { name: 'Table 2' })).not.toBeInTheDocument();
    });

    await userEvent.click(canvas.getByRole('button', { name: 'Add new table' }));

    await waitFor(() => {
      expect(canvas.queryByRole('button', { name: 'Table 2' })).not.toBeInTheDocument();
      expect(canvas.getByRole('button', { name: 'New Table 0' })).toBeInTheDocument();
    });
  },
};

export const AddTemplateWidget: Story = {
  parameters: { isolated: true },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const portal = within(canvasElement.ownerDocument.body);

    const initialColumnCount = parseColumnCount(canvas.getByTestId('arc-file-editor-column-count').textContent);

    await userEvent.click(getWidgetButton(canvas, 'Add Template'));

    await waitFor(() => {
      expect(canvas.getByText(STORY_TEMPLATE_NAME)).toBeInTheDocument();
    });

    await userEvent.click(canvas.getByText(STORY_TEMPLATE_NAME));

    await waitFor(() => {
      expect(canvas.getByText(/1 selected/i)).toBeInTheDocument();
    });

    await userEvent.click(canvas.getByRole('button', { name: /^Import$/i }));

    const importDialog = await portal.findByRole('dialog', { name: /Import templates/i });
    await userEvent.click(within(importDialog).getByRole('button', { name: /^Import$/i }));

    await waitFor(() => {
      const nextColumnCount = parseColumnCount(canvas.getByTestId('arc-file-editor-column-count').textContent);
      expect(nextColumnCount).toBeGreaterThan(initialColumnCount);
    });
  },
};

export const AppendTemplateToEmptyTable: Story = {
  parameters: { isolated: true },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const portal = within(canvasElement.ownerDocument.body);

    await userEvent.click(canvas.getByRole('button', { name: 'Table 1' }));
    expect(canvas.getByText('Start with template!')).toBeInTheDocument();

    await userEvent.click(getWidgetButton(canvas, 'Add Template'));
    await userEvent.click(await canvas.findByText(STORY_TEMPLATE_NAME));
    await userEvent.click(canvas.getByRole('button', { name: /^Import$/i }));

    const importDialog = await portal.findByRole('dialog', { name: /Import templates/i });
    await userEvent.click(within(importDialog).getByRole('button', { name: /^Import$/i }));

    await waitFor(() => {
      expect(canvas.queryByText('Start with template!')).not.toBeInTheDocument();
      expect(canvas.getByText(/Input \[Source Name\]/i)).toBeInTheDocument();
      expect(canvas.getByText(/Output \[Sample Name\]/i)).toBeInTheDocument();
      expect(canvas.getByText(/My Awesome component/i)).toBeInTheDocument();
    });
  },
};

// Cells are addressed through the grid's data attributes because the editor does not
// forward debug test ids to the annotation table.
const cellContentAt = (root: HTMLElement, row: number, column: number) => {
  const td = root.querySelector(`[data-row="${row}"][data-column="${column}"]`) as HTMLElement | null;
  return (td?.firstElementChild as HTMLElement | null) ?? td;
};

const findCellContentAt = (root: HTMLElement, row: number, column: number) =>
  waitFor(() => {
    const cell = cellContentAt(root, row, column);
    if (!cell) throw new Error(`Cell ${row},${column} is not rendered`);
    return cell;
  });

export const TemplateImportedWithUnitsStaysEditable: Story = {
  parameters: { isolated: true },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const portal = within(canvasElement.ownerDocument.body);

    await userEvent.click(canvas.getByRole('button', { name: 'Table 1' }));
    await userEvent.click(canvas.getByText('Start with template!'));
    await userEvent.click(await portal.findByText(STORY_TEMPLATE_NAME));
    await userEvent.click(await portal.findByRole('button', { name: /^Import$/i }));

    const importDialog = await portal.findByRole('dialog', { name: /Import templates/i });
    await userEvent.click(within(importDialog).getByLabelText(/With Units/i));
    await userEvent.click(within(importDialog).getByLabelText(/Import \(new table\)/i));
    await userEvent.click(within(importDialog).getByRole('button', { name: /^Import$/i }));

    await userEvent.click(await canvas.findByRole('button', { name: STORY_TEMPLATE_NAME }));

    // Column 2 is Output, column 4 is the unitized Temperature parameter.
    const outputCell = await findCellContentAt(canvasElement, 1, 2);
    expect((await findCellContentAt(canvasElement, 1, 4)).textContent).toMatch(/degree celsius/);

    // The imported row has no stored Output cell, the file picker must still fill it.
    await userEvent.click(outputCell);
    await userEvent.click(getWidgetButton(canvas, 'File Picker'));
    await userEvent.click(await canvas.findByRole('button', { name: /^Pick Files$/i }));
    const insertButton = await canvas.findByRole('button', { name: /Insert file names/i });
    await waitFor(() => expect(insertButton).toBeEnabled());
    await userEvent.click(insertButton);

    await waitFor(() => {
      expect(cellContentAt(canvasElement, 1, 2)?.textContent).toMatch(/myImage\.png/);
    });

    // Rows added afterwards keep the template unit, so typing a value is enough.
    const addRowsButton = canvas.getByTitle('Add Rows').querySelector('button') as HTMLButtonElement;
    await userEvent.click(addRowsButton);

    const newUnitCell = await findCellContentAt(canvasElement, 2, 4);
    await userEvent.dblClick(newUnitCell);
    const activeInput = await waitFor(() => {
      const input = cellContentAt(canvasElement, 2, 4)?.querySelector('input') as HTMLInputElement | null;
      if (!input) throw new Error('Unit cell did not activate');
      return input;
    });
    await userEvent.type(activeInput, '5', { delay: 50 });
    await userEvent.keyboard('{Enter}');

    await waitFor(() => {
      expect(cellContentAt(canvasElement, 2, 4)?.textContent).toMatch(/5 degree celsius/);
    });
  },
};

export const RenameThenDeleteAndAddTable: Story = {
  parameters: { isolated: true },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const portal = within(canvasElement.ownerDocument.body);

    await userEvent.dblClick(canvas.getByRole('button', { name: 'Table 2' }));
    const nameInput = canvas.getByRole('textbox');
    await userEvent.clear(nameInput);
    await userEvent.type(nameInput, 'Renamed Table{enter}');

    const renamedTable = await canvas.findByRole('button', { name: 'Renamed Table' });
    await userEvent.pointer({ keys: '[MouseRight]', target: renamedTable });
    await userEvent.click(await portal.findByText('Delete Table'));
    await userEvent.click(canvas.getByRole('button', { name: 'Add new table' }));

    await waitFor(() => {
      expect(canvas.queryByRole('button', { name: 'Renamed Table' })).not.toBeInTheDocument();
      expect(canvas.getByRole('button', { name: 'New Table 0' })).toBeInTheDocument();
    });
  },
};

export const RejectDuplicateTableName: Story = {
  parameters: { isolated: true },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await userEvent.dblClick(canvas.getByRole('button', { name: 'Table 2' }));
    const nameInput = canvas.getByRole('textbox');
    await userEvent.clear(nameInput);
    await userEvent.type(nameInput, 'Table 1{enter}');

    expect(nameInput).toHaveValue('Table 1');
    await userEvent.keyboard('{Escape}');
    expect(canvas.getAllByRole('button', { name: 'Table 1' })).toHaveLength(1);
    expect(canvas.getByRole('button', { name: 'Table 2' })).toBeInTheDocument();
  },
};

// Tables are saved as spreadsheet sheets, which ignore case and reject some characters.
export const RejectTableNamesThatCannotBeSaved: Story = {
  parameters: { isolated: true },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    for (const [name, message] of [
      ['table 1', 'A table named "table 1" already exists.'],
      ['Sugar/day 2', 'A table name cannot contain * ? : \\ / [ ].'],
    ]) {
      await userEvent.dblClick(canvas.getByRole('button', { name: 'Table 2' }));
      const nameInput = canvas.getByRole('textbox');
      await userEvent.clear(nameInput);
      await userEvent.type(nameInput, `${name}{enter}`);

      expect(nameInput).toHaveValue(name);
      expect(canvas.getByRole('alert')).toHaveTextContent(message);
      await userEvent.keyboard('{Escape}');
      expect(canvas.getByRole('button', { name: 'Table 2' })).toBeInTheDocument();
      expect(canvas.queryByRole('button', { name })).not.toBeInTheDocument();
    }
  },
};
