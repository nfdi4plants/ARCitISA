module internal Swate.Components.ClipboardCodecBrowserTests

open ARCtrl
open Swate.Components
open Vitest

for rejectHtml in [ false; true ] do
    Vitest.test (
        $"clipboard write retries with HTML before falling back to plain text (rejectHtml={rejectHtml})",
        fun () -> promise {
            let attempts = ResizeArray<string[]>()
            let plainText = ref None

            let clipboard =
                { new Clipboard with
                    member _.read() = promise { return [||] }
                    member _.readText() = promise { return "" }

                    member _.write items = promise {
                        attempts.Add items.[0].types

                        if attempts.Count = 1 || rejectHtml then
                            failwith "Unsupported clipboard content"
                    }

                    member _.writeText value = promise { plainText.Value <- Some value }
                }

            let restoreClipboard = ClipboardTestHelpers.install clipboard
            let restoreConstructor = ClipboardTestHelpers.installItemConstructor ()

            try
                do! ClipboardCodec.write "plain" (Some [| [| CompositeCell.FreeText "plain" |] |])

                Vitest.expect(attempts |> Seq.toArray).toEqual [|
                    [| "text/plain"; ClipboardCodec.MimeType; "text/html" |]
                    [| "text/plain"; "text/html" |]
                |]

                Vitest.expect(plainText.Value).toEqual (if rejectHtml then Some "plain" else None)
            finally
                restoreConstructor ()
                restoreClipboard ()
        }
    )
