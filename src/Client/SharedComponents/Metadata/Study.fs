module Components.Metadata.Study

open Arcitisa.Components.Shared
open Feliz
open ARCtrl
open Components
open Components.Forms
open System

let Main
    (study: ArcStudy, assignedAssays: ArcAssay list, setArcStudy: (ArcStudy * ArcAssay list) -> unit, model: Model.Model) =
    Generic.Section [
        Generic.BoxedField(
            "Study Metadata",
            content = [
                FormComponents.TextInput(
                    study.Identifier,
                    (fun s ->
                        let nextStudy = IdentifierSetters.setStudyIdentifier s study
                        setArcStudy (nextStudy, assignedAssays)
                    ),
                    "Identifier",
                    validator =
                        (fun s ->
                            try
                                ARCtrl.Helper.Identifier.checkValidCharacters s
                                Ok()
                            with ex ->
                                Error ex.Message
                        ),
                    disabled = Generic.isDisabledInARCitect model.PersistentStorageState.Host
                )
                FormComponents.TextInput(
                    Option.defaultValue "" study.Title,
                    (fun s ->
                        study.Title <- s |> Option.whereNot String.IsNullOrWhiteSpace
                        setArcStudy (study, assignedAssays)
                    ),
                    "Title"
                )
                FormComponents.TextInput(
                    Option.defaultValue "" study.Description,
                    (fun s ->
                        study.Description <- s |> Option.whereNot String.IsNullOrWhiteSpace
                        setArcStudy (study, assignedAssays)
                    ),
                    "Description",
                    isarea = true
                )
                FormComponents.PersonsInput(
                    study.Contacts,
                    (fun persons ->
                        study.Contacts <- ResizeArray(persons)
                        setArcStudy (study, assignedAssays)
                    ),
                    model.PersistentStorageState.IsARCitect,
                    "Contacts"
                )
                FormComponents.PublicationsInput(
                    study.Publications,
                    (fun pubs ->
                        study.Publications <- ResizeArray(pubs)
                        setArcStudy (study, assignedAssays)
                    ),
                    "Publications"
                )
                FormComponents.DateTimeInput(
                    Option.defaultValue "" study.SubmissionDate,
                    (fun s ->
                        study.SubmissionDate <- s |> Option.whereNot String.IsNullOrWhiteSpace
                        setArcStudy (study, assignedAssays)
                    ),
                    "Submission Date"
                )
                FormComponents.DateTimeInput(
                    Option.defaultValue "" study.PublicReleaseDate,
                    (fun s ->
                        study.PublicReleaseDate <- s |> Option.whereNot String.IsNullOrWhiteSpace
                        setArcStudy (study, assignedAssays)
                    ),
                    "Public ReleaseDate"
                )
                FormComponents.OntologyAnnotationsInput(
                    study.StudyDesignDescriptors,
                    (fun oas ->
                        study.StudyDesignDescriptors <- ResizeArray(oas)
                        setArcStudy (study, assignedAssays)
                    ),
                    "Study Design Descriptors"
                )
                //FormComponents.TextInputs(
                //    Array.ofSeq study.RegisteredAssayIdentifiers,
                //    "Registered Assay Identifiers",
                //    fun rais ->
                //        study.RegisteredAssayIdentifiers <- ResizeArray(rais)
                //        (study, assignedAssays) |> Study |> Spreadsheet.UpdateArcFile |> SpreadsheetMsg |> dispatch
                //)
                FormComponents.CommentsInput(
                    study.Comments,
                    (fun comments ->
                        study.Comments <- ResizeArray(comments)
                        setArcStudy (study, assignedAssays)
                    ),
                    "Comments"
                )
            ]
        )
    ]
