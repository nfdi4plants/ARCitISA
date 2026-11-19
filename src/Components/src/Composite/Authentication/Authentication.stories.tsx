import type { Meta, StoryObj } from '@storybook/react-vite';
import { within, expect, userEvent, waitFor, fireEvent } from 'storybook/test';
import { Entry as AuthenticationEntry } from './Authentication.fs.js';

const meta = {
  title: 'Composite Components/Authentication',
  tags: ['autodocs'],
  component: AuthenticationEntry,
} satisfies Meta<typeof AuthenticationEntry>;

export default meta;

type Story = StoryObj<typeof meta>;

const openUserMenu = async (canvas: ReturnType<typeof within>) => {
  const toggleBtn = await canvas.findByTestId('UserButtonToggle');
  await userEvent.click(toggleBtn);
  await waitFor(async () => {
    const dropdownContent = await canvas.queryByTestId('UserDropdownContent');
    expect(dropdownContent).toBeVisible()
  }, { timeout: 3000 });
};

const signInWithPat = async (canvas: ReturnType<typeof within>) => {
  await openUserMenu(canvas);

  const tokenInput = await canvas.findByTestId('PersonalAccessTokenInput');
  await userEvent.type(tokenInput, 'fake-test-token');

  const signInButton = await canvas.findByTestId('SignInButton');
  await userEvent.click(signInButton);
};

const expectSignedIn = async (canvas: ReturnType<typeof within>) => {
  await waitFor(async () => {
    const signedInInfo = await canvas.findByTestId('SignedInInfo');
    expect(signedInInfo).toHaveTextContent('Signed In: true');
  }, { timeout: 4000 });
};

const expectDeletedTokens = async (canvas: ReturnType<typeof within>, count: number) => {
  await waitFor(async () => {
    expect(await canvas.findByTestId('DeletedTokensInfo')).toHaveTextContent(`Deleted tokens: ${count}`);
  });
};

const expectAuthenticatedActions = async (canvas: ReturnType<typeof within>) => {
  await openUserMenu(canvas);

  expect(await canvas.findByTestId('RemoveAccountButton-acc-1')).toBeInTheDocument();

  const addAnotherButton = await canvas.findByTestId('AddAnotherAccountButton');
  expect(addAnotherButton).toBeInTheDocument();
};

export const Default: Story = {};

export const SignInFlow: Story = {
  name: 'Sign In flow',
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await signInWithPat(canvas);

    await expectSignedIn(canvas);
    await expectAuthenticatedActions(canvas);
  },
};

export const SignInRequiresPatFlow: Story = {
  name: 'Sign In requires PAT',
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await openUserMenu(canvas);

    const signInButton = await canvas.findByTestId('SignInButton');
    expect(signInButton).toBeDisabled();

    const tokenInput = await canvas.findByTestId('PersonalAccessTokenInput');
    await userEvent.type(tokenInput, 'fake-test-token');

    expect(signInButton).toBeEnabled();
  },
};

export const SwitchToSupportedDataHubFlow: Story = {
  name: 'Switch to supported DataHub',
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await openUserMenu(canvas);

    const optionsToggle = await canvas.findByTestId('DataHubOptionsToggle');
    await userEvent.click(optionsToggle);

    const supportedRadio = await canvas.findByTestId('DataHubRadio-datahub.rz.rptu.de');
    await userEvent.click(supportedRadio);

    await waitFor(async () => {
      const patLink = await canvas.findByTestId('GeneratePatLink');
    });

    const tokenInput = await canvas.findByTestId('PersonalAccessTokenInput');
    await userEvent.type(tokenInput, 'fake-test-token');
    const signInButton = await canvas.findByTestId('SignInButton');
    await userEvent.click(signInButton);

    await expectSignedIn(canvas);
    await expectAuthenticatedActions(canvas);
  },
};

export const SwitchToCustomDataHubFlow: Story = {
  name: 'Switch to custom DataHub',
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const selectedDataHub = 'https://gitlab.example.org/';

    await openUserMenu(canvas);

    const optionsToggle = await canvas.findByTestId('DataHubOptionsToggle');
    await userEvent.click(optionsToggle);

    const customInput = await canvas.findByTestId('CustomDataHubInput');
    fireEvent.input(customInput, { target: { value: selectedDataHub } });

    const customRadio = await canvas.findByTestId('CustomDataHubRadio');
    await userEvent.click(customRadio);

    await waitFor(async () => {
      const patLink = await canvas.findByTestId('GeneratePatLink');
    });

    const tokenInput = await canvas.findByTestId('PersonalAccessTokenInput');
    await userEvent.type(tokenInput, 'fake-test-token');
    const signInButton = await canvas.findByTestId('SignInButton');
    await userEvent.click(signInButton);

    await expectSignedIn(canvas);
    await expectAuthenticatedActions(canvas);
  },
};

export const SavedCustomDataHubFlow: Story = {
  name: 'Sign in to a saved custom DataHub',
  args: { customDataHubUrls: ['https://gitlab.example.org'] },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await openUserMenu(canvas);

    const optionsToggle = await canvas.findByTestId('DataHubOptionsToggle');
    await userEvent.click(optionsToggle);

    const savedRadio = await canvas.findByTestId('DataHubRadio-gitlab.example.org');
    await userEvent.click(savedRadio);
    expect(savedRadio).toBeChecked();

    const tokenInput = await canvas.findByTestId('PersonalAccessTokenInput');
    await userEvent.type(tokenInput, 'fake-test-token');
    const signInButton = await canvas.findByTestId('SignInButton');
    await userEvent.click(signInButton);

    await expectSignedIn(canvas);
  },
};

export const RememberCustomDataHubFlow: Story = {
  name: 'Remember a custom DataHub after sign in',
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await openUserMenu(canvas);

    const optionsToggle = await canvas.findByTestId('DataHubOptionsToggle');
    await userEvent.click(optionsToggle);
    expect(canvas.queryByTestId('DataHubRadio-gitlab.example.org')).not.toBeInTheDocument();

    const customInput = await canvas.findByTestId('CustomDataHubInput');
    fireEvent.input(customInput, { target: { value: 'https://gitlab.example.org/' } });

    const tokenInput = await canvas.findByTestId('PersonalAccessTokenInput');
    await userEvent.type(tokenInput, 'fake-test-token');
    const signInButton = await canvas.findByTestId('SignInButton');
    await userEvent.click(signInButton);

    await expectSignedIn(canvas);

    await openUserMenu(canvas);
    const addAnotherButton = await canvas.findByTestId('AddAnotherAccountButton');
    await userEvent.click(addAnotherButton);

    await userEvent.click(await canvas.findByTestId('DataHubOptionsToggle'));
    expect(await canvas.findByTestId('DataHubRadio-gitlab.example.org')).toBeInTheDocument();
  },
};

export const AddAnotherAccountFlow: Story = {
  name: 'Add another account flow',
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await signInWithPat(canvas);
    await expectSignedIn(canvas);

    await openUserMenu(canvas);
    const addAnotherButton = await canvas.findByTestId('AddAnotherAccountButton');
    await userEvent.click(addAnotherButton);

    const backButton = await canvas.findByTestId('AddAccountBackButton');
    expect(backButton).toBeInTheDocument();

    const tokenInput = await canvas.findByTestId('PersonalAccessTokenInput');
    expect(tokenInput).toBeInTheDocument();

    await userEvent.click(backButton);

    expect(await canvas.findByTestId('AccountRow-acc-1')).toBeInTheDocument();
  },
};

export const RemoveAndDeleteTokenFlow: Story = {
  name: 'Remove an account and delete its token',
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await signInWithPat(canvas);
    await expectSignedIn(canvas);

    await openUserMenu(canvas);
    await userEvent.click(await canvas.findByTestId('RemoveAccountButton-acc-2'));

    expect(await canvas.findByTestId('RemoveAccountPrompt')).toBeInTheDocument();
    expect(canvas.queryByTestId('AccountRow-acc-2')).not.toBeInTheDocument();

    await userEvent.click(await canvas.findByTestId('RemoveAccountDeleteToken'));

    await waitFor(() => {
      expect(canvas.queryByTestId('RemoveAccountPrompt')).not.toBeInTheDocument();
      expect(canvas.queryByTestId('AccountRow-acc-2')).not.toBeInTheDocument();
      expect(canvas.getByTestId('AccountRow-acc-1')).toBeInTheDocument();
    });
    await expectDeletedTokens(canvas, 1);
  },
};

export const CancelRemoveFlow: Story = {
  name: 'Cancel removing an account',
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await signInWithPat(canvas);
    await expectSignedIn(canvas);

    await openUserMenu(canvas);
    await userEvent.click(await canvas.findByTestId('RemoveAccountButton-acc-2'));
    await userEvent.click(await canvas.findByTestId('RemoveAccountCancel'));

    expect(await canvas.findByTestId('AccountRow-acc-2')).toBeInTheDocument();
    expect(canvas.queryByTestId('RemoveAccountPrompt')).not.toBeInTheDocument();
    await expectDeletedTokens(canvas, 0);
  },
};

export const MultiAccountSwitchFlow: Story = {
  name: 'Multi account switch flow',
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await signInWithPat(canvas);
    await expectSignedIn(canvas);

    await openUserMenu(canvas);

    const switchSecondAccountButton = await canvas.findByTestId('UseAccountButton-acc-2');
    await userEvent.click(switchSecondAccountButton);

    await waitFor(async () => {
      expect(canvas.queryByTestId('UseAccountButton-acc-2')).not.toBeInTheDocument();
      expect(await canvas.findByTestId('UseAccountButton-acc-1')).toBeInTheDocument();
    });
  },
};

export const MultiAccountRemoveFlow: Story = {
  name: 'Multi account remove flow',
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);

    await signInWithPat(canvas);
    await expectSignedIn(canvas);

    await openUserMenu(canvas);

    const removeSecondAccountButton = await canvas.findByTestId('RemoveAccountButton-acc-2');
    await userEvent.click(removeSecondAccountButton);
    await userEvent.click(await canvas.findByTestId('RemoveAccountKeepToken'));

    await waitFor(() => {
      expect(canvas.queryByTestId('AccountRow-acc-2')).not.toBeInTheDocument();
      expect(canvas.getByTestId('AccountRow-acc-1')).toBeInTheDocument();
    });
    await expectDeletedTokens(canvas, 0);
  },
};
