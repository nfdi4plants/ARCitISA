# ARCitISA 

![ARCitISA logo](docs/arcitisa-banner.svg)

ARCitISA is a powerful tool for research data management, providing a rich set of features for managing and annotating your data with a seamless experience.

### 🚀 Download ARCitISA (Electron App)

The easiest way to get started is the ARCitISA desktop app. Download the latest build for your operating system from the assets on our [GitHub Releases Page](https://github.com/nfdi4plants/ARCitISA/releases):

-   **Windows**: `.exe` installer
-   **macOS**: `.dmg` (available for Intel `x64` and Apple Silicon `arm64`)
-   **Linux**: `.deb`, `.rpm`, or `.zip`

    -   Debian and Ubuntu: `sudo apt install ./swate_<version>_amd64.deb`
    -   Fedora: `sudo dnf install ./swate-<version>-1.x86_64.rpm`
    -   Other distributions: unpack `ARCitISA-linux-x64-<version>.zip` and start `swate` in the unpacked folder

    If the zip stops with a sandbox error, start it with `--no-sandbox`, or run `sudo chown root:root chrome-sandbox` and `sudo chmod 4755 chrome-sandbox` in the unpacked folder.

### 📖 Storybook Preview

See all available components in action by visiting our [Storybook](https://nfdi4plants.github.io/ARCitISA/).

ARCitISA's UI is built from reusable **React Components**, published as the npm package `@nfdi4plants/swate-components` and the Fable NuGet package `Swate.Components` for .NET developers.

Want to build on top of ARCitISA's components? Install them directly:

-   **JavaScript**:
    Install via npm:

    ```bash
    npm install @nfdi4plants/swate-components
    ```

-   **Fable F#**:
    Install via NuGet:
    ```bash
    dotnet add package Swate.Components
    ```

### 📖 Documentation

You can find indepth documentation on how to use ARCitISA on our [knowledgebase](https://nfdi4plants.github.io/nfdi4plants.knowledgebase/).

### 🧭 Other ways to use ARCitISA

Besides the desktop app, ARCitISA is available as a **Web Tool** as well: [swate-alpha.nfdi4plants.org](https://swate-alpha.nfdi4plants.org)

You can also follow along our tutorial [Violas Journey](https://nfdi4plants.github.io/nfdi4plants.knowledgebase/start-here/) 🪻

> [!NOTE]
> The tutorial is based on ARCitect and still needs to be updated for the new ARCitISA desktop app.

### 🌱 Contributing + Development Setup

Any help is welcome, from a typo fix to a new feature. If you are unsure about anything, just ask. We are here to help you help us.

If you want to contribute or just check out ARCitISA on your local machine have a look at the [CONTRIBUTING.md](CONTRIBUTING.md) file.

### Cite us!

Every release is published to Zenodo with a stable DOI.

[![DOI](https://zenodo.org/badge/DOI/10.5281/zenodo.21456531.svg)](https://doi.org/10.5281/zenodo.21456531)
