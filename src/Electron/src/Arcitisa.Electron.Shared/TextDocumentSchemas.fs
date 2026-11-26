/// JSON Schemas for ARC files that the text editor checks, and which file each one belongs to.
/// The rules follow the ISA-ARC Workspace specification (3.0.0-draft.2, "The validation_packages.yml file").
module Arcitisa.Electron.Shared.TextDocumentSchemas

open Fable.Core
open Arcitisa.Components.Composite.TextEditor.SchemaValidation

/// The ARC-relative path of the validation packages file.
let validationPackagesPath = ".arc/validation_packages.yml"

/// Unknown keys are errors, so a misspelled key gets a "Did you mean" hint instead of being ignored.
/// The specification names the version key `specification`, its example and ARCtrl use `arc_specification`,
/// so both are allowed. Unique package names are a rule of the specification that JSON Schema cannot express.
let validationPackagesSchema =
    """{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "urn:arcitisa:schemas:validation-packages",
  "title": "validation_packages.yml",
  "type": "object",
  "required": ["validation_packages"],
  "properties": {
    "arc_specification": { "type": "string", "minLength": 1 },
    "specification": { "type": "string", "minLength": 1 },
    "validation_packages": {
      "type": "array",
      "items": {
        "title": "a validation package",
        "type": "object",
        "required": ["name"],
        "properties": {
          "name": { "type": "string", "minLength": 1 },
          "version": {
            "type": "string",
            "pattern": "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$",
            "description": "must be a version in the form MAJOR.MINOR.PATCH, such as 1.0.0"
          }
        },
        "additionalProperties": false
      }
    }
  },
  "additionalProperties": false
}"""

let private validationPackages =
    lazy
        (match
            Validator.create [| JS.JSON.parse validationPackagesSchema |] "urn:arcitisa:schemas:validation-packages"
         with
         | Ok validator -> Some validator
         | Error message ->
             JS.console.error (
                 $"The validation packages schema does not compile, so the file opens without validation: {message}"
             )

             None)

/// The validator for an ARC-relative path, if the file has a schema. The same instance comes back each time,
/// so the editor does not lint again on every render. A schema that does not compile gives None, so the file still opens.
let tryFindValidator (arcRelativePath: string) : Validator.SchemaValidator option =
    if TextDocumentHelper.isSameDocumentPath arcRelativePath validationPackagesPath then
        validationPackages.Force()
    else
        None
