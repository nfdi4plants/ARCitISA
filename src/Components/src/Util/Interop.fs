module internal Swate.Components.Util.Interop

let listOfArray (items: 'T[]) : 'T list = List.ofArray items

let okResult (value: 'T) : Result<'T, 'Error> = Ok value
