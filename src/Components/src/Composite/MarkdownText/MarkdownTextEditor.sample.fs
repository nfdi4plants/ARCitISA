namespace Swate.Components.Composite.MarkdownTextEditorSample

open Fable.Core
open Feliz

open Swate.Components.Composite.MarkdownText

module private MarkdownTextEditorSampleHelpers =

    let notes =
        """# Markdown Notes

Use this editor for protocol notes and checklists.

```mermaid
flowchart TD
  A[Draft] --> B{Review}
  B -->|Approved| C[Execute]
  B -->|Changes| A
```
"""

open MarkdownTextEditorSampleHelpers

[<Erase; Mangle(false)>]
type internal MarkdownTextEditorSample =

    /// A field with notes and a Mermaid diagram, in the Live mode.
    [<ReactComponent>]
    static member Entry() =
        let value, setValue = React.useState notes

        MarkdownTextEditor.MarkdownTextEditor(value, setValue, placeholder = "Write markdown...", height = 360)
