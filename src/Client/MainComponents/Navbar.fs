module MainComponents.Navbar

open Feliz

open LocalHistory
open Messages
open Components
open MainComponents
open Model
open Arcitisa.Components
open Arcitisa.Components.Primitive
open Arcitisa.Components.Primitive.Buttons
open ARCtrl
open Arcitisa.Components.Shared

open LocalStorage.AutosaveConfig

let private FileName (model: Model) =
    let txt =
        match model.SpreadsheetModel.ArcFile with
        | Some(ArcFiles.Assay a) -> a.Identifier
        | Some(ArcFiles.Study(s, _)) -> s.Identifier
        | Some(ArcFiles.Investigation i) -> i.Identifier
        | Some(ArcFiles.Template t) -> t.FileName
        | _ -> ""

    let letter =
        match model.SpreadsheetModel.ArcFile with
        | Some(ArcFiles.Assay a) -> "A"
        | Some(ArcFiles.Study(s, _)) -> "S"
        | Some(ArcFiles.Investigation i) -> "I"
        | Some(ArcFiles.Template t) -> "T"
        | _ -> ""

    match model.SpreadsheetModel.ArcFile with
    | Some _ ->
        Html.div [
            prop.className
                "swt:text-lg swt:font-bold swt:inline-flex swt:items-center swt:max-w-31.25 swt:px-2 swt:truncate"
            prop.children [
                Html.span [ prop.className "swt:hidden swt:lg:block"; prop.text txt ]
                Html.span [
                    prop.className "swt:block swt:lg:hidden"
                    prop.text letter // Truncate for smaller screens
                ]
            ]
            prop.title txt
        ]
    | None -> Html.none

let private QuickAccessButtonListStart (state: LocalHistory.Model) dispatch =
    React.Fragment [
        Buttons.QuickAccessButton(
            Icons.Backward(),
            "Backward",
            (fun _ ->
                let newPosition = state.HistoryCurrentPosition + 1
                //let newPosition_clamped = System.Math.Min(newPosition, state.HistoryExistingItemCount)
                //let noChange = newPosition_clamped = Spreadsheet.LocalStorage.CurrentHistoryPosition
                //let overMax = newPosition_clamped = Spreadsheet.LocalStorage.MaxHistory
                //let notEnoughHistory = Spreadsheet.LocalStorage.AvailableHistoryItems - (Spreadsheet.LocalStorage.CurrentHistoryPosition + 1) <= 0
                if state.NextPositionIsValid(newPosition) then
                    History.UpdateHistoryPosition newPosition |> Msg.HistoryMsg |> dispatch
            ),
            isDisabled = (state.NextPositionIsValid(state.HistoryCurrentPosition + 1) |> not)
        )
        Buttons.QuickAccessButton(
            Icons.Forward(),
            "Forward",
            (fun _ ->
                let newPosition = state.HistoryCurrentPosition - 1

                if state.NextPositionIsValid(newPosition) then
                    History.UpdateHistoryPosition newPosition |> Msg.HistoryMsg |> dispatch
            ),
            isDisabled = (state.NextPositionIsValid(state.HistoryCurrentPosition - 1) |> not)
        )
    ]

let private QuickAccessButtonListEnd (model: Model) dispatch =
    let autoSaveConfig = getAutosaveConfiguration ()
    let openReset, setOpenReset = React.useState false

    React.Fragment [
        Modals.ResetTable.Main(isOpen = openReset, setIsOpen = setOpenReset, dispatch = dispatch)
        match model.PersistentStorageState.Host with
        | Some Arcitisahost.Browser ->
            Buttons.QuickAccessButton(
                Icons.Save(),
                "Save",
                (fun _ ->
                    match model.PersistentStorageState.Host with
                    | Some(Arcitisahost.Browser) ->
                        Spreadsheet.ExportXlsx model.SpreadsheetModel.ArcFile.Value
                        |> SpreadsheetMsg
                        |> dispatch
                    | Some(Arcitisahost.ARCitect) ->
                        ARCitect.Save model.SpreadsheetModel.ArcFile.Value |> ARCitectMsg |> dispatch
                    | _ -> ()
                ),
                isDisabled = model.SpreadsheetModel.ArcFile.IsNone
            )

            Buttons.QuickAccessButton(
                Icons.Delete(),
                "Reset",
                (fun _ -> setOpenReset (not openReset)),
                color = DaisyuiColors.Error
            )

            NavbarBurger.Main(model, dispatch)
        | Some Arcitisahost.ARCitect ->
            if autoSaveConfig.IsSome && not autoSaveConfig.Value then
                Buttons.QuickAccessButton(
                    Icons.Save(),
                    "Save",
                    (fun _ ->
                        match model.PersistentStorageState.Host with
                        | Some(Arcitisahost.Browser) ->
                            Spreadsheet.ExportXlsx model.SpreadsheetModel.ArcFile.Value
                            |> SpreadsheetMsg
                            |> dispatch
                        | Some(Arcitisahost.ARCitect) ->
                            ARCitect.Save model.SpreadsheetModel.ArcFile.Value |> ARCitectMsg |> dispatch
                        | _ -> ()
                    ),
                    isDisabled = model.SpreadsheetModel.ArcFile.IsNone
                )

            NavbarBurger.Main(model, dispatch, host = Arcitisahost.ARCitect)
        | _ -> Html.none
    ]

let private WidgetNavbarList (model, addWidget: MainComponents.Widget -> unit) =
    let addBuildingBlock =
        Buttons.QuickAccessButton(
            Icons.BuildingBlock(),
            "Add Building Block",
            (fun _ -> addWidget Widget._BuildingBlock)
        )

    let addTemplate =
        Buttons.QuickAccessButton(Icons.Templates(), "Add Template", (fun _ -> addWidget Widget._Template))

    let filePicker =
        Buttons.QuickAccessButton(Icons.FilePicker(), "File Picker", (fun _ -> addWidget Widget._FilePicker))

    let dataAnnotator =
        Buttons.QuickAccessButton(
            Icons.DataAnnotator(),
            "Data Annotator",
            (fun _ -> addWidget Widget._DataAnnotator),
            classes = "swt:w-min"
        )

    React.Fragment [
        match model.SpreadsheetModel.ActiveView with
        | Spreadsheet.ActiveView.Table _ ->
            addBuildingBlock
            addTemplate
            filePicker
            dataAnnotator
        | Spreadsheet.ActiveView.DataMap -> dataAnnotator
        | Spreadsheet.ActiveView.Metadata -> Html.none
    ]

[<ReactComponent>]
let Main (model: Model, dispatch, widgets, setWidgets) =
    let addWidget (widget: MainComponents.Widget) =
        let add (widget) widgets =
            widget :: widgets |> List.rev |> setWidgets

        if widgets |> List.contains widget then
            List.filter (fun w -> w <> widget) widgets
            |> fun filteredWidgets -> add widget filteredWidgets
        else
            add widget widgets

    Components.BaseNavbar.Main [
        Html.div [ prop.className "swt:grow-0" ]
        Html.div [
            prop.className "swt:overflow-x-auto swt:min-w-0 swt:shrink swt:grow"
            prop.children [
                QuickAccessButtonListStart model.History dispatch
                WidgetNavbarList(model, addWidget)
            ]
        ]
        // match model.PersistentStorageState.Host with
        // | Some (Arcitisahost.ARCitect) ->
        //     Html.none
        // | Some _ ->
        //     Html.div [
        //         prop.className "ml-auto"
        //         prop.children [
        //             QuickAccessButtonListEnd model dispatch
        //         ]
        //     ]
        // | _ -> Html.none
        Html.div [
            prop.className "swt:ml-auto swt:grow-0 swt:shrink-0"
            prop.children [ QuickAccessButtonListEnd model dispatch ]
        ]
    ]
