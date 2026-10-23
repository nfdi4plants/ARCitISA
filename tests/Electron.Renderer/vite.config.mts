import { defineConfig } from "vitest/config";

export default defineConfig({
    test: {
        environment: "jsdom",
        include: ["output/**/*.test.js", "output/*.test.rtl.js"],
    },
});
