namespace Arcitisa.Components.Shared

[<AutoOpen>]
module Extensions =

    module Option =
        /// <summary>
        /// If function returns `true` then return `Some x` otherwise `None`
        /// </summary>
        /// <param name="f"></param>
        /// <param name="x"></param>
        let where f x = if f x then Some x else None
        /// <summary>
        /// If function return `true` then return `None` otherwise `Some x`
        /// </summary>
        /// <param name="f"></param>
        /// <param name="x"></param>
        let whereNot f x = if f x then None else Some x
