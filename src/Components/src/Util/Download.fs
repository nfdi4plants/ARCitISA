module Arcitisa.Components.Util.Download

open Arcitisa.Components.Shared
open Fable.Remoting.Client

let download (filename, bytes: byte[]) = bytes.SaveFileAs(filename)

let downloadFromString (filename, content: string) =
    let bytes = System.Text.Encoding.UTF8.GetBytes(content)
    bytes.SaveFileAs(filename)
