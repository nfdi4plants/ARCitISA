module Model.ARCitect

open ARCtrl
open Fable.Core
open Arcitisa.Components.Shared


module Interop =

    [<RequireQualifiedAccess>]
    module InteropTypes =


        type ARCitectFile = {|
            mimetype: string
            content: string
            size: int
            name: string
        |}

    type IARCitectOutAPI = {
        Init: unit -> JS.Promise<(ArcFilesDiscriminate * string * DatamapParentInfo option) option>
        Save: ArcFilesDiscriminate * string * DatamapParentInfo option -> JS.Promise<unit>
        /// selectDictionaries:bool -> JS.Promise<wasSuccessful: bool>
        RequestPaths: bool -> JS.Promise<bool>
        /// () -> JS.Promise<wasSuccessful: bool>
        RequestFile: unit -> JS.Promise<bool>
        /// returns person jsons
        RequestPersons: unit -> JS.Promise<string[]>
    }

    type IARCitectInAPI = {
        TestHello: string -> JS.Promise<string>
        /// JS.Promise<wasSuccessful: bool>
        ResponsePaths: string[] -> JS.Promise<bool>
        ResponseFile: InteropTypes.ARCitectFile -> JS.Promise<bool>
        Refresh: unit -> JS.Promise<bool>
        SetARCFile: ArcFilesDiscriminate * string * DatamapParentInfo option -> JS.Promise<bool>
    }

let api =
    MessageInterop.MessageInterop.createApi ()
    |> MessageInterop.MessageInterop.buildOutProxy<Interop.IARCitectOutAPI>

open Elmish

type Msg =
    | Init of ApiCall<unit, (ArcFilesDiscriminate * string * DatamapParentInfo option) option>
    | Save of ArcFiles
    /// ApiCall<selectDirectories: bool, wasSuccessful: bool>
    | RequestPaths of ApiCall<bool, bool>
    /// Selecting paths requires user input, which we cannot await.
    /// To avoid timeout `RequestPaths` simply returns true if call was successful,
    /// ... and `ResponsePaths` will be sent as soon as user selected the directories
    | ResponsePaths of string[]
    /// expects person jsons
    | RequestPersons of ApiCall<unit, string[]>
    | RequestFile of ApiCall<unit, bool>
    | ResponseFile of Interop.InteropTypes.ARCitectFile

type Model = {
    Persons: Person[]
} with

    static member init() = { Persons = [||] }
