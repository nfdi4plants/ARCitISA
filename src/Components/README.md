# Components

## Local Dev

### Tests

1. Once: `npm test`
2. Watch: `npm run test:watch`

### Playground

`npm start`

### Run Storybook

`npm run storybook`

### Build Storybook

`npm run build:storybook`

### Build Npm Package

`npm run build`

### Test Npm Package

`npm pack` can be used to locally test the npm package before publishing it. It creates a `.tgz` file in the `./src` folder. **MUST** run `npm run build` first to transpile the f# code and bundle the js code.

Can be references like this in a react test repo after moving the `.tgz` file to the test repo:

```json
"dependencies": {
    "@nfdi4plants/arcitisa-components": "file:./nfdi4plants-arcitisa-components-2.0.2.tgz",
    "react": "^19.1.0",
    "react-dom": "^19.1.0"
  },
```

### Build .NET Package

`npm run build:net`

### Release

#### .NET

1. Update version in `./src/Components.fsproj`
2. `npm run build:net` (creates tailwind style css and packs nuget package)
3. Upload nuget package from `./src/bin/Releases`

#### NPM

0. Release normally and copy `<new_version>` from console output.
1. `npm version <new_version> --no-git-tag-version`
2. `npm run build` (transpiles with fable, creates tsc types, and bundles with rollup)
3. (Due to [#701](https://github.com/nfdi4plants/ARCitISA/issues/701)) replace `@layer base` with `@layer swt-base` in `dist/arcitisa-components.css`
4. `npm publish --access public [--tag next]` (Use `--tag next` for prerelease)

##### Background info:

<details>
<summary>Requirements</summary>

1. Library MUST include ARCitISA styling via .css file.
2. Library MUST be readable code (not minified) for easier debugging.
3. Library MUST be tree-shakeable.
4. Library MUST include proper TypeScript types.
5. Library MUST preserve individual files to support dynamic imports in larger components (keyword preserveModules).

</details>

> **Question:** Why transpile to `src/dist`, instead of using the fable transpiled files next to the f# files? 
>
> **Answer:** Because otherwise the vite tsc compiler cannot find the f# code from Arcitisa.Components.Shared. By giving fable a specific output dir `src/dist`, all code, including the f# code from Arcitisa.Components.Shared, is in one place and tsc can find it.

> **Question:** Why use a bundler, instead of uploading raw files.
>
> **Answer:** some F# fable libraries use native .js files. These are difficult to copy to the correct locations using `tsc` or other "bundle" mechanisms without actually bundling the files. Rollup (used by vite) is a bundler that can handle this and also supports tree-shaking and other optimizations.
