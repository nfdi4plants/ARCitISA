namespace Arcitisa.Components.Composite.ArcVaultActions

open Fable.Core
open Feliz
open Arcitisa.Components
open Arcitisa.Components.Primitive.Popover
open ARCtrl

[<Erase; Mangle(false)>]
type ArcVaultActions =

    [<ReactMemoComponent>]
    static member private Trigger(arcName: string, arcRootPath: string, ?disabled: bool) =
        Popover.Trigger(
            Html.div [
                prop.className "swt:flex swt:items-center swt:gap-2 swt:text-primary swt:py-1"
                prop.children [
                    Html.i [
                        prop.className "swt:iconify swt:fluent--archive-20-filled"
                    ]
                    Html.span [
                        prop.className "swt:block swt:truncate swt:tracking-wide"
                        prop.text arcName
                    ]
                ]
            ],
            className =
                "swt:min-h-0 swt:h-auto swt:justify-start swt:px-2 swt:font-semibold swt:normal-case swt:bg-base-100 swt:hover:bg-primary-content swt:rounded swt:transition-colors swt:duration-200 swt:ease-in-out swt:cursor-pointer",
            props = [
                prop.testId "arc-vault-actions-btn"
                prop.title arcRootPath
                prop.disabled (defaultArg disabled false)
            ]
        )

    [<ReactMemoComponent>]
    static member private Content
        (arcName: string, arcRootPath: string, onCopyPath: string -> unit, onOpenArcFolder: unit -> unit)
        =
        Popover.Content(
            className = "swt:w-96 swt:max-w-[calc(100vw-3rem)] swt:shadow-lg!",
            children =
                Html.div [
                    prop.className "swt:flex swt:flex-col swt:gap-3 swt:text-sm"
                    prop.children [
                        Html.div [
                            prop.className "swt:flex swt:flex-col swt:gap-1"
                            prop.children [
                                Html.h3 [
                                    prop.className "swt:text-sm swt:font-semibold"
                                    prop.text arcName
                                ]
                                Html.p [
                                    prop.testId "arc-vault-actions-path-value"
                                    prop.className "swt:break-all swt:text-xs swt:opacity-90"
                                    prop.text arcRootPath
                                ]
                            ]
                        ]
                        Html.div [
                            prop.className "swt:flex swt:flex-wrap swt:items-center swt:gap-2"
                            prop.children [
                                Html.button [
                                    prop.testId "arc-vault-actions-path-copy"
                                    prop.className "swt:btn swt:btn-sm"
                                    prop.onClick (fun _ -> onCopyPath arcRootPath)
                                    prop.children [
                                        Html.i [ prop.className "swt:iconify swt:fluent--copy-24-regular" ]
                                        Html.span [ prop.text "Copy path" ]
                                    ]
                                ]
                                Html.button [
                                    prop.testId "arc-vault-actions-path-open-folder"
                                    prop.className "swt:btn swt:btn-sm"
                                    prop.onClick (fun _ -> onOpenArcFolder ())
                                    prop.children [
                                        Html.i [
                                            prop.className "swt:iconify swt:fluent--folder-open-24-regular"
                                        ]
                                        Html.span [ prop.text "Open folder" ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
        )

    /// This component displays the ARC vault name and provides actions related to the ARC vault, such as copying the local path or opening the folder in the file explorer.
    [<ReactComponent(true)>]
    static member ArcVaultActions(arcRootPath: string, onCopyPath: string -> unit, onOpenArcFolder: unit -> unit) =
        let arcName = arcRootPath |> ArcIO.getArcRootPath


        let onCopyPathCallback = React.useCallback (onCopyPath, [| onCopyPath |])

        let onOpenArcFolderCallback =
            React.useCallback (onOpenArcFolder, [| onOpenArcFolder |])

        Popover.Popover(
            debug = "ArcVaultActions",
            placement = FloatingUI.Placement.BottomStart,
            children =
                React.Fragment [
                    ArcVaultActions.Trigger(arcName, arcRootPath)
                    ArcVaultActions.Content(arcName, arcRootPath, onCopyPathCallback, onOpenArcFolderCallback)
                ]
        )

    [<ReactComponent>]
    static member Entry(?onCopyPath, ?onOpenArcFolder) =
        let onCopyPath =
            defaultArg onCopyPath (fun path -> Browser.Dom.window.alert ($"Copying path: {path}"))

        let onOpenArcFolder =
            defaultArg onOpenArcFolder (fun () -> Browser.Dom.window.alert ("Opening ARC folder"))

        let arcPath = "C:\\Users\\User\\ArcVault"

        ArcVaultActions.ArcVaultActions(
            arcRootPath = arcPath,
            onCopyPath = onCopyPath,
            onOpenArcFolder = onOpenArcFolder
        )
