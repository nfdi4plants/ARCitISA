# Local Setup

-   [Requirements](#requirements)
-   [Setup](#setup)
-   [Start](#start)
    -   [Available commands](#available-commands)
-   [Contribute](#contribute)

## Requirements

-   [.NET SDK](https://dotnet.microsoft.com/en-us/download), >= 8.0.0
    -   verify with `dotnet --version`
-   [nodejs](https://nodejs.org/en/download), >=18
    -   verify with `node --version`
-   npm, >=9
    -   likely part of nodejs installation
    -   verify with `npm --version`
-   [docker](https://docs.docker.com/engine/install/), >= 24
    -   verify with `docker --version`
    -   this is required for database setup
-   Any F# IDE, e.g. [Visual Studio Code](https://code.visualstudio.com/) + [Ionide extension](https://marketplace.visualstudio.com/items?itemName=Ionide.Ionide-fsharp#:~:text=Ionide-VSCode%20is%20a%20VSCode,powers%20language%20features%20is%20FSAutoComplete.), [Rider](https://www.jetbrains.com/rider/), [Visual Studio](https://visualstudio.microsoft.com/)

## Setup

This needs to be done once per repository download.

1. Clone this repo
2. Run dotnet tool restore
3. Run npm install

> [!NOTE]
> After starting the database for the first time you will need to setup the db from online ressources. This can be done by Swobup on
> [http://localhost:8000/docs](http://localhost:8000/docs). You can find the credentials in the [docker-compose](.db/docker-compose.yml) file (SWOBUP_USERNAME=swobup, SWOBUP_PASSWORD=test).
>
> Run:
>
> 1. PUT `api/v2/database/init`
> 2. PUT `api/v2/ontology/build`
>
> You can verify the correct task execution on [redis](http://localhost:1111). It might happen that some ontologies fail to build, in this case you can try to build them again with `POST api/v2/ontology` and the correct link from our [ontology repository](https://github.com/nfdi4plants/nfdi4plants_ontology).

## Start

`./build.cmd run db`, to start up ARCitISA (+ Database network)

**ARCitISA** runs on [https://localhost:3000](https://localhost:3000), **Swobup** on [http://localhost:8000/docs](http://localhost:8000/docs), **Database - Neo4j** can be viewed in the browser on [http://localhost:7474/browser/](http://localhost:7474/browser/).

> [!WARNING]
> Should the neo4j database not correctly start up with `./build.cmd run db` delete the container (only container not the volume) and start it again. This can be easily done with the docker ui. Open the `swate_dev` network and click on the thrash bin icon next to the neo4j container.
>
> If you know a solution to this issue, please let us know 😞
>
> Edit: This issue does not arise if you stop the container before shutting down your computer.

### Available commands

```
Usage: ./build.cmd <command>

run [db]                            Start .net backend server, vite frontend (and database,
                                    swobup with docker if `db`)

test                                Run all tests

```

### Components playground

`src/Components` is a reusable React component library that can be published to npm and reused by `src/Client` and `src/Electron/src/Renderer`. It contains Storybook/Vitest-based component tests and usage documentation.

- `npm run storybook --workspace=src/Components`: Run the storybook instance.
- `npm run start --workspace=src/Components`: to run the playground. You can edit `src/Components/playground/App.tsx` to test out components in a live environment. This is useful for testing out new components or UI logic without needing to run the entire Client or Electron app.

### Electron app

- `npm run start --workspace=src/Electron`: to run the Electron app with hot-reloading for the renderer process. If you make changes to the main process, you will need to restart the Electron app manually.

## Contribute

If you want to contribute to ARCitISA, open an issue with the feature/bug you want to work on. This way you can ensure that your approach is in line with the project goals and you can get feedback from the maintainers.

Afterwards you can fork the repository and start working on your feature/bug. When you are done, describe your changes in [CHANGELOG.md](CHANGELOG.md) following the [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) format and open a pull request.

We are currently still working on a nice project structure. For now ask us if any questions arise in the related GitHub issue!

Check out the following files for more specific contribution guidelines:
- [docs/Structure.md](docs/Structure.md) for architecture and project structure guidelines.
- [docs/ReactComponentDesign.md](docs/ReactComponentDesign.md) for guidelines on designing reusable React components in `src/Components`.

> [!IMPORTANT]
> Use VS Code or Rider for development, as they allow autoformatting via Fantomas on file save. This ensures that the code style is consistent across the project and reduces the amount of formatting-related comments in pull requests.

## Zen

> The GitHub API consolidates the Zen of GitHub in its own codebase, in 14 aphorisms:

- Responsive is better than fast
- It’s not fully shipped until it’s fast
- Anything added dilutes everything else
- Practicality beats purity
- Approachable is better than simple
- Mind your words, they are important
- Speak like a human
- Half measures are as bad as nothing at all
- Encourage flow
- Non-blocking is better than blocking
- Favor focus over features
- Avoid administrative distraction
- Design for failure
- Keep it logically awesome
