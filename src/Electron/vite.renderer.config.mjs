import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

// https://vitejs.dev/config
export default defineConfig({
    plugins: [
        react({
            include: /\.(js|jsx|ts|tsx)$/,
            // Modules that run in a Web Worker stay out of Fast Refresh, because its runtime needs window. Fable
            // emits them as .jsx files, so plugin-react would wrap them. The pattern lists the module graph of
            // ElkWorker.fs, the only worker file of the renderer. Extend it when a worker imports a new module
            // that defines a capitalized value.
            exclude: /[\\/](ElkWorker|WebWorker|Elk)\.fs\.jsx$/,
        }),
        tailwindcss()
    ],
    optimizeDeps: {
        include: ['elkjs/lib/elk.bundled.js'],
    },
    server: {
        watch: {
            // Ignore raw F# source and non-renderer generated outputs to avoid unnecessary full reloads.
            ignored: (watchPath) => {
                const p = watchPath.replace(/\\/g, '/');

                const isFSharpSource =
                    p.endsWith('.fs') ||
                    p.endsWith('.fsx') ||
                    p.endsWith('.fsi') ||
                    p.endsWith('.fsproj');

                const isMainOrPreloadOutput =
                    p.includes('/src/fable_output/Main/') ||
                    p.includes('/src/fable_output/Preload/');

                return isFSharpSource || isMainOrPreloadOutput;
            },
            awaitWriteFinish: {
                stabilityThreshold: 150,
                pollInterval: 25,
            },
        },
    }
});