namespace Swate.Components.Composite.MarkdownPreviewSample

open Fable.Core
open Feliz

open Swate.Components.Composite.MarkdownText
open Swate.Components.Composite.MarkdownText.Links

module private MarkdownPreviewSampleHelpers =

    let okImage =
        "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='48' height='32'%3E%3Crect width='48' height='32' fill='%2300a19b'/%3E%3C/svg%3E"

    let failureMessages =
        Map [
            "docs/missing.png", "The file docs/missing.png does not exist in the ARC."
            "docs/lfs.png", "The file docs/lfs.png is stored in Git LFS and is not downloaded."
            "docs/large.png", "The file docs/large.png is larger than the preview allows."
            "docs/scan.tif", "The browser cannot show TIFF files."
        ]

    /// Serves one image that loads and a missing address for every other path, so the failure path runs.
    let context =
        { new IMarkdownLinkContext with
            member _.baseFolder = "docs"

            member _.assetUrl path =
                if path = "docs/ok.svg" then
                    Some okImage
                else
                    Some("/__missing__/" + path)

            member _.describeAsset path =
                Promise.lift (Map.tryFind path failureMessages)

            member _.openArcPath _ = ()
            member _.openExternal _ = ()
        }

    let document =
        """# Preview sample

An image that loads: ![ok](ok.svg)

Images that fail:

![missing](missing.png) ![lfs](lfs.png) ![large](large.png) ![scan](scan.tif)

An image outside the ARC: ![outside](../../outside.png)

A raw HTML image with a width: <img src="ok.svg" width="96" alt="wide">

Links: [a file](./other.md), [the ARC root file](/README.md), [outside](../../x.md), [external](https://example.org/), [heading](#links-again)

## Links again

Text under the heading."""

open MarkdownPreviewSampleHelpers

[<Erase; Mangle(false)>]
type internal MarkdownPreviewSample =

    /// A preview inside a file of an ARC. Images load or explain why they do not, and links go to the host.
    [<ReactComponent>]
    static member WithContext() =
        Html.div [
            prop.className "swt:p-4"
            prop.children [
                MarkdownView.MarkdownView(document, linkContext = context)
            ]
        ]

    /// A preview of a host without an ARC. ARC images become notices and ARC links are plain text.
    [<ReactComponent>]
    static member WithoutContext() =
        Html.div [
            prop.className "swt:p-4"
            prop.children [ MarkdownView.MarkdownView(document) ]
        ]
