module internal Swate.Components.Primitive.Banner.BannerTests

open Browser.Types
open Vitest
open Swate.Components.Primitive.Banner.Types

Vitest.afterEach (fun () -> RTL.cleanup ())

let private render (message: string) (actions: BannerAction[]) =
    RTL.render (Banner.Banner(message, actions = actions)) |> ignore

let private textOf (testId: string) =
    (RTL.screen.getByTestId testId).textContent

let private present (testId: string) =
    (RTL.screen.queryByTestId testId).IsSome

let private buttons () =
    RTL.screen.queryAllByRole("button").Count

let private actionButton (index: int) =
    RTL.screen.getByTestId $"BannerAction{index}" :?> HTMLButtonElement

Vitest.describe (
    "Banner",
    fun () ->
        Vitest.test (
            "has the alert role",
            fun () ->
                render "Message" [||]

                let alert = RTL.screen.getByRole "alert"

                Vitest.expect(alert.contains (RTL.screen.getByTestId "BannerLine0")).toBe true
        )

        Vitest.test (
            "renders a one-line message as a single line",
            fun () ->
                render "only line" [||]

                Vitest.expect(textOf "BannerLine0").toBe "only line"
                Vitest.expect(present "BannerLine1").toBe false
        )

        Vitest.test (
            "renders each line break of the message as its own line",
            fun () ->
                render "first line\nsecond line\r\nthird line" [||]

                Vitest.expect(textOf "BannerLine0").toBe "first line"
                Vitest.expect(textOf "BannerLine1").toBe "second line"
                Vitest.expect(textOf "BannerLine2").toBe "third line"
                Vitest.expect(present "BannerLine3").toBe false
        )

        Vitest.test (
            "renders the given actions in order",
            fun () ->
                render "Message" [|
                    BannerAction.create ("Alpha", ignore)
                    BannerAction.create ("Beta", ignore, icon = "swt:fluent--arrow-sync-24-regular")
                |]

                Vitest.expect((actionButton 0).textContent).toBe "Alpha"
                Vitest.expect((actionButton 1).textContent).toBe "Beta"
                Vitest.expect(present "BannerAction2").toBe false
        )

        Vitest.test (
            "calls the callback of the clicked action only",
            fun () ->
                let calls = ResizeArray<string>()

                render "Message" [|
                    BannerAction.create ("Alpha", fun () -> calls.Add "alpha")
                    BannerAction.create ("Beta", fun () -> calls.Add "beta")
                |]

                RTL.fireEvent.click (actionButton 1)

                Vitest.expect(calls.Count).toBe 1
                Vitest.expect(calls.[0]).toBe "beta"
        )

        Vitest.test (
            "does not call the callback of a disabled action",
            fun () ->
                let calls = ref 0

                render "Message" [|
                    BannerAction.create ("Alpha", (fun () -> calls.Value <- calls.Value + 1), disabled = true)
                |]

                RTL.fireEvent.click (actionButton 0)

                Vitest.expect((actionButton 0).disabled).toBe true
                Vitest.expect(calls.Value).toBe 0
        )

        Vitest.test (
            "has no button besides the given actions",
            fun () ->
                render "Message" [|
                    BannerAction.create ("Alpha", ignore)
                    BannerAction.create ("Beta", ignore)
                |]

                Vitest.expect(buttons ()).toBe 2
        )

        Vitest.test (
            "has no button at all without actions",
            fun () ->
                render "Message" [||]

                Vitest.expect(buttons ()).toBe 0
                Vitest.expect(present "BannerActions").toBe false
        )

        Vitest.test (
            "uses the given test id for the root and the parts",
            fun () ->
                RTL.render (
                    Banner.Banner("Message", actions = [| BannerAction.create ("Alpha", ignore) |], testId = "Custom")
                )
                |> ignore

                Vitest.expect(present "Custom").toBe true
                Vitest.expect(present "CustomLine0").toBe true
                Vitest.expect(present "CustomAction0").toBe true
                Vitest.expect(present "Banner").toBe false
        )
)
