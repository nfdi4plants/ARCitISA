module Arcitisa.Components.Composite.Template.Helper

open System
open ARCtrl
open Arcitisa.Components.Shared


let toFullAuthorName (author: Person) =
    [ author.FirstName; author.MidInitials; author.LastName ]
    |> List.choose id
    |> String.concat " "
