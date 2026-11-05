module Swate.Components.Primitive.Banner.Types

open Fable.Core

/// Sets the colour of a banner and its default icon.
[<StringEnum; RequireQualifiedAccess>]
type BannerVariant =
    | Info
    | Success
    | Warning
    | Error

type BannerAction = {
    Label: string
    OnClick: unit -> unit
    /// Fully qualified icon class, for example "swt:fluent--arrow-sync-24-regular".
    Icon: string option
    Disabled: bool
} with

    static member create(label: string, onClick: unit -> unit, ?icon: string, ?disabled: bool) = {
        Label = label
        OnClick = onClick
        Icon = icon
        Disabled = defaultArg disabled false
    }

/// One banner in a list of banners, for example in Layout.Main.
type BannerEntry = {
    /// Unique within the list. It is also the banner's test id.
    Key: string
    Message: string
    Variant: BannerVariant
    /// Fully qualified icon class. None uses the variant's icon.
    Icon: string option
    Actions: BannerAction[]
    /// Some makes the banner closable by the user. The host removes the entry when it is called.
    OnClose: (unit -> unit) option
} with

    static member create
        (
            key: string,
            message: string,
            ?variant: BannerVariant,
            ?icon: string,
            ?actions: BannerAction[],
            ?onClose: unit -> unit
        ) =
        {
            Key = key
            Message = message
            Variant = defaultArg variant BannerVariant.Info
            Icon = icon
            Actions = defaultArg actions [||]
            OnClose = onClose
        }
