namespace SidebarComponents

open Fable.React
open Fable.React.Props
open Fable.Core
open Fable.Core.JsInterop
open Model
open Messages
open Messages.BuildingBlock
open Arcitisa.Components.Shared
open Feliz
open Elmish

module private LayoutHelperAux =

    let rnd = System.Random()

    let mutable order: bool =
        let v = rnd.Next(0, 10)
        if v > 5 then false else true

type SidebarLayout =

    static member LogicContainer(children: ReactElement list) =
        Html.div [
            //     prop.className "border-l-4 border-transparent px-4 py-2 shadow-md"
            //     prop.style [
            //         let rndVal = rnd.Next(30,70)
            //         let colorArr = [|NFDIColors.LightBlue.Lighter10; NFDIColors.Mint.Lighter10;|]
            //         style.custom("borderImageSlice", "1")
            //         style.custom("borderImageSource", $"linear-gradient({colorArr.[if order then 0 else 1]} {100-rndVal}%%, {colorArr.[if order then 1 else 0]})")
            //         order <- not order
            //     ]
            prop.className "swt:relative swt:flex swt:p-4 animated-border swt:shadow-md swt:gap-4 swt:flex-col" //experimental
            prop.children children
        ]

    static member Container(children: ReactElement list) =
        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-2 swt:py-4"
            prop.children children
        ]

    static member Header(txt: string) =
        Html.h3 [
            prop.className "swt:text-lg swt:font-semibold"
            prop.text txt
        ]

    static member Description(content: ReactElement) =
        Html.div [
            prop.className "swt:prose-sm swt:prose-p:m-1 swt:prose-ul:my-1 swt:prose-ul:list-disc"
            prop.children content
        ]

    static member Description(content: string) =
        SidebarLayout.Description(Html.p content)
