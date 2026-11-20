import { defineConfig } from "vitest/config";

export default defineConfig({
    test: {
        environment: "jsdom",
        // dotnet fable --run starts vitest with NODE_ENV=production. React Testing Library needs the development build of React for act.
        env: { NODE_ENV: "test" },
        include: ["output/**/*.test.js", "output/*.test.rtl.js"],
    },
});
