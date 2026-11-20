module Swate.Components.Composite.TextEditor.SchemaValidation.Types

/// Offsets into the source text, end exclusive.
type Span = { From: int; To: int }

type NodeKind =
    | ObjectNode
    | ArrayNode
    | ScalarNode

/// What a JSON pointer points at in the source. When a segment is missing, Found is false and the
/// fields describe the deepest node that exists.
type Located = {
    Found: bool
    /// The value. For a block mapping in YAML this spans all its lines.
    Value: Span option
    /// The key that holds the value. None for array items and the root.
    Key: Span option
    Kind: NodeKind
    /// The first key of an object value.
    FirstKey: Span option
    /// The key of a direct child property of an object value.
    ChildKey: string -> Span option
}

/// Maps JSON pointer segments ("contacts", "0", "email") to source spans. One locator serves one lint pass.
type Locator = string list -> Located
