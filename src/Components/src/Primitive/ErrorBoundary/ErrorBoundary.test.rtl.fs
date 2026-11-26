module internal Arcitisa.Components.Primitive.ErrorBoundary.ErrorBoundaryTests

// React logs every error that a boundary catches to console.error. The tests silence that output and
// count the calls, because the boundary logs the error as well.

open Fable.Core
open Feliz
open Vitest

[<Emit("""(() => {
    globalThis.__boundaryErrors = 0;
    globalThis.__boundaryOriginalError = console.error;
    console.error = () => { globalThis.__boundaryErrors += 1 };
})()""")>]
let private silenceConsoleError () : unit = jsNative

[<Emit("(globalThis.__boundaryOriginalError && (console.error = globalThis.__boundaryOriginalError), delete globalThis.__boundaryOriginalError)")>]
let private restoreConsoleError () : unit = jsNative

[<Emit("globalThis.__boundaryErrors")>]
let private loggedErrors () : int = jsNative

Vitest.beforeEach (fun () -> silenceConsoleError ())

Vitest.afterEach (fun () ->
    RTL.cleanup ()
    restoreConsoleError ()
)

/// The child throws while the flag is set.
let private throws = ref true

[<ReactComponent>]
let private Child (message: string) =
    if throws.Value then
        failwith message

    Html.div [ prop.testId "child" ]

[<Emit("(() => { throw 'plain text' })()")>]
let private throwText () : unit = jsNative

[<ReactComponent>]
let private ThrowsText () =
    throwText ()
    Html.none

let private fallback (error: exn) : ReactElement =
    Html.div [
        prop.testId "fallback"
        prop.children [
            Html.span [ prop.testId "message"; prop.text error.Message ]
        ]
    ]

let private present (testId: string) : bool =
    (RTL.screen.queryByTestId testId).IsSome

Vitest.describe (
    "ErrorBoundary",
    fun () ->
        Vitest.test (
            "shows the fallback with the message of the error when a child throws",
            fun () ->
                throws.Value <- true
                RTL.render (ErrorBoundary.ErrorBoundary(Child "boom", fallback)) |> ignore

                Vitest.expect(present "fallback").toBe true
                Vitest.expect(present "child").toBe false
                Vitest.expect((RTL.screen.getByTestId "message").textContent).toBe "boom"
                Vitest.expect(loggedErrors ()).toBeGreaterThan 0
        )

        Vitest.test (
            "keeps the error while the resetKey stays and clears it when the resetKey changes",
            fun () ->
                throws.Value <- true

                let boundary (key: string) =
                    ErrorBoundary.ErrorBoundary(Child "boom", fallback, resetKey = key)

                let result = RTL.render (boundary "a")
                Vitest.expect(present "fallback").toBe true

                throws.Value <- false
                result.rerender (boundary "a")
                Vitest.expect(present "fallback").toBe true

                result.rerender (boundary "b")
                Vitest.expect(present "child").toBe true
                Vitest.expect(present "fallback").toBe false
        )

        Vitest.test (
            "catches the error of a new key again",
            fun () ->
                throws.Value <- false

                let boundary (key: string) =
                    ErrorBoundary.ErrorBoundary(Child "boom", fallback, resetKey = key)

                let result = RTL.render (boundary "a")
                Vitest.expect(present "child").toBe true

                throws.Value <- true
                result.rerender (boundary "b")
                Vitest.expect(present "fallback").toBe true
        )

        Vitest.test (
            "renders the siblings outside the boundary",
            fun () ->
                throws.Value <- true

                RTL.render (
                    Html.div [
                        Html.span [ prop.testId "sibling" ]
                        ErrorBoundary.ErrorBoundary(Child "boom", fallback)
                    ]
                )
                |> ignore

                Vitest.expect(present "sibling").toBe true
                Vitest.expect(present "fallback").toBe true
        )

        Vitest.test (
            "gives the fallback the text of a thrown value that is not an Error",
            fun () ->
                RTL.render (ErrorBoundary.ErrorBoundary(ThrowsText(), fallback)) |> ignore

                Vitest.expect((RTL.screen.getByTestId "message").textContent).toBe "plain text"
        )
)
