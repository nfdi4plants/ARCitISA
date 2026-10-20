import type { Meta, StoryObj } from "@storybook/react-vite";
import {
  fn,
  within,
  expect,
  userEvent,
  waitFor,
  fireEvent,
  screen,
} from "storybook/test";
import Table from "./AnnotationTable.fs.js";
import { TIBApi } from "../Util/Api.fs.js";
import React, { act } from "react";
import MockTable from "./MockTable.js";

// These stories call the live term search APIs. A slow response gets more time.
const LIVE_API_TIMEOUT = 15_000;

type TableOptionalProps = Omit<
  React.ComponentProps<typeof Table>,
  "arcTable" | "setArcTable"
>;


function renderTable(args: TableOptionalProps) {
  const [table, setTable] = React.useState(() => MockTable.Copy());

  const setTableWithLog = (newTable: any) => {
    setTable(newTable);
  };

  return (
    <div className="swt:h-150">
      <Table {...args} arcTable={table} setArcTable={setTableWithLog} />
    </div>
  );
}

const meta = {
  title: "Composite Components/AnnotationTable",
  tags: ["autodocs"],
  component: Table,
} satisfies Meta<typeof Table>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
  render: renderTable,
  args: {
    height: 600,
    debug: true,
  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);

    const contextMenu = canvas.getByTestId("annotation_table");
    await expect(contextMenu).toBeVisible();
  },
};

export const ContextMenu: Story = {
  render: renderTable,
  args: {
    height: 600,
    debug: true,
  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);

    const cell = await canvas.findByTestId("cell-1-1");

    fireEvent.contextMenu(cell);

    await waitFor(() => {
      const contextMenu = screen.getByTestId("context_menu");
      expect(contextMenu).toBeVisible();
    });
  },
};

export const EditCell: Story = {
  render: renderTable,
  args: {
    height: 600,
    debug: true,
  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);

    const cell = await canvas.findByTestId("cell-2-1");

    await userEvent.dblClick(cell);

    const activeCell = await canvas.findByTestId(
      "active-cell-string-input-2-1",
    );
    expect(activeCell).toBeVisible();

    await userEvent.clear(activeCell);
    await userEvent.type(activeCell, "Edited Text", { delay: 50 });
    await userEvent.keyboard("{Enter}");

    await waitFor(async () => {
      const updatedCell = await canvas.findByText("Edited Text");
      expect(updatedCell).toBeVisible();
    });

    const cell2 = await canvas.findByTestId("cell-2-1");

    await userEvent.dblClick(cell2);

    const activeCell2 = await canvas.findByTestId(
      "active-cell-string-input-2-1",
    );
    await userEvent.clear(activeCell2);
    await userEvent.type(activeCell2, "Some totally new text", { delay: 50 });

    await userEvent.keyboard("{Escape}");

    await waitFor(async () => {
      const updatedCell = await canvas.findByText("Edited Text");
      expect(updatedCell).toBeVisible();
    });
  },
};

export const EditTermCellRaw: Story = {
  render: renderTable,
  args: {
    height: 600,
    debug: true,
  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);

    const cell = await canvas.findByTestId("cell-2-3");

    await userEvent.dblClick(cell);

    const activeCell = await canvas.findByTestId("term-search-input");
    expect(activeCell).toBeVisible();

    await userEvent.clear(activeCell);
    await userEvent.clear(activeCell);
    await userEvent.clear(activeCell);
    await userEvent.clear(activeCell);
    await userEvent.type(activeCell, "leco", { delay: 50 });

    await waitFor(
      async () => {
        const debugValue = activeCell.getAttribute("data-debugresultcount");
        expect(debugValue ? parseInt(debugValue, 10) : 0).toBeGreaterThan(0);
      },
      { timeout: LIVE_API_TIMEOUT },
    );

    await userEvent.keyboard("{Enter}");

    await waitFor(async () => {
      const updatedCell = await canvas.findByText("leco");
      expect(updatedCell).toBeVisible();
    });
  },
};

export const EditTermCellKbd: Story = {
  render: renderTable,
  args: {
    height: 600,
    debug: true,
  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);

    const cell = await canvas.findByTestId("cell-2-3");

    await userEvent.dblClick(cell);

    const activeCell = await canvas.findByTestId("term-search-input");
    expect(activeCell).toBeVisible();

    await userEvent.clear(activeCell);
    await userEvent.clear(activeCell);
    await userEvent.clear(activeCell);
    await userEvent.type(activeCell, "leco instrument", { delay: 50 });

    await waitFor(
      async () => {
        const debugValue = activeCell.getAttribute("data-debugresultcount");
        expect(debugValue ? parseInt(debugValue, 10) : 0).toBeGreaterThan(0);
      },
      { timeout: LIVE_API_TIMEOUT },
    );

    await waitFor(
      async () => {
        const termSearchResult = await screen.findByText("MS:1001800");
        expect(termSearchResult).toBeVisible();
      },
      { timeout: LIVE_API_TIMEOUT },
    );

    await userEvent.keyboard("[ArrowDown][Enter]");

    await waitFor(
      async () => {
        const updatedCell = await canvas.findByText("LECO instrument model");
        expect(updatedCell).toBeVisible();
      },
      { timeout: 5000 },
    );
  },
};

export const EditTermCellMouseclick: Story = {
  render: renderTable,
  args: {
    height: 600,
    debug: true,
  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);

    const cell = await canvas.findByTestId("cell-2-3");

    await userEvent.dblClick(cell);

    const activeCell = await canvas.findByTestId("term-search-input");
    expect(activeCell).toBeVisible();

    await userEvent.clear(activeCell);
    await userEvent.clear(activeCell);
    await userEvent.clear(activeCell);
    await userEvent.type(activeCell, "leco", { delay: 50 });

    await waitFor(async () => {
      const debugValue = activeCell.getAttribute("data-debugresultcount");
      expect(debugValue ? parseInt(debugValue, 10) : 0).toBeGreaterThan(0);
    }, { timeout: LIVE_API_TIMEOUT });

    const instrumentModelItem = await screen.findByText("MS:1001800", {}, { timeout: LIVE_API_TIMEOUT });
    expect(instrumentModelItem).toBeVisible();

    await userEvent.click(instrumentModelItem);

    // await userEvent.keyboard('[ArrowDown][ArrowDown][Enter]')

    // await waitFor(async () => {
    //   const updatedCell = await canvas.findByText('LECO instrument model');
    //   expect(updatedCell).toBeVisible();
    // });
  },
};

export const EditTermHeader: Story = {
  render: renderTable,
  args: {
    height: 600,
    debug: true,
  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);

    const inactiveCellId = "cell-0-3";

    const cell = await canvas.findByTestId(inactiveCellId);

    await userEvent.dblClick(cell);

    const activeCell = await canvas.findByTestId("term-search-input");
    expect(activeCell).toBeVisible();

    await userEvent.clear(activeCell);
    await userEvent.clear(activeCell);
    await userEvent.clear(activeCell);
    await userEvent.clear(activeCell);
    await userEvent.type(activeCell, "instrument data banana", { delay: 50 });
    await userEvent.keyboard("{Enter}");

    await waitFor(async () => {
      const updatedCell = await canvas.findByText(
        "Component [instrument data banana]",
      );
      expect(updatedCell).toBeVisible();
    });
  },
};

export const FreeTextDetails: Story = {
  render: renderTable,
  args: {
    height: 600,
    debug: true,
  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);

    const cell = await canvas.findByTestId("cell-1-1");

    await fireEvent.contextMenu(cell);

    const contextMenu = screen.getByTestId("context_menu");
    await expect(contextMenu).toBeVisible();

    const detailsButton = within(contextMenu).getByRole("button", {
      name: /Details/d,
    });
    await expect(detailsButton).toBeVisible();

    await userEvent.click(detailsButton);

    await waitFor(
      () => {
        const modal = screen.getByTestId("modal_Details_FreeText");
        expect(modal).toBeVisible();
      },
      { timeout: 5000 },
    );
  },
};

export const TermDetails: Story = {
  render: renderTable,
  args: {
    height: 600,
    debug: true,
  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);

    const cell = await canvas.findByTestId("cell-1-3");

    await fireEvent.contextMenu(cell);

    const contextMenu = screen.getByTestId("context_menu");
    await expect(contextMenu).toBeVisible();

    const detailsButton = within(contextMenu).getByRole("button", {
      name: /Details/d,
    });
    await expect(detailsButton).toBeVisible();

    await userEvent.click(detailsButton);

    await waitFor(
      () => {
        const modal = screen.getByTestId("modal_Details_Term");
        expect(modal).toBeVisible();
      },
      { timeout: 5000 },
    );
  },
};

export const UnitizedDetails: Story = {
  render: renderTable,
  args: {
    height: 600,
    debug: true,
  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);

    const cell = await canvas.findByTestId("cell-1-5");

    await fireEvent.contextMenu(cell);

    const contextMenu = screen.getByTestId("context_menu");
    await expect(contextMenu).toBeVisible();

    const detailsButton = within(contextMenu).getByRole("button", {
      name: /Details/d,
    });
    await expect(detailsButton).toBeVisible();

    await userEvent.click(detailsButton);

    await waitFor(
      () => {
        const modal = screen.getByTestId("modal_Details_Unitized");
        expect(modal).toBeVisible();
      },
      { timeout: 5000 },
    );
  },
};

export const EditColumn: Story = {
  render: renderTable,
  args: {
    height: 600,
    debug: true,
  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);

    const cell = await canvas.findByTestId("cell-1-1");

    await fireEvent.contextMenu(cell);

    const contextMenu = screen.getByTestId("context_menu");
    await expect(contextMenu).toBeVisible();

    const editButton = within(contextMenu).getByRole("button", {
      name: /Edit/i,
    });
    await expect(editButton).toBeVisible();

    await userEvent.click(editButton);

    await waitFor(() => {
      const modal = screen.getByTestId("modal_Edit");
      expect(modal).toBeVisible();
    });
  },
};

export const GenerateRows: Story = {
  render: renderTable,
  args: {
    height: 600,
    debug: true,
  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);

    const cell = await canvas.findByTestId("cell-1-1");

    await fireEvent.contextMenu(cell);

    const contextMenu = screen.getByTestId("context_menu");
    await expect(contextMenu).toBeVisible();

    const editButton = within(contextMenu).getByRole("button", {
      name: /Edit/d,
    });
    await expect(editButton).toBeVisible();

    await userEvent.click(editButton);

    await waitFor(() => {
      const editModal = screen.getByTestId("modal_Edit");
      expect(editModal).toBeVisible();
    });

    const editModal = screen.getByTestId("modal_Edit");
    await expect(editModal).toBeVisible();

    const createButton = await within(editModal).findByText(/Generate Rows/i);
    await expect(createButton).toBeVisible();

    await userEvent.click(createButton);

    await waitFor(() => {
      const createTab = screen.getByTestId("Create Column");
      expect(createTab).toBeVisible();
    });
  },
};

export const UpdateRows: Story = {
  render: renderTable,
  args: {
    height: 600,

    debug: true,
  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);

    const cell = await canvas.findByTestId("cell-1-1");

    await fireEvent.contextMenu(cell);

    const contextMenu = screen.getByTestId("context_menu");
    await expect(contextMenu).toBeVisible();

    const editButton = within(contextMenu).getByRole("button", {
      name: /Edit/d,
    });
    await expect(editButton).toBeVisible();

    await userEvent.click(editButton);

    await waitFor(() => {
      const editModal = screen.getByTestId("modal_Edit");
      expect(editModal).toBeVisible();
    });

    const editModal = screen.getByTestId("modal_Edit");
    await expect(editModal).toBeVisible();

    const createButton = await within(editModal).findByText(/Update Rows/i);
    await expect(createButton).toBeVisible();

    await userEvent.click(createButton);

    await waitFor(() => {
      const createTab = screen.getByTestId("Update Column");
      expect(createTab).toBeVisible();
    });
  },
};

export const DeleteColumn: Story = {
  render: renderTable,
  args: {
    height: 600,

    debug: true,
  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);

    const table = screen.getByTestId("annotation_table");
    await expect(table).toBeVisible();

    const oldColumnLength = table.getAttribute("data-columnCount");
    await expect(oldColumnLength).toBe("6");

    const cell = await canvas.findByTestId("cell-1-1");

    await fireEvent.contextMenu(cell);

    const contextMenu = screen.getByTestId("context_menu");
    await expect(contextMenu).toBeVisible();

    const deleteButton = within(contextMenu).getByRole("button", {
      name: /Delete Column/d,
    });
    await expect(deleteButton).toBeVisible();
    await userEvent.click(deleteButton);
    const newTable = screen.getByTestId("annotation_table");
    await expect(newTable).toBeVisible();

    const newColumnLength = newTable.getAttribute("data-columncount");
    await expect(newColumnLength).toBe("5");
  },
};

export const DeleteRow: Story = {
  render: renderTable,
  args: {
    height: 600,

    debug: true,
  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);

    const cell = await canvas.findByTestId("cell-1-1");

    await fireEvent.contextMenu(cell);

    const contextMenu = screen.getByTestId("context_menu");
    await expect(contextMenu).toBeVisible();

    const editButton = within(contextMenu).getByRole("button", {
      name: /Delete Row/d,
    });
    await expect(editButton).toBeVisible();

    await userEvent.click(editButton);

    await waitFor(() => {
      const table = screen.getByTestId("annotation_table");
      expect(table).toBeVisible();

      const rows = within(table).getAllByRole("row");
      expect(rows).toHaveLength(17);
    });
  },
};

export const ActivateTermSearchContainer: Story = {
  render: renderTable,
  args: {
    height: 600,

    debug: true,
  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);

    const cell = await canvas.findByTestId("cell-1-3");

    await userEvent.click(cell);
    await userEvent.keyboard("T");

    const input = within(canvasElement).getByTestId("term-search-input");

    await userEvent.clear(input);
    await userEvent.type(input, "Temperature", { delay: 50 });

    await waitFor(
      () => {
        const termTemperature = screen.getByText("Temperature");
        expect(termTemperature).toBeVisible();
      },
      { timeout: LIVE_API_TIMEOUT },
    );
  },
};

export const TermDetailsKeyboard: Story = {
  render: renderTable,
  args: {
    height: 600,

    debug: true,
  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);

    const cell = await canvas.findByTestId("cell-1-3");

    await userEvent.click(cell);

    await userEvent.keyboard("{Control>}{Enter}{/Control}");

    await waitFor(
      () => {
        const modal = screen.getByTestId("modal_Details_Term");
        expect(modal).toBeVisible();
      },
      { timeout: 5000 },
    );
  },
};

export const FreeTextDetailsKeyboardActivation: Story = {
  render: renderTable,
  args: {
    height: 600,

    debug: true,
  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);

    const cell = await canvas.findByTestId("cell-1-1");

    await fireEvent.contextMenu(cell);

    const contextMenu = screen.getByTestId("context_menu");
    await expect(contextMenu).toBeVisible();

    const detailsAction = within(contextMenu).getByRole("button", { name: /^Details\b/i });
    await userEvent.click(detailsAction);

    await waitFor(
      () => {
        const modal = screen.getByTestId("modal_Details_FreeText");
        expect(modal).toBeVisible();
      },
      { timeout: 10_000 },
    );
  },
};

export const EditColumnKeyboardActivation: Story = {
  render: renderTable,
  args: {
    height: 600,

    debug: true,
  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);

    const cell = await canvas.findByTestId("cell-1-1");

    await fireEvent.contextMenu(cell);

    const contextMenu = screen.getByTestId("context_menu");
    await expect(contextMenu).toBeVisible();

    const editAction = within(contextMenu).getByRole("button", { name: /^Edit\b/i });
    await userEvent.click(editAction);

    await waitFor(
      () => {
        const modal = screen.getByTestId("modal_Edit");
        expect(modal).toBeVisible();
      },
      { timeout: 10_000 },
    );
  },
};

export const NextRow: Story = {
  render: renderTable,
  args: {
    height: 600,

    debug: true,
  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);

    const cell = await canvas.findByTestId("cell-2-1");

    await userEvent.dblClick(cell);

    const activeCell = await canvas.findByTestId(
      "active-cell-string-input-2-1",
    );
    expect(activeCell).toBeVisible();

    await userEvent.clear(activeCell);
    await userEvent.type(activeCell, "Edited Text 1", { delay: 50 });
    await userEvent.keyboard("{Enter}");

    const nextCell = await waitFor(() => {
      return canvasElement.querySelector('[data-row="3"][data-column="1"]');
    });
    expect(nextCell).toBeTruthy();
    expect(
      nextCell,
      "unable to find cell at row 3 column 1",
    ).toBeInTheDocument();
    expect(nextCell, "next cell is not `data-selected`").toHaveAttribute(
      "data-selected",
      "true",
    );

    if (!nextCell) {
      throw new Error("Next cell is undefined");
    }

    await waitFor(
      () => {
        const updatedCell = canvas.getByTestId("cell-2-1");
        expect(updatedCell).toHaveTextContent("Edited Text 1");
      },
      { timeout: 5000 },
    );

    await userEvent.type(nextCell, "22", { delay: 50 }); // This is an unknown issue with userEvent. This seems to miss the first character

    await waitFor(
      async () => {
        const activeCell = await canvas.findByTestId(
          "active-cell-string-input-3-1",
        );
        expect(activeCell).toBeVisible();
        await userEvent.keyboard("{Enter}");
        const updatedCell = canvas.getByTestId("cell-3-1");
        expect(updatedCell).toHaveTextContent("Source 22");
      },
      { timeout: 5000 },
    );

    const nextNextCell = await waitFor(() => {
      return canvasElement.querySelector('[data-row="4"][data-column="1"]');
    });
    expect(nextNextCell).toBeTruthy();
    expect(
      nextNextCell,
      "unable to find cell at row 3 column 1",
    ).toBeInTheDocument();
    expect(
      nextNextCell,
      "next next cell is not `data-selected`",
    ).toHaveAttribute("data-selected", "true");
  },
};

export const JumpWithinSelectedCells: Story = {
  render: renderTable,
  args: {
    height: 600,

    debug: true,
  },
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);

    const firstCell = await canvas.findByTestId("cell-1-1");
    const lastCell = await canvas.findByTestId("cell-2-2");

    await userEvent.dblClick(firstCell);

    await fireEvent.click(lastCell, { shiftKey: true });

    const activeCell1 = await canvas.findByTestId(
      "active-cell-string-input-1-1",
    );
    expect(activeCell1).toBeVisible();
    await userEvent.keyboard("{Enter}");

    const activeCell2 = await canvas.findByTestId(
      "active-cell-string-input-2-1",
    );
    expect(activeCell2).toBeVisible();
    await userEvent.keyboard("{Enter}");

    const activeCell3 = await canvas.findByTestId(
      "active-cell-string-input-1-2",
    );
    expect(activeCell3).toBeVisible();
    await userEvent.keyboard("{Enter}");

    const activeCell4 = await canvas.findByTestId(
      "active-cell-string-input-2-2",
    );
    expect(activeCell4).toBeVisible();
    await userEvent.keyboard("{Enter}");

    const activeCell5 = await canvas.findByTestId(
      "active-cell-string-input-1-1",
    );
    expect(activeCell5).toBeVisible();
  },
};
