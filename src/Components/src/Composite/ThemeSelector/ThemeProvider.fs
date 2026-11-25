namespace Swate.Components.Composite.ThemeSelector

open Feliz
open Fable.Core
open Browser
open Fable.React
open Swate.Components
open Swate.Components.Primitive
open Swate.Components.Composite.ThemeSelector.Types

[<Erase; Mangle(false)>]
type ThemeProvider =

    [<ReactComponent(true)>]
    static member ThemeProvider
        (children: ReactElement, ?dataAttribute: string, ?localStorageKey: string, ?enforceTheme: Theme)
        =
        let localStorageKey = defaultArg localStorageKey "swate-theme-ctx"
        let dataAttribute = defaultArg dataAttribute "data-theme"
        let (theme, setTheme) = React.useLocalStorage (localStorageKey, Theme.Auto)

        React.useLayoutEffect (
            (fun () ->
                let root = document.documentElement

                match theme with
                | Theme.Auto -> root.removeAttribute (dataAttribute)
                | themeName -> root.setAttribute (dataAttribute, unbox themeName)
            ),
            [| box theme |]
        )

        React.useLayoutEffect (
            (fun () ->
                match enforceTheme with
                | Some enforcedTheme when enforcedTheme <> theme -> setTheme enforcedTheme
                | _ -> ()
            ),
            [| box enforceTheme |]
        )

        Context.ThemeCtx.Provider({ state = theme; setState = setTheme }, children)
