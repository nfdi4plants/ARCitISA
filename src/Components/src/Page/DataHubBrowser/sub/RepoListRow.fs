namespace Arcitisa.Components.Page.DataHubBrowser.sub

open Fable.Core
open Feliz
open Arcitisa.Components
open Arcitisa.Components.Api.GitLabApi
open Arcitisa.Components.Composite.Actionbar
open Arcitisa.Components.Composite.Actionbar.Types
open Arcitisa.Components.Primitive

module private RepoListRowHelper =

    let timeAgoUpdated (dt: System.DateTime option) =
        dt
        |> Option.map (fun dateTime -> "Updated " + DateTimeExtensions.timeAgo System.DateTime.UtcNow dateTime)

[<Erase; Mangle(false)>]
type RepoListRow =

    [<ReactComponent>]
    static member private ActionbarButtons(buttonInfos: ButtonInfo[]) =
        Actionbar.Main(
            buttonInfos,
            1,
            barClassName =
                "swt:w-fit swt:h-fit swt:flex swt:flex-col swt:bg-base-300 swt:rounded-lg swt:shadow-sm swt:join swt:join-vertical",
            tooltipPosition = DaisyuiTooltipPosition.Left,
            buttonSize = DaisyuiSize.SM,
            buttonClassName = "swt:btn swt:btn-primary swt:btn-square swt:join-item"
        )

    [<ReactComponent>]
    static member RepoListRow
        (
            project: ExploreProjectDto,
            // onClone: ExploreProjectDto -> unit,
            // ?onOpen: (ExploreProjectDto -> unit),
            ?extraButtons: ExploreProjectDto -> ButtonInfo[]
        ) =

        let imageFailed, setImageFailed = React.useState (false)

        let visibility = project.visibility |> Option.defaultValue "public"

        let visibilityLabel, visibilityIcon =
            match visibility with
            | "private" -> "Private repository", "swt:fluent--lock-closed-24-regular swt:size-4"
            | "internal" -> "Internal repository", "swt:fluent--shield-24-regular swt:size-4"
            | _ -> "Public repository", "swt:fluent--globe-24-regular swt:size-4"

        let avatarInitial =
            if System.String.IsNullOrWhiteSpace project.name then
                "?"
            else
                let c = System.Math.Min(3, project.name.Length)
                project.name.Substring(0, c).ToUpperInvariant()

        let lastActivityString =
            project.last_activity_at |> RepoListRowHelper.timeAgoUpdated

        // let actionButtons = [|
        //     ButtonInfo.create (
        //         "swt:fluent--arrow-download-24-regular swt:size-5",
        //         "Clone repository",
        //         (fun () -> onClone project)
        //     )
        //     // match isLocallyCloned, onOpen with
        //     // | true, Some openFn ->
        //     //     ButtonInfo.create (
        //     //         "swt:fluent--open-24-regular swt:size-5",
        //     //         "Open local repository",
        //     //         (fun () -> openFn project)
        //     //     )
        //     | _ -> ()
        // |]

        Html.li [
            prop.testId ("GitLabRepoRow-" + string project.id)
            prop.className [ "swt:list-row" ]
            prop.children [
                // Avatar
                Html.div [
                    prop.className "swt:avatar swt:self-start swt:min-w-16"
                    prop.children [
                        Html.div [
                            prop.className "swt:w-16 swt:h-16 swt:rounded"
                            prop.children [
                                match project.avatar_url with
                                | Some avatarUrl when
                                    not imageFailed && not (System.String.IsNullOrWhiteSpace avatarUrl)
                                    ->
                                    Html.img [
                                        prop.testId ("GitLabRepoAvatarImage-" + string project.id)
                                        prop.src avatarUrl
                                        prop.alt project.name
                                        prop.onError (fun (_: Browser.Types.Event) -> setImageFailed true)
                                    ]
                                | _ ->
                                    Html.div [
                                        prop.testId ("GitLabRepoAvatarInitials-" + string project.id)
                                        prop.className
                                            "swt:w-full swt:h-full swt:rounded swt:bg-base-300 swt:flex swt:items-center swt:justify-center swt:text-xl swt:font-semibold"
                                        prop.text avatarInitial
                                        prop.ariaLabel avatarInitial
                                    ]
                            ]
                        ]
                    ]
                ]
                // Center ref
                Html.div [
                    prop.className "swt:flex swt:items-center swt:grow swt:min-w-0 swt:gap-2"
                    prop.children [
                        Html.a [
                            prop.href project.web_url
                            prop.target.blank
                            prop.rel "noopener noreferrer"
                            prop.className "swt:link swt:link-hover swt:text-base-content/70 swt:block swt:truncate"
                            prop.children [
                                Html.span [
                                    prop.className "swt:max-md:hidden"
                                    prop.text (project.``namespace``.name + " / ")
                                ]
                                Html.span [
                                    prop.className "swt:text-base-content swt:font-semibold"
                                    prop.text project.name
                                ]
                            ]
                        ]
                        Html.div [
                            prop.className "swt:tooltip swt:tooltip-top swt:size-4"
                            prop.ariaLabel visibilityLabel
                            prop.children [
                                Html.div [
                                    prop.className "swt:tooltip-content"
                                    prop.text visibilityLabel
                                ]
                                Html.i [ prop.className [ "swt:iconify"; visibilityIcon ] ]
                            ]
                        ]
                    ]
                ]
                if project.description.IsSome then
                    Html.div [
                        prop.className "swt:list-col-wrap swt:gap-1 swt:flex swt:flex-col"
                        prop.children [
                            Html.div [
                                prop.className
                                    "swt:text-xs swt:text-base-content/70 swt:max-sm:hidden swt:max-md:line-clamp-3"
                                prop.text project.description.Value
                            ]
                            Html.div [
                                prop.className "swt:flex swt:gap-1 swt:flex-wrap swt:flex-row"
                                prop.children [
                                    for tag in project.tag_list |> Array.truncate 3 do
                                        Html.span [
                                            prop.className "swt:badge swt:badge-neutral swt:badge-xs"
                                            prop.text tag
                                        ]
                                ]
                            ]
                        ]
                    ]
                Html.div [
                    prop.children [
                        Html.div [
                            prop.className "swt:flex swt:items-center swt:justify-end swt:gap-1"
                            prop.children [
                                Html.span [ prop.className "swt:iconify swt:fluent--star-12-regular" ]
                                Html.span [
                                    prop.className "swt:text-xs swt:text-base-content/70"
                                    prop.text (string project.star_count)
                                ]
                            ]
                        ]
                        match lastActivityString with
                        | Some _ ->
                            Html.div [
                                prop.className "swt:text-xs swt:text-base-content/60 swt:text-right"
                                prop.text (lastActivityString.Value)
                            ]
                        | None -> Html.none
                    ]
                ]
                match extraButtons with
                | Some fn ->
                    let buttons = fn project
                    RepoListRow.ActionbarButtons(buttons)
                | None -> Html.none
            ]
        ]
