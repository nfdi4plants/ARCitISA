module Index


open Messages
open Model


///<summary> This is a basic test case used in Client unit tests </summary>
let sayHello name = $"Hello {name}"

open Feliz

[<ReactComponent>]
let View (model: Model) (dispatch: Msg -> unit) =

    let enforceLightTheme =
        match model.PersistentStorageState.Host with
        | Some Arcitisahost.ARCitect -> Some Arcitisa.Components.Composite.ThemeSelector.Types.Theme.Sunrise
        | _ -> None

    // React.strictMode [
    Arcitisa.Components.Composite.ThemeSelector.ThemeProvider.ThemeProvider(
        Arcitisa.Components.Composite.TermSearch.TermSearchConfigProvider.TIBQueryProvider(
            Arcitisa.Components.Composite.AnnotationTable.AnnotationTableContextProvider.AnnotationTableContextProvider(

                Html.div [
                    prop.id "ClientView"
                    prop.className "swt:flex swt:w-full swt:overflow-auto swt:h-screen"
                    prop.children [
                        // handle logging/error modal display
                        match model.DevState.DisplayLogList with
                        | [] -> Html.none
                        | errors when
                            errors
                            |> List.exists (
                                function
                                | LogItem.Error(_, _) -> true
                                | _ -> false
                            )
                            ->
                            let errors =
                                errors
                                |> List.choose (
                                    function
                                    | LogItem.Error(_, msg) -> Some msg
                                    | _ -> None
                                )
                                |> String.concat "\n\n"

                            let close = fun b -> UpdateDisplayLogList [] |> DevMsg |> dispatch
                            Arcitisa.Components.Primitive.BaseModal.BaseModal.ErrorModalObsolete(true, close, errors)
                        | _ -> Modals.InteropLogging.Main(model.DevState, dispatch)
                        match model.PageState.IsHome, model.PersistentStorageState.Host with
                        | false, _ -> View.MainPageView.Main(model, dispatch)
                        | _, Some Arcitisahost.Excel ->
                            Html.div [
                                prop.className "swt:flex swt:flex-col swt:w-full swt:h-full"
                                prop.children [ SidebarView.SidebarView.Main(model, dispatch) ]
                            ]
                        | _, _ ->
                            let isActive = model.PageState.ShowSideBar

                            Html.div [
                                prop.className [
                                    "swt:drawer swt:drawer-end"
                                    if isActive then
                                        "swt:drawer-open"
                                ]
                                prop.children [
                                    Html.input [
                                        prop.id "split-window-drawer"
                                        prop.className "swt:drawer-toggle"
                                        prop.type'.checkbox
                                        prop.isChecked isActive
                                    ]
                                    Html.div [
                                        prop.className "swt:drawer-content"
                                        prop.children [ SpreadsheetView.Main(model, dispatch) ]
                                    ]
                                    Html.div [
                                        prop.className "swt:drawer-side"
                                        prop.children [ SidebarView.SidebarView.Main(model, dispatch) ]
                                    ]
                                ]
                            ]
                    ]
                ]
            )
        ),
        ?enforceTheme = enforceLightTheme
    )
// ]
