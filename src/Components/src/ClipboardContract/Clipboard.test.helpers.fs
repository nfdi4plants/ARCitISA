module internal Swate.Components.ClipboardTestHelpers

open ARCtrl
open Fable.Core

/// Restore the original property descriptor, including when the browser has no clipboard API.
[<Emit("(() => { const descriptor = Object.getOwnPropertyDescriptor(navigator, 'clipboard'); Object.defineProperty(navigator, 'clipboard', { configurable: true, value: $0 }); return () => { if (descriptor) Object.defineProperty(navigator, 'clipboard', descriptor); else delete navigator.clipboard; }; })()")>]
let install (_clipboard: Clipboard) : (unit -> unit) = jsNative

[<Emit("""(() => {
    const descriptor = Object.getOwnPropertyDescriptor(globalThis, 'ClipboardItem');
    Object.defineProperty(globalThis, 'ClipboardItem', { configurable: true, value: class {
        constructor(values) { this.types = Object.keys(values); this.values = values; }
        async getType(type) {
            const blob = this.values[type];
            return { text: () => new Promise((resolve, reject) => {
                const reader = new FileReader();
                reader.onload = () => resolve(reader.result);
                reader.onerror = () => reject(reader.error);
                reader.readAsText(blob);
            }) };
        }
    } });
    return () => {
        if (descriptor) Object.defineProperty(globalThis, 'ClipboardItem', descriptor);
        else delete globalThis.ClipboardItem;
    };
})()""")>]
let installItemConstructor () : (unit -> unit) = jsNative

let createItem (plainText: string) (cells: CompositeCell[][]) : ClipboardItem =
    let payload = cells |> ClipboardCodec.createPayload |> ClipboardCodec.encode

    { new ClipboardItem with
        member _.types = [| ClipboardCodec.MimeType; "text/plain" |]

        member _.getType mimeType = promise {
            let text =
                if mimeType = ClipboardCodec.MimeType then
                    payload
                else
                    plainText

            return
                { new ClipboardBlob with
                    member _.text() = promise { return text }
                }
        }
    }
