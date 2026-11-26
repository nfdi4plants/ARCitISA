# Maintainer Documentation

## Release Workflow

### Pipeline Orchestration

Job dependencies, matrix fan-out/fan-in, and artifact flow for `release-pipeline.yml` on push to `main`.

```mermaid
---
config:
  look: neo
  layout: dagre
---
flowchart TD
    commit("push to main") --> test-fork{{"fork"}}

    subgraph test["test (os matrix)"]
        t-win["windows"]
        t-linux["linux"]
        t-mac["macOS"]
    end

    test-fork --> t-win & t-linux & t-mac
    t-win & t-linux & t-mac --> test-join{{"join"}}
    test-join --> verify

    subgraph verify["verify_release"]
        v-parse["parse CHANGELOG.md"]
        v-check{"tag exists?"}
        v-skip["skip release"]
        v-tag["create & push git tag"]
        v-draft["create GitHub draft release"]
        v-parse --> v-check
        v-check -->|yes| v-skip
        v-check -->|no| v-tag --> v-draft
    end

    v-draft --> rel-fork{{"fork"}}

    subgraph release["release (target matrix, ubuntu-latest)"]
        r-nuget["nuget"]
        r-npm["npm"]
        r-docker["docker"]
        r-story["storybook"]
    end

    subgraph binaries["release_electron_binaries (os × arch matrix)"]
        b-win["windows x64"]
        b-mac-x64["macOS x64"]
        b-mac-arm["macOS arm64"]
        b-linux["linux x64"]
    end

    rel-fork --> r-nuget & r-npm & r-docker & r-story
    rel-fork --> b-win & b-mac-x64 & b-mac-arm & b-linux

    b-win & b-mac-x64 & b-mac-arm & b-linux --> art-bin["upload-artifact: release-assets-electron-* (fails if nothing was built)"]

    r-nuget & r-npm & r-docker & r-story --> rel-join{{"join"}}
    art-bin --> bin-join{{"join"}}

    rel-join --> post
    bin-join --> post

    subgraph post["post_release (ubuntu-latest)"]
        p-dl["download all release-assets-* artifacts"]
        p-check["check that every expected artifact has its files"]
        p-upload["upload assets to draft release"]
        p-final["un-draft + mark as latest"]
        p-dl --> p-check --> p-upload --> p-final
    end

    p-final --> done("done")

    style test fill:#FFE0B2
    style verify fill:#FFF9C4
    style release fill:#C8E6C9
    style binaries fill:#B3E5FC
    style post fill:#E1BEE7
```

### Release Target Details

What each target in the `release` matrix does internally.

```mermaid
---
config:
  look: neo
  layout: dagre
---
flowchart LR
    subgraph nuget["nuget"]
        direction TB
        n1["dotnet pack"] --> n2["dotnet nuget push"]
    end
    subgraph npm["npm"]
        direction TB
        n3["npm run build"] --> n4["patch CSS layer"] --> n5["npm publish"]
    end
    subgraph docker["docker"]
        direction TB
        n6["docker build"] --> n7["docker push ghcr.io"]
        n7 --> n8[":next, :latest, :vX.Y.Z"]
    end
    subgraph electron-bin["electron-bin (per OS/arch)"]
        direction TB
        n11["npm run fable"] --> n12["node scripts/forge-make.cjs --arch"] --> n12b["check out/make for fresh installers"]
    end
    subgraph storybook["storybook"]
        direction TB
        n13["npm run build:storybook"] --> n14["publish to gh-pages"]
    end

    style nuget fill:#C8E6C9
    style npm fill:#C8E6C9
    style docker fill:#C8E6C9
    style electron-bin fill:#B3E5FC
    style storybook fill:#C8E6C9
```

If a desktop build is missing, its release_electron_binaries job fails. post_release then does not run, and the GitHub release stays a draft. post_release also checks the downloaded artifacts and stops before it uploads anything when one is incomplete. `Release.verifyReleaseAssets` in `build/Actions/Release.fs` lists the expected artifacts. Update that list when the `release_electron_binaries` matrix changes.

## Obsolete names

[ObsoleteRemoveOnNextMajor.md](ObsoleteRemoveOnNextMajor.md) lists names from the time ARCitISA was called Swate that are kept for compatibility, with what breaks when they are removed. Check it before a major release.
