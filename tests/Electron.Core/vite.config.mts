import { defineConfig } from "vitest/config";
import { createRequire } from "node:module";
import { fileURLToPath, pathToFileURL } from "node:url";

// The repo root has chokidar 3.6.0. The app (src/Electron) uses chokidar 5, so tests resolve it from there.
const electronRequire = createRequire(
    pathToFileURL(fileURLToPath(new URL("../../src/Electron/package.json", import.meta.url))),
);

export default defineConfig({
    resolve: {
        alias: {
            electron: fileURLToPath(new URL("./electron.mock.mts", import.meta.url)),
            chokidar: electronRequire.resolve("chokidar"),
        },
    },
    test: {
        environment: "node",
        include: ["output/**/*.test.js"],
        testTimeout: 120000,
        hookTimeout: 120000,
    },
});
