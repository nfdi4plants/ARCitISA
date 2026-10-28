module ElectronRenderer.FileTreeFreeLfsModalTests

open Browser.Types
open Renderer.Components.LeftSidebar.FileExplorer.Modals
open Vitest

Vitest.afterEach (fun () -> RTL.cleanup ())

let private renderModal () =
    let mutable freeCalls = 0
    let mutable closeCalls = 0

    RTL.render (
        FileTreeFreeLfsModal.Main(
            isOpen = true,
            itemName = Some "reads.fastq",
            close = (fun () -> closeCalls <- closeCalls + 1),
            submit = (fun () -> freeCalls <- freeCalls + 1)
        )
    )
    |> ignore

    (fun () -> freeCalls), (fun () -> closeCalls)

Vitest.test (
    "cancelling the free confirmation does not free the local copy",
    fun () ->
        let freeCalls, closeCalls = renderModal ()

        RTL.fireEvent.click (RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Cancel")))

        Vitest.expect(freeCalls ()).toBe 0
        Vitest.expect(closeCalls ()).toBe 1
)

Vitest.test (
    "confirming the free confirmation frees the local copy exactly once",
    fun () ->
        let freeCalls, _ = renderModal ()

        RTL.fireEvent.click (RTL.screen.getByRole ("button", ByRoleOptions(name = Text "Free local copy")))

        Vitest.expect(freeCalls ()).toBe 1
)

Vitest.test (
    "a folder without a local copy to free cannot submit",
    fun () ->
        let mutable freeCalls = 0

        RTL.render (
            FileTreeFreeLfsModal.Main(
                isOpen = true,
                itemName = Some "data",
                close = ignore,
                submit = (fun () -> freeCalls <- freeCalls + 1),
                fileCount = 0
            )
        )
        |> ignore

        // Inside the modal box the close icon comes first, then Cancel, and the submit button is last. Focus
        // management adds hidden dismiss elements outside the box, so the query stays inside it.
        let buttons =
            (RTL.screen.getByTestId "modal_arc-free-lfs").querySelectorAll "button"

        let submit = buttons.[buttons.length - 1] :?> HTMLButtonElement

        Vitest.expect(submit.disabled).toBe true

        RTL.fireEvent.click submit

        Vitest.expect(freeCalls).toBe 0
)
