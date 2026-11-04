module Swate.Components.Primitive.Banner.Types

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
