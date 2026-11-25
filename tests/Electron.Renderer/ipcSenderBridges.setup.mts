// The renderer's Api module builds its IPC proxies when it loads, and each proxy needs the bridge that the preload
// puts on window. Tests run without the preload, so each sender API of src/Electron/src/Renderer/Api.fs gets a
// bridge here whose functions reject, and a test that calls one sees which call it was. A test may set its own
// bridge before it imports Api. Receiver bridges are left alone, so tests can still check a missing one.
const senderApis = [
    "IGitLabApi",
    "IVersionControlApi",
    "IArcVaultsApi",
    "IMarkdownResolverApi",
    "IAuthApi",
    "IAppVersionApi",
    "IUiSettingsApi",
    "IUserDefaultsApi",
    "ITemplateApi",
    "IValidationPackageIPC",
];

const rejectingBridge = (api: string) =>
    new Proxy(
        {},
        {
            get: (_target, field) => () =>
                Promise.reject(new Error(`${api}.${String(field)} has no preload bridge in the renderer tests.`)),
        },
    );

const target = window as unknown as Record<string, unknown>;

for (const api of senderApis) {
    const name = `FABLE_REMOTING_${api}`;

    if (!(name in target)) {
        target[name] = rejectingBridge(api);
    }
}
