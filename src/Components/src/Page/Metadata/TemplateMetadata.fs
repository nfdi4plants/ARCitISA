namespace Swate.Components.Page.Metadata

open Fable.Core
open Feliz
open ARCtrl
open Swate.Components
open Swate.Components.Primitive.LayoutComponents
open Swate.Components.Page.Metadata.FormComponents
open Swate.Components.Page.Metadata.FormComponents.PersonsInput

[<Erase; Mangle(false)>]
type TemplateMetadata =

    [<ReactComponent(true)>]
    static member TemplateMetadata
        // 👀 If you rename these variables, ensure that the names are forwarded for lazy loading in `src\Components\src\Metadata\ArcFileMetadata.fs` as well!
        (template: ARCtrl.Template, setTemplate: ARCtrl.Template -> unit, ?onImportPersons: unit -> JS.Promise<Person[]>) =
        LayoutComponents.Section [
            LayoutComponents.BoxedField(
                "Template Metadata",
                content = [
                    TextInput.TextInput(template.Id.ToString(), (fun _ -> ()), label = "Identifier", disabled = true)
                    TextInput.TextInput(
                        template.Name,
                        (fun value ->
                            template.Name <- value
                            setTemplate template
                        ),
                        label = "Name"
                    )
                    TextInput.TextInput(
                        template.Description,
                        (fun value ->
                            template.Description <- value
                            setTemplate template
                        ),
                        label = "Description",
                        isArea = true
                    )
                    TextInput.TextInput(
                        template.Organisation.ToString(),
                        (fun value ->
                            template.Organisation <- Organisation.ofString value
                            setTemplate template
                        ),
                        label = "Organisation"
                    )
                    TextInput.TextInput(
                        template.Version,
                        (fun value ->
                            template.Version <- value
                            setTemplate template
                        ),
                        label = "Version"
                    )
                    DateTimeInput.DateTimeInput(
                        template.LastUpdated.ToString("yyyy-MM-ddTHH:mm"),
                        (fun value ->
                            match System.DateTime.TryParse(value) with
                            | true, parsed ->
                                template.LastUpdated <- parsed
                                setTemplate template
                            | false, _ -> ()
                        ),
                        label = "Last Updated"
                    )
                    OntologyAnnotationInput.OntologyAnnotationsInput(
                        template.Tags,
                        (fun annotations ->
                            template.Tags <- annotations
                            setTemplate template
                        ),
                        label = "Tags"
                    )
                    OntologyAnnotationInput.OntologyAnnotationsInput(
                        template.EndpointRepositories,
                        (fun annotations ->
                            template.EndpointRepositories <- annotations
                            setTemplate template
                        ),
                        label = "Endpoint Repositories"
                    )
                    PersonsInput.PersonsInput(
                        template.Authors,
                        (fun persons ->
                            template.Authors <- persons
                            setTemplate template
                        ),
                        label = "Authors",
                        ?onImportPersons = onImportPersons
                    )
                ]
            )
        ]
