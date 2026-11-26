module Arcitisa.Components.Composite.Layout.RightSidebarContext

open Feliz
open Arcitisa.Components.Primitive

type RightSidebarState<'A> = {
    isOpen: bool
    setIsOpen: bool -> unit
    sidebarType: 'A
    setSidebarType: 'A -> unit
} with

    static member Empty<'A>() : RightSidebarState<'A> = {
        isOpen = false
        setIsOpen = ignore
        sidebarType = Unchecked.defaultof<'A>
        setSidebarType = ignore
    }

type RightSidebarCtxState = {
    isOpen: bool
    setIsOpen: bool -> unit
    sidebarType: obj
    setSidebarType: obj -> unit
} with

    static member Empty: RightSidebarCtxState = {
        isOpen = false
        setIsOpen = ignore
        sidebarType = null
        setSidebarType = ignore
    }

module RightSidebarHelper =

    let toRightSidebarCtxState (state: RightSidebarState<'A>) : RightSidebarCtxState = {
        isOpen = state.isOpen
        setIsOpen = state.setIsOpen
        sidebarType = box state.sidebarType
        setSidebarType = fun nextSidebarType -> state.setSidebarType (unbox<'A> nextSidebarType)
    }

    let unboxOrDefault<'A> (value: obj) : 'A =
        if obj.ReferenceEquals(value, null) then
            Unchecked.defaultof<'A>
        else
            unbox<'A> value

open RightSidebarHelper

let RightSidebarCtx =
    React.createContext<RightSidebarCtxState> (RightSidebarCtxState.Empty)

[<Hook>]
let useRightSidebarCtx<'A> () : RightSidebarState<'A> =
    let context = React.useContext RightSidebarCtx

    {
        isOpen = context.isOpen
        setIsOpen = context.setIsOpen
        sidebarType = unboxOrDefault<'A> context.sidebarType
        setSidebarType = fun nextSidebarType -> context.setSidebarType (box nextSidebarType)
    }
