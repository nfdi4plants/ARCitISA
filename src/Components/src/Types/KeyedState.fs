namespace Arcitisa.Components

/// A state that is associated with a key.
///
/// Downstream components can compare the key with the current key to determine if the state is still valid. If the key has changed, the state should be considered invalid and instead a default can be shown.
type KeyedState<'T, 'K when 'K: equality> = {
    state: 'T
    key: 'K
} with

    static member init(initialState: 'T, key: 'K) : KeyedState<'T, 'K> = { state = initialState; key = key }

    static member init(initialState: 'T, keyFn: 'T -> 'K) : KeyedState<'T, 'K> = {
        state = initialState
        key = keyFn initialState
    }
