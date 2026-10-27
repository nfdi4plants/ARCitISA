namespace Swate.Components.Page.Metadata

open Fable.Core
open Feliz
open ARCtrl
open Swate.Components
open Swate.Components.Primitive.LayoutComponents

[<Erase; Mangle(false)>]
type DataMapMetadata =

    [<ReactComponent(true)>]
    static member DataMapMetadata
        // 👀 If you rename these variables, ensure that the names are forwarded for lazy loading in `src\Components\src\Metadata\ArcFileMetadata.fs` as well!
        (datamap: Datamap)
        =
        Swate.Components.Primitive.LayoutComponents.LayoutComponents.Section [
            Swate.Components.Primitive.LayoutComponents.LayoutComponents.BoxedField(
                "DataMap",
                description = $"Data Contexts: {datamap.DataContexts.Count}"
            )
        ]
