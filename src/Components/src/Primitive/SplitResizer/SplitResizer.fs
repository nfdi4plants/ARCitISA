namespace Arcitisa.Components.Primitive.SplitResizer

open Fable.Core
open Browser.Types
open Feliz
open Arcitisa.Components

[<Erase; Mangle(false)>]
type SplitResizer =

    /// A handle that sits on the border between two panes. While the user drags it, the ratio of
    /// the pointer position within the container goes to onRatio, clamped to 0.15 to 0.85. The
    /// handle is absolutely positioned, so the parent places it on the border. With vertical set,
    /// the panes are stacked and the ratio comes from clientY, otherwise it comes from clientX.
    /// The component reads vertical once at mount.
    [<ReactComponent>]
    static member SplitResizer
        (containerRef: IRefValue<option<HTMLElement>>, vertical: bool, onRatio: float -> unit, ?testId: string)
        =
        let draggingRef = React.useRef false
        let draggingUi, setIsDraggingUi = React.useState false
        let pointerPosition, setPointerPosition = React.useState (None: int option)
        let throttledPointerPosition = React.useThrottle (pointerPosition, 20)

        // The effect runs when the throttled position changes, which caps how often onRatio is
        // called. It reads the latest position, so the last move of a drag is never lost.
        React.useEffect (
            (fun () ->
                match containerRef.current, pointerPosition with
                | None, _
                | Some null, _
                | _, None -> ()
                | Some container, Some pointerPos ->
                    let rect = container.getBoundingClientRect ()

                    let directionalPointerPosition =
                        if vertical then
                            (float pointerPos - rect.top) / rect.height
                        else
                            (float pointerPos - rect.left) / rect.width

                    onRatio (max 0.15 (min 0.85 directionalPointerPosition))
            ),
            [| box throttledPointerPosition |]
        )

        // Register the drag handler event
        // This is not throttled, therefore minimal logic inside the event listener
        React.useEffectOnce (
            (fun () ->
                let stop (_: Event) =
                    draggingRef.current <- false
                    setIsDraggingUi false

                let onMove (event: Event) =
                    let e = unbox<PointerEvent> event

                    if draggingRef.current then
                        // A release outside the window or a cancelled press sends no pointerup.
                        if e.buttons = 0 then
                            stop event
                        else
                            setPointerPosition (Some(int (if vertical then e.clientY else e.clientX)))

                Browser.Dom.document.addEventListener ("pointermove", onMove)
                Browser.Dom.document.addEventListener ("pointerup", stop)
                Browser.Dom.document.addEventListener ("pointercancel", stop)

                FsReact.createDisposable (fun () ->
                    Browser.Dom.document.removeEventListener ("pointermove", onMove)
                    Browser.Dom.document.removeEventListener ("pointerup", stop)
                    Browser.Dom.document.removeEventListener ("pointercancel", stop)
                )
            )
        )

        Html.div [
            if testId.IsSome then
                prop.testId testId.Value
            prop.onPointerDown (fun _ ->
                draggingRef.current <- true
                setIsDraggingUi true
            )
            prop.data ("active", if draggingUi then "true" else "false")
            prop.className [
                "swt:shrink-0 swt:select-none swt:transition-colors swt:bg-transparent swt:hover:bg-primary swt:z-10 swt:absolute swt:data-[active=true]:bg-primary"
                if vertical then
                    "swt:h-1.5 swt:cursor-row-resize swt:w-full"
                else
                    "swt:w-1.5 swt:cursor-col-resize swt:h-full swt:top-0 swt:bottom-0 swt:-left-1"
            ]
        ]
