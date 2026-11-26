namespace Arcitisa.Components.Primitive.Popover

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Arcitisa.Components
open Arcitisa.Components.Primitive
open Arcitisa.Components.Primitive.Popover.Context

module private PopoverHelper =

    let tryContext () =
        match usePopoverCtx () with
        | Some ctx when not (isNullOrUndefined ctx) -> Some ctx
        | _ -> None

    [<ReactComponent>]
    let MissingContextError (componentName: string) =
        Html.div [
            prop.className "swt:text-error swt:text-xs swt:p-1 swt:border swt:border-error swt:rounded"
            prop.text $"⚠ Popover.{componentName} must be used inside Popover.Popover."
        ]

    let dataState isOpen = if isOpen then "open" else "closed"

    let resolveProps (props: obj option) = props |> Option.defaultValue null

    let defaultMiddleware () = [|
        FloatingUI.Middleware.offset 10
        FloatingUI.Middleware.flip ()
        FloatingUI.Middleware.shift {| padding = 8 |}
    |]

[<Erase; Mangle(false)>]
type Popover =

    [<ReactComponent(true)>]
    static member Popover
        (
            children: ReactElement,
            ?isOpen: bool,
            ?onOpenChange: bool -> unit,
            ?defaultOpen: bool,
            ?placement: FloatingUI.Placement,
            ?middleware: FloatingUI.IMiddleware[],
            ?modal: bool,
            ?debug: string,
            ?clickProps: FloatingUI.UseClickProps,
            ?dismissProps: FloatingUI.UseDismissProps,
            ?portalId: string,
            ?preserveTabOrder: bool,
            ?initialFocus: obj,
            ?returnFocus: obj,
            ?visuallyHiddenDismiss: obj,
            ?closeOnFocusOut: bool,
            ?outsideElementsInert: bool,
            ?focusManagerDisabled: bool
        ) =
        let controlledOpen = isOpen

        let uncontrolledOpen, setUncontrolledOpen =
            React.useState (defaultArg defaultOpen false)

        let isOpen = defaultArg controlledOpen uncontrolledOpen

        let setIsOpen next =
            onOpenChange |> Option.iter (fun fn -> fn next)

            if controlledOpen.IsNone then
                setUncontrolledOpen next

        let floating =
            FloatingUI.useFloating (
                ``open`` = isOpen,
                onOpenChange = setIsOpen,
                placement = defaultArg placement FloatingUI.Placement.Bottom,
                middleware = defaultArg middleware (PopoverHelper.defaultMiddleware ()),
                whileElementsMounted = FloatingUI.autoUpdate
            )

        let click =
            match clickProps with
            | Some props -> FloatingUI.useClick (floating.context, props)
            | None -> FloatingUI.useClick floating.context

        let dismiss =
            match dismissProps with
            | Some props -> FloatingUI.useDismiss (floating.context, props)
            | None -> FloatingUI.useDismiss floating.context

        let role =
            FloatingUI.useRole (floating.context, FloatingUI.UseRoleProps(role = FloatingUI.RoleAttribute.Dialog))

        let interactions = FloatingUI.useInteractions [| click; dismiss; role |]

        let transitionStatus = FloatingUI.useTransitionStatus floating.context

        let labelId, setLabelId = React.useStateWithUpdater<string option> None

        let descriptionId, setDescriptionId = React.useStateWithUpdater<string option> None

        let providerValue: PopoverContext = {
            isOpen = isOpen
            setIsOpen = setIsOpen
            floating = floating
            interactions = interactions
            isMounted = transitionStatus.isMounted
            status = transitionStatus.status
            modal = defaultArg modal false
            labelId = labelId
            setLabelId = setLabelId
            descriptionId = descriptionId
            setDescriptionId = setDescriptionId
            debug = debug
            portalId = portalId
            preserveTabOrder = defaultArg preserveTabOrder true
            initialFocus = initialFocus
            returnFocus = returnFocus
            visuallyHiddenDismiss = visuallyHiddenDismiss
            closeOnFocusOut = closeOnFocusOut
            outsideElementsInert = outsideElementsInert
            focusManagerDisabled = focusManagerDisabled
        }

        PopoverCtx.Provider(Some providerValue, children)

    [<ReactComponent>]
    static member Trigger
        (children: ReactElement, ?className: string, ?interactionProps: obj, ?props: IReactProperty list, ?debug: string) =
        match PopoverHelper.tryContext () with
        | None -> PopoverHelper.MissingContextError "Trigger"
        | Some ctx ->
            let resolvedDebug = debug |> Option.orElse ctx.debug

            Html.div [
                prop.role.button
                prop.tabIndex 0
                prop.ref ctx.floating.refs.setReference
                prop.custom ("data-state", PopoverHelper.dataState ctx.isOpen)
                if resolvedDebug.IsSome then
                    prop.testId ("popover_trigger_" + resolvedDebug.Value)
                prop.className [ yield! Option.toList className ]
                yield! Option.defaultValue [] props
                yield!
                    prop.spread
                    <| ctx.interactions.getReferenceProps (PopoverHelper.resolveProps interactionProps)
                prop.children children
            ]

    [<ReactComponent>]
    static member TriggerRender
        (
            render:
                {|
                    isOpen: bool
                    setReference: obj
                    referenceProps: obj
                |}
                    -> ReactElement,
            ?interactionProps: obj
        ) =
        match PopoverHelper.tryContext () with
        | None -> PopoverHelper.MissingContextError "TriggerRender"
        | Some ctx ->
            render {|
                isOpen = ctx.isOpen
                setReference = box ctx.floating.refs.setReference
                referenceProps = ctx.interactions.getReferenceProps (PopoverHelper.resolveProps interactionProps)
            |}

    [<ReactComponent>]
    static member Content
        (
            children: ReactElement,
            ?className: string,
            ?interactionProps: obj,
            ?props: IReactProperty list,
            ?debug: string,
            ?ariaLabel: string
        ) =
        match PopoverHelper.tryContext () with
        | None -> PopoverHelper.MissingContextError "Content"
        | Some ctx ->
            let resolvedDebug = debug |> Option.orElse ctx.debug

            if ctx.isOpen then
                FloatingUI.FloatingPortal(
                    ?id = ctx.portalId,
                    preserveTabOrder = ctx.preserveTabOrder,
                    children =
                        FloatingUI.FloatingFocusManager(
                            context = ctx.floating.context,
                            modal = ctx.modal,
                            ?disabled = ctx.focusManagerDisabled,
                            ?initialFocus = ctx.initialFocus,
                            ?returnFocus = ctx.returnFocus,
                            ?visuallyHiddenDismiss = ctx.visuallyHiddenDismiss,
                            ?closeOnFocusOut = ctx.closeOnFocusOut,
                            ?outsideElementsInert = ctx.outsideElementsInert,
                            children =
                                Html.div [
                                    prop.ref ctx.floating.refs.setFloating
                                    prop.custom ("style", ctx.floating.floatingStyles)
                                    prop.custom ("data-state", PopoverHelper.dataState ctx.isOpen)
                                    prop.custom ("data-status", ctx.status)
                                    match ctx.labelId with
                                    | Some lid -> prop.ariaLabelledBy lid
                                    | None ->
                                        if ariaLabel.IsSome then
                                            prop.ariaLabel ariaLabel.Value
                                    if ctx.descriptionId.IsSome then
                                        prop.ariaDescribedBy ctx.descriptionId.Value
                                    if resolvedDebug.IsSome then
                                        prop.testId ("popover_content_" + resolvedDebug.Value)
                                    prop.className [
                                        "swt:z-[9999] swt:min-w-56 swt:max-w-[min(28rem,calc(100vw-2rem))]"
                                        "swt:rounded-md swt:border swt:border-base-content swt:bg-base-100"
                                        "swt:p-4 swt:shadow-md swt:outline-hidden"
                                        yield! Option.toList className
                                    ]
                                    yield! Option.defaultValue [] props
                                    yield!
                                        prop.spread
                                        <| ctx.interactions.getFloatingProps (
                                            PopoverHelper.resolveProps interactionProps
                                        )
                                    prop.children children
                                ]
                        )
                )
            else
                Html.none

    [<ReactComponent>]
    static member Heading(children: ReactElement, ?className: string, ?props: IReactProperty list, ?id: string) =
        let generatedId = FloatingUI.useId ()
        let headingId = defaultArg id generatedId
        let ctxOpt = PopoverHelper.tryContext ()

        React.useEffect (
            (fun () ->
                match ctxOpt with
                | Some ctx ->
                    ctx.setLabelId (fun cur ->
                        match cur with
                        | None -> Some headingId
                        | s -> s
                    )

                    FsReact.createDisposable (fun () ->
                        ctx.setLabelId (fun cur ->
                            match cur with
                            | Some x when x = headingId -> None
                            | s -> s
                        )
                    )
                | None -> FsReact.createDisposable (fun () -> ())
            ),
            [|
                headingId :> obj
                (ctxOpt |> Option.map (fun c -> c.setLabelId :> obj) |> Option.defaultValue null)
            |]
        )

        match ctxOpt with
        | None -> PopoverHelper.MissingContextError "Heading"
        | Some _ ->
            Html.h2 [
                prop.id headingId
                prop.className [
                    "swt:text-base swt:font-semibold swt:leading-tight"
                    yield! Option.toList className
                ]
                yield! Option.defaultValue [] props
                prop.children children
            ]

    [<ReactComponent>]
    static member Description(children: ReactElement, ?className: string, ?props: IReactProperty list, ?id: string) =
        let generatedId = FloatingUI.useId ()
        let descriptionId = defaultArg id generatedId
        let ctxOpt = PopoverHelper.tryContext ()

        React.useEffect (
            (fun () ->
                match ctxOpt with
                | Some ctx ->
                    ctx.setDescriptionId (fun cur ->
                        match cur with
                        | None -> Some descriptionId
                        | s -> s
                    )

                    FsReact.createDisposable (fun () ->
                        ctx.setDescriptionId (fun cur ->
                            match cur with
                            | Some x when x = descriptionId -> None
                            | s -> s
                        )
                    )
                | None -> FsReact.createDisposable (fun () -> ())
            ),
            [|
                descriptionId :> obj
                (ctxOpt
                 |> Option.map (fun c -> c.setDescriptionId :> obj)
                 |> Option.defaultValue null)
            |]
        )

        match ctxOpt with
        | None -> PopoverHelper.MissingContextError "Description"
        | Some _ ->
            Html.p [
                prop.id descriptionId
                prop.className [
                    "swt:text-sm swt:opacity-70"
                    yield! Option.toList className
                ]
                yield! Option.defaultValue [] props
                prop.children children
            ]

    [<ReactComponent>]
    static member Close(?children: ReactElement, ?className: string, ?props: IReactProperty list) =
        match PopoverHelper.tryContext () with
        | None -> PopoverHelper.MissingContextError "Close"
        | Some ctx ->
            match children with
            | Some c ->
                Html.button [
                    prop.type'.button
                    prop.className [ "swt:btn swt:btn-sm"; yield! Option.toList className ]
                    prop.onClick (fun _ -> ctx.setIsOpen false)
                    yield! Option.defaultValue [] props
                    prop.children c
                ]
            | None ->
                Primitive.Buttons.Buttons.CircularExitButton(
                    className = (defaultArg className "swt:btn-sm"),
                    props = [
                        prop.type'.button
                        prop.onClick (fun _ -> ctx.setIsOpen false)
                        yield! Option.defaultValue [] props
                    ]
                )

    [<ReactComponent>]
    static member Simple
        (
            trigger: ReactElement,
            content: ReactElement,
            ?placement: FloatingUI.Placement,
            ?modal: bool,
            ?debug: string,
            ?contentClassName: string,
            ?triggerClassName: string
        ) =
        Popover.Popover(
            ?placement = placement,
            ?modal = modal,
            ?debug = debug,
            children =
                React.Fragment [
                    Popover.Trigger(trigger, ?className = triggerClassName)
                    Popover.Content(
                        ?className = contentClassName,
                        children =
                            Html.div [
                                prop.className "swt:flex swt:flex-col swt:gap-2"
                                prop.children [
                                    Html.div [
                                        prop.className "swt:flex swt:items-start swt:justify-between swt:gap-2"
                                        prop.children [
                                            Html.div [ prop.className "swt:flex-1"; prop.children content ]
                                            Popover.Close()
                                        ]
                                    ]
                                ]
                            ]
                    )
                ]
        )
