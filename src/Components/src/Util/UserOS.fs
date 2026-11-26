module Arcitisa.Components.Util.UserOS

open Arcitisa.Components

type UserOS =
    | Windows
    | MacOS
    | Linux
    | Android
    | IOS
    | Unknown

let getUserOS () =
    let userAgent = navigator.userAgent.ToLowerInvariant()
    let platform = navigator.platform.ToLowerInvariant()

    if userAgent.Contains("android") then
        Android
    elif
        userAgent.Contains("iphone")
        || userAgent.Contains("ipad")
        || userAgent.Contains("ipod")
    then
        IOS
    elif platform.Contains("mac") then
        MacOS
    elif platform.Contains("win") then
        Windows
    elif platform.Contains("linux") then
        Linux
    else
        Unknown
