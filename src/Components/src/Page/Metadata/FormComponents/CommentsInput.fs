namespace Arcitisa.Components.Page.Metadata.FormComponents

open Fable.Core
open Browser.Types
open Feliz
open ARCtrl

[<Erase; Mangle(false)>]
type CommentsInput =

    [<ReactComponent>]
    static member private CommentInput
        (
            comment: Comment,
            setter: Comment -> unit,
            ?label: string,
            ?rmv: MouseEvent -> unit,
            ?keyValidator: string -> Result<unit, string>
        ) =
        Html.div [
            prop.children [
                if label.IsSome then
                    Html.label [ prop.className "swt:label"; prop.text label.Value ]
                Html.div [
                    prop.className "swt:flex swt:flex-row swt:gap-2 swt:relative"
                    prop.children [
                        TextInput.TextInput(
                            comment.Name |> Option.defaultValue "",
                            (fun value ->
                                comment.Name <- (if value = "" then None else Some value)
                                setter comment
                            ),
                            placeholder = "comment name",
                            ?validator = keyValidator
                        )
                        TextInput.TextInput(
                            comment.Value |> Option.defaultValue "",
                            (fun value ->
                                comment.Value <- (if value = "" then None else Some value)
                                setter comment
                            ),
                            placeholder = "comment"
                        )
                        if rmv.IsSome then
                            Helpers.deleteButton rmv.Value
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member CommentsInput(comments: ResizeArray<Comment>, setter: ResizeArray<Comment> -> unit, ?label: string) =
        let keyValidator (name: string) =
            let isDuplicate =
                comments
                |> Seq.exists (fun comment -> comment.Name.IsSome && comment.Name.Value = name)

            if isDuplicate then
                Error "Comment names must be unique."
            else
                Ok()

        InputSequence.InputSequence(
            comments,
            Comment,
            setter,
            (fun (value, setValue, remove) ->
                CommentsInput.CommentInput(value, setValue, rmv = remove, keyValidator = keyValidator)
            ),
            ?label = label
        )
