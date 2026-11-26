# ARCitISA 

![ARCitISA logo](docs/arcitisa-banner.svg)

[![Tutorial: Viola's Journey][viola-badge]][viola-tutorial]

ARCitISA is a powerful tool for research data management, providing a rich set of features for managing and annotating your data with a seamless experience.

### 🚀 Download ARCitISA (Electron App)

The easiest way to get started is the ARCitISA desktop app. Download the latest build for your operating system from the assets on our [GitHub Releases Page](https://github.com/nfdi4plants/ARCitISA/releases):

-   **Windows**: `.exe` installer
-   **macOS**: `.dmg` (available for Intel `x64` and Apple Silicon `arm64`)
-   **Linux**: `.deb`, `.rpm`, `.pkg.tar.zst` (Arch), `.AppImage`, or `.zip` for `x64`, and `.deb`, `.rpm` or `.zip` for `arm64`

    -   Debian and Ubuntu: `sudo apt install ./arcitisa_<version>_amd64.deb` (`_arm64.deb` on arm64)
    -   Fedora: `sudo dnf install ./arcitisa-<version>-1.x86_64.rpm` (`.arm64.rpm` on arm64)
    -   Arch Linux and distributions based on it: `sudo pacman -U arcitisa-<version>-1-x86_64.pkg.tar.zst`
    -   Other distributions: make `ARCitISA-<version>-x64.AppImage` executable with `chmod +x` and run it, or unpack `ARCitISA-linux-x64-<version>.zip` (`linux-arm64` on arm64) and start `arcitisa` in the unpacked folder

    If the AppImage or the zip stops with a sandbox error, start it with `--no-sandbox`. For the zip you can instead run `sudo chown root:root chrome-sandbox` and `sudo chmod 4755 chrome-sandbox` in the unpacked folder.

### 🪻 Viola's Journey

![](docs/viola-border-top.svg)

**New to ARCitISA? Start here!** Follow Viola, a PhD student studying how cold stress affects sugar metabolism in _Arabidopsis thaliana_, from creating a first ARC all the way to publishing it:

-   🌱 **Create** an ARC and describe the investigation, studies and assays
-   🤝 **Share** it via the DataHUB and track every change
-   ✅ **Validate** it, add a license and submit it for publication
-   🔬 **Go further** with data analysis, datamaps and standard procedures

**[🪻 Start Viola's Journey ➜][viola-tutorial]**

![](docs/viola-border-bottom.svg)

[viola-tutorial]: https://nfdi4plants.github.io/nfdi4plants.knowledgebase/start-here/
[viola-badge]: https://img.shields.io/badge/Tutorial-Viola%27s%20Journey-6A3FB5?style=for-the-badge&labelColor=2F855A&logo=data%3Aimage%2Fsvg%2Bxml%3Bbase64%2CPHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9Ii0xMyAtMTQgMjYgMjYiPjxlbGxpcHNlIGN4PSItNSIgY3k9Ii02IiByeD0iNiIgcnk9IjcuNSIgdHJhbnNmb3JtPSJyb3RhdGUoLTI1IC01IC02KSIgZmlsbD0iI0U0RDZGRiIvPjxlbGxpcHNlIGN4PSI1IiBjeT0iLTYiIHJ4PSI2IiByeT0iNy41IiB0cmFuc2Zvcm09InJvdGF0ZSgyNSA1IC02KSIgZmlsbD0iI0U0RDZGRiIvPjxlbGxpcHNlIGN4PSItOCIgY3k9IjIiIHJ4PSI2LjUiIHJ5PSI1IiB0cmFuc2Zvcm09InJvdGF0ZSgxOCAtOCAyKSIgZmlsbD0iI0ZGRkZGRiIvPjxlbGxpcHNlIGN4PSI4IiBjeT0iMiIgcng9IjYuNSIgcnk9IjUiIHRyYW5zZm9ybT0icm90YXRlKC0xOCA4IDIpIiBmaWxsPSIjRkZGRkZGIi8%2BPGVsbGlwc2UgY3k9IjciIHJ4PSI4IiByeT0iNiIgZmlsbD0iI0Y3RDM1QyIvPjxjaXJjbGUgY3k9IjIiIHI9IjIuNCIgZmlsbD0iI0YyQTkzQiIvPjwvc3ZnPg%3D%3D

### 📖 Storybook Preview

See all available components in action by visiting our [Storybook](https://nfdi4plants.github.io/ARCitISA/).

ARCitISA's UI is built from reusable **React Components**, published as the npm package `@nfdi4plants/arcitisa-components` and the Fable NuGet package `ARCitISA.Components` for .NET developers.

Want to build on top of ARCitISA's components? Install them directly:

-   **JavaScript**:
    Install via npm:

    ```bash
    npm install @nfdi4plants/arcitisa-components
    ```

-   **Fable F#**:
    Install via NuGet:
    ```bash
    dotnet add package ARCitISA.Components
    ```

### 📖 Documentation

You can find indepth documentation on how to use ARCitISA on our [knowledgebase](https://nfdi4plants.github.io/nfdi4plants.knowledgebase/).

### 🧭 Other ways to use ARCitISA

Besides the desktop app, ARCitISA is available as a **Web Tool** as well: [swate-alpha.nfdi4plants.org](https://swate-alpha.nfdi4plants.org)

### 🌱 Contributing + Development Setup

Any help is welcome, from a typo fix to a new feature. If you are unsure about anything, just ask. We are here to help you help us.

If you want to contribute or just check out ARCitISA on your local machine have a look at the [CONTRIBUTING.md](CONTRIBUTING.md) file.

### Cite us!

Every release is published to Zenodo with a stable DOI.

[![DOI](https://zenodo.org/badge/DOI/10.5281/zenodo.21456531.svg)](https://doi.org/10.5281/zenodo.21456531)
