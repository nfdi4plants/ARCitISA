module internal Arcitisa.Components.Primitive.Select.MultiSelectSample

open Fable.Core
open Feliz
open Types
open Arcitisa.Components
open Arcitisa.Components.Primitive.Select.Context

[<ReactComponent(true)>]
let Sample () =

    let options: SelectItem<{| givenName: string; age: int |}>[] = [|
        {|
            label = "Kevin Frey"
            item = {| givenName = "Kevin Frey"; age = 30 |}
        |}
        {|
            label = "John Doe"
            item = {| givenName = "John Doe"; age = 25 |}
        |}
        {|
            label = "Jane Smith"
            item = {| givenName = "Jane Smith"; age = 28 |}
        |}
        {|
            label = "Alice Johnson"
            item = {|
                givenName = "Alice Johnson"
                age = 22
            |}
        |}
        {|
            label = "Bob Brown"
            item = {| givenName = "Bob Brown"; age = 35 |}
        |}
        {|
            label = "Charlie White"
            item = {|
                givenName = "Charlie White"
                age = 40
            |}
        |}
        {|
            label = "Diana Green"
            item = {|
                givenName = "Diana Green"
                age = 32
            |}
        |}
        {|
            label = "Ethan Black"
            item = {|
                givenName = "Ethan Black"
                age = 29
            |}
        |}
        {|
            label = "Fiona Blue"
            item = {| givenName = "Fiona Blue"; age = 27 |}
        |}
    // Shoutout to my ai for the mock data
    |]

    let selectedIndices, setSelectedIndices = React.useState (Set.empty: Set<int>)

    MultiSelect.MultiSelect(options, selectedIndices, setSelectedIndices)
