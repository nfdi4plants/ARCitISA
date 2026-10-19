namespace Swate.Components.Composite.Template

open ARCtrl
open Fable.Core
open Feliz
open Swate.Components
open Swate.Components.Primitive
open Swate.Components.Primitive.LoadingSpinner


module TemplateHelper = Swate.Components.Composite.Template.Helper

module private TemplatesDisplayHelper =

    [<Literal>]
    let ColCount = 5

[<Erase; Mangle(false)>]
type TemplatesDisplay =

    [<ReactComponent>]
    static member private TemplateRefreshButton(isRefreshing: bool, onRefresh: unit -> unit) =
        Html.button [
            prop.className "swt:btn swt:btn-neutral swt:btn-square"
            prop.title "Refresh templates"
            prop.disabled isRefreshing
            prop.onClick (fun e ->
                e.preventDefault ()
                e.stopPropagation ()
                onRefresh ()
            )
            prop.children [ Icons.ArrowsRotate() ]
        ]

    [<ReactMemoComponent(AreEqualFn.FsEqualsButFunctions)>]
    static member private TemplateTableItem
        (template: Template, isSelected: bool, toggleIsSelected: unit -> unit, ?key: obj)
        =

        let showDetails, setShowDetails = React.useState false

        let authorStr =
            template.Authors
            |> Seq.map (fun author ->
                let givenName =
                    [ author.FirstName; author.LastName; author.MidInitials ]
                    |> List.choose id
                    |> String.concat " "

                givenName
            )
            |> String.concat ", "

        let sharedClasses = "swt:cursor-pointer"
        let isSelectedClass = "swt:bg-primary/10"
        let sharedProps = [ prop.onClick (fun _ -> toggleIsSelected ()) ]

        React.KeyedFragment(
            template.Id,
            [
                Html.tr [
                    prop.testId $"template-row-{template.Id}"
                    prop.role.button
                    prop.ariaRoleDescription "toggle template selection"
                    yield! sharedProps
                    prop.className [
                        sharedClasses
                        if isSelected then
                            isSelectedClass
                        if showDetails then
                            "swt:border-transparent!" // removes bottom border to indicate relation to details row
                    ]
                    prop.key "generic"
                    prop.children [
                        Html.td [
                            Html.input [
                                prop.testId $"template-selection-{template.Id}"
                                prop.custom ("readOnly", true)
                                prop.isChecked isSelected
                                prop.type'.checkbox
                                prop.className "swt:checkbox"
                            ]
                        ]
                        Html.td [
                            prop.children [
                                Html.div [ prop.title template.Name; prop.text template.Name ]
                            ]
                        ]
                        Html.td [
                            prop.children [
                                Html.div [
                                    prop.className "swt:flex-col swt:items-start swt:text-xs swt:opacity-60"
                                    prop.children [
                                        Html.div [
                                            prop.className "swt:text-xs font-semibold"
                                            prop.text (template.Organisation.ToString())
                                        ]
                                        Html.div [
                                            prop.className "swt:text-xs"
                                            prop.text (sprintf "v%s" template.Version)
                                        ]
                                    ]
                                ]
                            ]
                        ]
                        Html.td [
                            Html.p [
                                prop.title authorStr
                                prop.className "swt:flex swt:items-center swt:text-xs swt:opacity-60 swt:line-clamp-3!"
                                prop.text authorStr
                            ]
                        ]
                        Html.td [
                            Buttons.Buttons.CollapseButton(
                                showDetails,
                                setShowDetails,
                                classes = "swt:btn-sm",
                                classFn = (fun isCollapsed -> if isCollapsed then "swt:btn-primary" else "")
                            )
                        ]
                    ]
                ]
                if showDetails then
                    Html.tr [
                        prop.className [
                            sharedClasses
                            if isSelected then
                                isSelectedClass
                        ]
                        yield! sharedProps
                        prop.key "details"
                        prop.children [
                            Html.td [
                                prop.className "swt:pt-0"
                                prop.colSpan TemplatesDisplayHelper.ColCount
                                prop.children [
                                    Html.div [
                                        prop.className "swt:py-2 swt:text-xs"
                                        prop.text template.Description
                                    ]
                                    Html.div [
                                        prop.className "swt:flex swt:gap-1"
                                        prop.children [
                                            for tag in template.EndpointRepositories do
                                                Html.span [
                                                    prop.className "swt:badge swt:badge-sm swt:badge-accent"
                                                    prop.text tag.NameText
                                                ]
                                            for tag in template.Tags do
                                                Html.span [
                                                    prop.className "swt:badge swt:badge-sm swt:badge-secondary"
                                                    prop.text tag.NameText
                                                ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
            ]
        )

    [<ReactComponent(true)>]
    static member TemplatesDisplay
        (
            templates: Template[],
            selectedTemplateIds: Set<System.Guid>,
            toggleTemplateSelection: System.Guid -> unit,
            isLoading: bool,
            refreshTemplates: unit -> unit,
            ?maxheight: Styles.ICssUnit
        ) =
        // static member private DisplayTemplates(templates: Template[], model: Model, dispatch, ?maxheight: Styles.ICssUnit) =
        let maxheight = defaultArg maxheight (length.px 600)

        Html.div [
            prop.style [ style.maxHeight maxheight ]
            prop.className "swt:shrink swt:grow swt:h-fit swt:w-full"
            prop.children [
                Html.table [
                    prop.className "swt:table swt:table-fixed swt:w-full"
                    prop.children [

                        Html.colgroup [
                            Html.col [ prop.className "swt:w-14" ] // select btn: fixed size
                            Html.col [ prop.className "swt:w-[35%]" ] // Name: limited
                            Html.col [ prop.className "swt:w-[20%]" ] // Details: limited
                            Html.col [ prop.className "swt:w-[30%]" ] // authors: limited
                            Html.col [ prop.className "swt:w-18" ] // actions: fixed size
                        ]

                        Html.thead [
                            Html.tr [
                                Html.th [
                                    Html.div [
                                        prop.className "swt:flex swt:items-center"
                                        prop.children [ Icons.Filter("swt:size-3"); Html.span templates.Length ]
                                    ]
                                ]
                                Html.th "Template Name"
                                Html.th "Details"
                                Html.th "Authors"
                                Html.th [
                                    TemplatesDisplay.TemplateRefreshButton(isLoading, refreshTemplates)
                                ]
                            ]
                        ]
                        Html.tbody [
                            match isLoading with
                            | true ->
                                Html.tr [
                                    Html.td [
                                        prop.colSpan TemplatesDisplayHelper.ColCount
                                        prop.style [ style.textAlign.center ]
                                        prop.children [
                                            LoadingSpinner.LoadingSpinner("Loading templates...", size = DaisyuiSize.XL)
                                        ]
                                    ]
                                ]
                            | false ->
                                match templates with
                                | [||] ->
                                    Html.tr [
                                        Html.td [
                                            prop.colSpan TemplatesDisplayHelper.ColCount
                                            prop.children [
                                                Html.div [
                                                    prop.className
                                                        "swt:flex swt:justify-center swt:items-center swt:w-full swt:h-full"
                                                    prop.text "No templates found."
                                                ]
                                            ]
                                        ]
                                    ]
                                | _ ->
                                    for i in 0 .. templates.Length - 1 do
                                        let template = templates.[i]

                                        let isSelected = selectedTemplateIds |> Set.exists (fun t -> t = template.Id)

                                        let toggleIsSelected = fun _ -> toggleTemplateSelection template.Id

                                        TemplatesDisplay.TemplateTableItem(
                                            template,
                                            isSelected,
                                            toggleIsSelected,
                                            key = template.Id
                                        )
                        ]
                    ]
                ]
            ]
        ]
