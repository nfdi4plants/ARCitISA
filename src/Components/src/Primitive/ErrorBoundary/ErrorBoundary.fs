namespace Arcitisa.Components.Primitive.ErrorBoundary

open Fable.Core
open Fable.Core.JsInterop
open Fable.React
open Feliz
open Arcitisa.Components.JsBindings

module private ErrorBoundaryTypes =

    /// Props of the class component. An anonymous record is a plain JavaScript object, which React copies.
    /// ResetKey is empty when the caller gives none.
    type Props = {|
        Children: ReactElement
        Fallback: exn -> ReactElement
        ResetKey: string
    |}

    /// Error is null while the children render. React merges the object that setState and the static methods
    /// return into the state, and a field that is missing leaves the old value in place. An option that is None
    /// leaves its field out, so it could not clear the error.
    /// SeenResetKey is the key of the props at the last render. A different key clears the error.
    type State = {| Error: exn; SeenResetKey: string |}

module private ErrorBoundaryHelper =

    let noError: exn = Unchecked.defaultof<exn>

open ErrorBoundaryTypes
open ErrorBoundaryHelper

/// The React class component behind the boundary. An error boundary has to be a class with the static method
/// getDerivedStateFromError, as React offers no hook for it. The base type is the Component class of
/// Fable.React.Types, which imports it from react.
[<AttachMembers>]
type private ErrorBoundaryClass(initialProps: Props) =
    inherit Component<Props, State>(initialProps)

    do
        base.setInitState {|
            Error = noError
            SeenResetKey = initialProps.ResetKey
        |}

    /// Called by React after a descendant threw while rendering. The returned object is merged into the state.
    /// Anything can be thrown, a string for example, so the state keeps an exception with the text of it.
    static member getDerivedStateFromError(error: obj) : obj =
        box {|
            Error = exn (Errors.messageOf error)
        |}

    /// A new resetKey clears the error, so the children get another try.
    static member getDerivedStateFromProps(props: Props, state: State) : obj =
        if props.ResetKey <> state.SeenResetKey then
            box {|
                Error = noError
                SeenResetKey = props.ResetKey
            |}
        else
            null

    override this.componentDidCatch(error: exn, info: obj) : unit =
        Browser.Dom.console.error ("The error boundary caught an error", error, info)

    override this.render() : ReactElement =
        let error = this.state.Error

        if isNull (box error) then
            this.props.Children
        else
            this.props.Fallback error

/// Catches the errors that the children throw while rendering and shows the fallback in their place.
/// The fallback gets the error. A new resetKey clears the error, which fits a view that changes. The boundary
/// catches errors of the children only, not an error in its own parent.
[<Erase; Mangle(false)>]
type ErrorBoundary =

    [<ReactComponent>]
    static member ErrorBoundary
        (children: ReactElement, fallback: exn -> ReactElement, ?resetKey: string)
        : ReactElement =
        let props: Props = {|
            Children = children
            Fallback = fallback
            ResetKey = defaultArg resetKey ""
        |}

        ReactBindings.React.createElement (jsConstructor<ErrorBoundaryClass>, props, [])
