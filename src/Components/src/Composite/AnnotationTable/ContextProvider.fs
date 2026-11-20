namespace Swate.Components.Composite.AnnotationTable

open Feliz
open Fable.Core
open Swate.Components
open Swate.Components.Primitive
open Swate.Components.Composite.AnnotationTable
open Swate.Components.Composite.AnnotationTable.Context

[<Erase; Mangle(false)>]
type AnnotationTableContextProvider =

    [<ReactComponent(true)>]
    static member AnnotationTableContextProvider(children: ReactElement) =

        let (data: Map<string, AnnotationTableContext>), setData =
            React.useStateWithUpdater (Map.empty)

        let setSelection =
            React.useCallback (
                (fun (key: string) (range: CellCoordinateRange option) ->
                    setData (Map.add key { SelectedCells = range })
                ),
                [||]
            )

        let clearSelection =
            React.useCallback ((fun (key: string) -> setData (Map.remove key)), [||])

        AnnotationTableStateCtx.Provider(
            {
                state = data
                setSelection = setSelection
                clearSelection = clearSelection
            },
            [ children ]
        )
