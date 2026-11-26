namespace Arcitisa.Components.Page.ArcFileEditor

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Arcitisa.Components
open Arcitisa.Components.Shared
open Arcitisa.Components.Composite.Table.Types
open Arcitisa.Components.Page.ArcFileEditor.Types
open Arcitisa.Components.Util

[<Erase; Mangle(false)>]
type SearchField =

    [<ReactComponent>]
    static member SearchField(arcFile: ArcFiles, activeView: ActiveView, tableRef: IRefValue<TableHandle>) =
        let query, setQuery = React.useState ""
        let inputRef = React.useInputRef ()

        let isMac = Shortcuts.isMac ()
        let findKey = Shortcuts.label isMac "Mod-f"

        React.useListener.onKeyDown (
            (fun event ->
                let hasModifier =
                    if isMac then
                        event.metaKey && not event.ctrlKey
                    else
                        event.ctrlKey && not event.metaKey

                if
                    event.key.ToLowerInvariant() = "f"
                    && hasModifier
                    && not event.altKey
                    && not event.shiftKey
                    && (event.target :?> Browser.Types.Element).closest("[role='dialog']").IsNone
                then
                    match inputRef.current with
                    | Some input when not input.disabled && not event.defaultPrevented ->
                        event.preventDefault ()
                        event.stopPropagation ()
                        input.focus ()
                        input.select ()
                    | _ -> ()
            ),
            options = jsOptions<Browser.Types.AddEventListenerOptions> (fun options -> options.capture <- true)
        )

        let navigation, setNavigation =
            React.useState (None: (CellCoordinate[] * int) option)

        let target = Helper.tryGetAddRowsTarget (activeView, arcFile)

        let matches =
            React.useMemo (
                (fun () -> Helper.findSearchMatches target query),
                [| box arcFile; box activeView; box query |]
            )

        let currentIndex =
            navigation
            |> Option.bind (fun (previousMatches, index) ->
                if obj.ReferenceEquals(previousMatches, matches) then
                    Some index
                else
                    None
            )

        let changeQuery value =
            setQuery value
            setNavigation None

        let onKeyDown (event: Browser.Types.KeyboardEvent) =
            match event.key with
            | "Enter" ->
                event.preventDefault ()
                event.stopPropagation ()

                if matches.Length > 0 && not (isNullOrUndefined tableRef.current) then
                    let nextIndex =
                        match currentIndex with
                        | None -> if event.shiftKey then matches.Length - 1 else 0
                        | Some index -> (index + (if event.shiftKey then -1 else 1) + matches.Length) % matches.Length

                    tableRef.current.SelectHandle.selectAt (matches.[nextIndex], false)
                    setNavigation (Some(matches, nextIndex))
            | "Escape" ->
                event.preventDefault ()
                event.stopPropagation ()
                changeQuery ""
            | _ -> ()

        Html.label [
            prop.className "swt:input swt:input-sm swt:w-60 swt:shrink-0 swt:self-center"
            prop.children [
                Html.span [
                    prop.className "swt:iconify swt:fluent--search-20-regular swt:opacity-60 swt:size-4"
                    prop.ariaHidden true
                ]
                Html.input [
                    prop.ref inputRef
                    prop.type'.search
                    prop.ariaLabel "Search cells"
                    prop.placeholder "Search cells..."
                    prop.title
                        $"{findKey}: search cells. Enter: next match. Shift+Enter: previous match. Escape: clear."
                    prop.custom ("aria-keyshortcuts", if isMac then "Meta+f" else "Control+f")
                    prop.className "swt:min-w-0 swt:grow"
                    prop.disabled target.IsNone
                    prop.value query
                    prop.onChange changeQuery
                    prop.onKeyDown onKeyDown
                ]
                if System.String.IsNullOrEmpty query then
                    Html.kbd [
                        prop.className "swt:kbd swt:kbd-xs swt:whitespace-nowrap"
                        prop.ariaHidden true
                        prop.text findKey
                    ]
                Html.span [
                    prop.role.status
                    prop.ariaLive.polite
                    prop.ariaLabel "Search results"
                    prop.className "swt:whitespace-nowrap swt:text-xs swt:tabular-nums swt:opacity-70"
                    prop.text (
                        if System.String.IsNullOrWhiteSpace query || target.IsNone then
                            ""
                        else
                            $"{currentIndex |> Option.map ((+) 1) |> Option.defaultValue 0} / {matches.Length}"
                    )
                ]
            ]
        ]
