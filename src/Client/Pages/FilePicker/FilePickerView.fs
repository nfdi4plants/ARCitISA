namespace Pages

open Model
open Browser.Types
open Elmish
open Messages.FilePicker
open Messages
open Feliz
open Arcitisa
open Arcitisa.Components
open Arcitisa.Components.Composite.AnnotationTable
open Arcitisa.Components.Composite.AnnotationTable.Context
open Arcitisa.Components.Primitive
open Arcitisa.Components.Primitive.Buttons

module FilePicker =

    let update
        (filePickerMsg: FilePicker.Msg)
        (state: FilePicker.Model)
        (model: Model.Model)
        : FilePicker.Model * Cmd<Messages.Msg> =
        match filePickerMsg with
        | LoadNewFiles fileNames ->
            let fileNames = fileNames |> List.mapi (fun i x -> i + 1, x)

            let nextCmd =
                Messages.FilePicker.UpdateFileNames fileNames
                |> Messages.FilePickerMsg
                |> Cmd.ofMsg

            state, nextCmd
        | UpdateFileNames newFileNames ->
            let nextState: FilePicker.Model = { FileNames = newFileNames }
            nextState, Cmd.none

type FilePicker =


    /// "parentContainerResizeClass": uses tailwind container queries. Expects a string like "@md/parentId:flex-row"
    static member private UploadButtons(model: Model, dispatch, parentContainerResizeClass: string) =

        let inputId = "filePicker_OnFilePickerMainFunc"

        Html.div [
            prop.className [
                "swt:flex swt:flex-col swt:gap-2"
                parentContainerResizeClass
            ]
            prop.children [
                Html.input [
                    prop.style [ style.display.none ]
                    prop.id inputId
                    prop.multiple true
                    prop.type'.file
                    prop.onChange (fun (ev: File list) ->
                        let files = ev //ev.target?files

                        let fileNames = files |> List.map (fun f -> f.name)

                        fileNames |> LoadNewFiles |> FilePickerMsg |> dispatch

                    //let picker = Browser.Dom.document.getElementById(inputId)
                    //// https://stackoverflow.com/questions/3528359/html-input-type-file-file-selection-event/3528376
                    //picker?value <- null
                    )
                ]
                match model.PersistentStorageState.Host with
                | Some Arcitisahost.ARCitect ->
                    Html.button [
                        prop.className "swt:btn swt:btn-primary swt:btn-block"
                        prop.text "Pick Files"
                        prop.onClick (fun _ -> Start false |> ARCitect.RequestPaths |> ARCitectMsg |> dispatch)
                    ]

                    Html.button [
                        prop.className "swt:btn swt:btn-primary swt:btn-block"
                        prop.text "Pick Directories"
                        prop.onClick (fun _ -> Start true |> ARCitect.RequestPaths |> ARCitectMsg |> dispatch)
                    ]
                | _ ->
                    Html.button [
                        prop.className "swt:btn swt:btn-primary swt:btn-block"
                        prop.text "Pick file names"
                        prop.onClick (fun _ ->
                            let getUploadElement = Browser.Dom.document.getElementById inputId
                            getUploadElement.click ()
                        )
                    ]
            ]
        ]

    [<ReactComponentAttribute>]
    static member ActionButtons (model: Model) dispatch =

        let ctx = useAnnotationTableStateCtx ()

        let selectedCells =
            ctx.state
            |> Map.tryFind model.SpreadsheetModel.ActiveTable.Name
            |> Option.bind (fun ctx -> ctx.SelectedCells)
            |> Option.map (fun x -> {|
                xStart = x.xStart - 1
                xEnd = x.xEnd - 1
                yStart = x.yStart - 1
                yEnd = x.yEnd - 1
            |})
            |> unbox<Arcitisa.Components.CellCoordinateRange option>

        Html.div [
            prop.className "swt:flex swt:flex-row swt:justify-center swt:gap-2"
            prop.children [
                Html.button [
                    prop.className
                        "swt:btn swt:btn-neutral swt:btn-outline swt:bg-neutral swt:text-white swt:hover:btn-primary"
                    prop.text "Cancel"
                    prop.onClick (fun _ -> Messages.FilePicker.UpdateFileNames [] |> FilePickerMsg |> dispatch)
                ]

                Html.button [
                    prop.className "swt:btn swt:btn-primary"
                    prop.text "Insert file names"
                    prop.onClick (fun _ ->
                        let fileNames = model.FilePickerState.FileNames |> List.map snd

                        SpreadsheetInterface.InsertFileNames(selectedCells, fileNames)
                        |> InterfaceMsg
                        |> dispatch
                    )
                ]
            ]
        ]

    static member private SortButton icon msg =
        Html.button [
            prop.className "swt:btn swt:join-item"
            prop.onClick msg
            prop.children [ icon ]
        ]

    static member FileSortElements (model: Model) dispatch =
        Html.div [
            Html.div [
                prop.className "swt:join"
                prop.children [
                    FilePicker.SortButton
                        (Icons.ArrowDownAZ())
                        (fun _ ->
                            let sortedList =
                                model.FilePickerState.FileNames
                                |> List.sortBy snd
                                |> List.mapi (fun i x -> i + 1, snd x)

                            UpdateFileNames sortedList |> FilePickerMsg |> dispatch
                        )
                    FilePicker.SortButton
                        (Icons.ArrowDownZA())
                        (fun _ ->
                            let sortedList =
                                model.FilePickerState.FileNames
                                |> List.sortByDescending snd
                                |> List.mapi (fun i x -> i + 1, snd x)

                            UpdateFileNames sortedList |> FilePickerMsg |> dispatch
                        )
                ]
            ]
        ]

    static member private DeleteFromTable (id, fileName) (model: Model) dispatch =
        Buttons.DeleteButton(
            props = [
                prop.onClick (fun _ ->
                    let newList =
                        model.FilePickerState.FileNames
                        |> List.except [ id, fileName ]
                        |> List.mapi (fun i (_, name) -> i + 1, name)

                    newList |> UpdateFileNames |> FilePickerMsg |> dispatch
                )
                prop.className "swt:btn-xs swt:btn-error wt:btn-outline"
            ]
        )

    static member private MoveUpButton (id, fileName) (model: Model) dispatch =
        Html.button [
            prop.className "swt:btn swt:btn-xs swt:join-item"
            prop.onClick (fun _ ->
                let sortedList =
                    model.FilePickerState.FileNames
                    |> List.map (fun (iterInd, iterFileName) ->
                        let isNameToMove = (id, fileName) = (iterInd, iterFileName)

                        if isNameToMove then
                            // if the iterated element is the one we want to move, substract 1.5 from it
                            // let sortBy handle all stuff then assign new indices with mapi
                            (float iterInd - 1.5, iterFileName)
                        else
                            (float iterInd, iterFileName)
                    )
                    |> List.sortBy fst
                    |> List.mapi (fun i v -> i + 1, snd v)

                UpdateFileNames sortedList |> FilePickerMsg |> dispatch
            )
            prop.children [ Icons.ArrowUp() ]
        ]

    static member private MoveDownButton (id, fileName) (model: Model) dispatch =
        Html.button [
            prop.className "swt:btn swt:btn-xs swt:join-item"
            prop.onClick (fun _ ->
                let sortedList =
                    model.FilePickerState.FileNames
                    |> List.map (fun (iterInd, iterFileName) ->
                        let isNameToMove = (id, fileName) = (iterInd, iterFileName)

                        if isNameToMove then
                            // if the iterated element is the one we want to move, add 1.5 from it
                            // let sortBy handle all stuff then assign new indices with mapi
                            (float iterInd + 1.5, iterFileName)
                        else
                            (float iterInd, iterFileName)
                    )
                    |> List.sortBy fst
                    |> List.mapi (fun i v -> i + 1, snd v)

                UpdateFileNames sortedList |> FilePickerMsg |> dispatch
            )
            prop.children [ Icons.ArrowDown() ]
        ]

    static member private MoveButtonList (id, fileName) (model: Model) dispatch =
        Html.div [
            prop.className "swt:join"
            prop.children [
                FilePicker.MoveUpButton (id, fileName) model dispatch
                FilePicker.MoveDownButton (id, fileName) model dispatch
            ]
        ]


    static member private FileViewTable (model: Model) dispatch =
        Html.table [
            prop.className "swt:table swt:table-zebra swt:table-xs"
            prop.children [
                Html.tbody [
                    for index, fileName in model.FilePickerState.FileNames do
                        Html.tr [
                            Html.td [ Html.b $"{index}" ]
                            Html.td fileName
                            Html.td [
                                FilePicker.MoveButtonList (index, fileName) model dispatch
                            ]
                            Html.td [
                                prop.style [ style.textAlign.right ]
                                prop.children [
                                    FilePicker.DeleteFromTable (index, fileName) model dispatch
                                ]
                            ]
                        ]
                ]
            ]
        ]


    static member Main(model: Model, dispatch, containerQueryClass: string) =
        let hasFiles = not (List.isEmpty model.FilePickerState.FileNames)

        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-2 swt:overflow-y-hidden"
            prop.children [
                FilePicker.UploadButtons(model, dispatch, containerQueryClass)

                if hasFiles then
                    FilePicker.FileSortElements model dispatch

                    Html.div [
                        prop.className "swt:overflow-y-auto swt:overflow-x-hidden swt:py-2"
                        prop.children [ FilePicker.FileViewTable model dispatch ]
                    ]

                    FilePicker.ActionButtons model dispatch
            ]
        ]

    static member Sidebar(model: Model, dispatch: Messages.Msg -> unit) =

        SidebarComponents.SidebarLayout.Container [
            SidebarComponents.SidebarLayout.Header "File Picker"

            SidebarComponents.SidebarLayout.Description
                "Select files from your computer and insert their names into Excel"

            // Colored container element for all uploaded file names and sort elements
            FilePicker.Main(model, dispatch, "swt:@md/sidebar:flex-row")
        ]
