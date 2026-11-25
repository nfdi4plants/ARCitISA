module Arcitisa.Components.DateTimeExtensions

let getUtcNowISO () = System.DateTime.UtcNow.ToString("O")

/// The time from value to now in the largest fitting unit, for example "5 minutes ago".
let timeAgo (now: System.DateTime) (value: System.DateTime) : string =
    let span =
        max System.TimeSpan.Zero (now.ToUniversalTime() - value.ToUniversalTime())

    if span.TotalSeconds < 60.0 then
        sprintf "%d seconds ago" (int span.TotalSeconds)
    elif span.TotalMinutes < 60.0 then
        sprintf "%d minutes ago" (int span.TotalMinutes)
    elif span.TotalHours < 24.0 then
        sprintf "%d hours ago" (int span.TotalHours)
    elif span.TotalDays < 30.0 then
        sprintf "%d days ago" (int span.TotalDays)
    elif span.TotalDays < 365.0 then
        sprintf "%d months ago" (int (span.TotalDays / 30.0))
    else
        sprintf "%d years ago" (int (span.TotalDays / 365.0))
