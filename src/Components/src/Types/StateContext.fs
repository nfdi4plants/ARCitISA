namespace Arcitisa.Components

type StateContext<'T> = { state: 'T; setState: 'T -> unit }

module StateContext =

    let init initialState = {
        state = initialState
        setState = fun _ -> ()
    }

type StateUpdaterContext<'T> = {
    state: 'T
    setStateUpdater: ('T -> 'T) -> unit
}

module StateUpdaterContext =

    let init initialState : StateUpdaterContext<'T> = {
        state = initialState
        setStateUpdater = fun _ -> ()
    }
