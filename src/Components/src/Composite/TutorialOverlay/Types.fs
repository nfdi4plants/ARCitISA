module Arcitisa.Components.Composite.TutorialOverlay.Types

open Fable.Core
open Browser.Types

/// How a tutorial step's completion is detected. Moving to the next step is
/// always the user's click on Next/Skip; completion only changes what the
/// card shows (success state, Skip turning into Next, sidebar checkmark).
[<RequireQualifiedAccess>]
type TutorialAdvance =
    /// Completed by reading past it with the Next button.
    | Manual
    /// Completed once an event of the given type fires on (or inside) an
    /// element matching the selector - this is what makes a step a hands-on task.
    | OnEvent of eventType: string * selector: string
    /// Completed once the polled condition returns true. The check receives the
    /// tutorial content container, so it can query only the wrapped UI instead
    /// of the whole document - useful for outcomes that are not a single click,
    /// such as a drag landing or an element appearing.
    | OnCondition of check: (HTMLElement -> bool)

type TutorialStep = {
    Id: string
    Title: string
    Description: string
    /// CSS selector, scoped to the tutorial content, whose matches get the
    /// spotlight. Steps with a task highlight every match (e.g. a drag source
    /// and its dropzone) and keep the whole UI interactive; explanation steps
    /// spotlight the first match and block interactions elsewhere. None dims
    /// the whole surface lightly and centers the card.
    TargetSelector: string option
    /// Try-it instruction shown emphasized under the description.
    Task: string option
    Advance: TutorialAdvance
    /// Names the sandbox state this step starts from. Steps without one
    /// inherit the nearest earlier checkpoint. Whenever the (inherited)
    /// checkpoint of the active step differs from the one currently mounted,
    /// the tutorial content is remounted through the host's render function -
    /// so jumping or going back to a step always restores a state in which its
    /// task can be fulfilled, no matter what earlier interactions changed.
    Checkpoint: string option
}

/// Plain-function constructors so Storybook/TypeScript hosts can build steps
/// without reaching into Fable union runtime representations.
[<Mangle(false)>]
module Exports =

    let advanceManual () = TutorialAdvance.Manual

    let advanceOnEvent (eventType: string, selector: string) =
        TutorialAdvance.OnEvent(eventType, selector)

    let advanceOnCondition (check: HTMLElement -> bool) = TutorialAdvance.OnCondition check

    let step
        (
            id: string,
            title: string,
            description: string,
            targetSelector: string option,
            task: string option,
            advance: TutorialAdvance,
            checkpoint: string option
        ) : TutorialStep =
        {
            Id = id
            Title = title
            Description = description
            TargetSelector = targetSelector
            Task = task
            Advance = advance
            Checkpoint = checkpoint
        }
