module Swate.Components.Composite.Template.Helper

open System
open ARCtrl
open Swate.Components.Shared


let toFullAuthorName (author: Person) =
    [ author.FirstName; author.MidInitials; author.LastName ]
    |> List.choose id
    |> String.concat " "
