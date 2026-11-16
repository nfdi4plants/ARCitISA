namespace Swate.Components.Composite.TextEditorSample

open Fable.Core
open Feliz

open Swate.Components.Composite.TextEditor
open Swate.Components.Composite.TextEditor.Types
open Swate.Components.Composite.TextEditor.SchemaValidation

module internal TextEditorSampleData =

    let studySchema =
        """{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://example.org/schemas/study.schema.json",
  "title": "Study",
  "type": "object",
  "required": ["identifier", "title", "contacts"],
  "properties": {
    "identifier": {
      "type": "string",
      "pattern": "^[A-Za-z0-9_-]+$",
      "description": "may only contain letters, digits, - and _"
    },
    "title": { "type": "string", "minLength": 1 },
    "status": { "enum": ["planned", "running", "finished"] },
    "submissionDate": { "type": "string", "format": "date" },
    "contacts": {
      "type": "array",
      "minItems": 1,
      "items": { "$ref": "person.schema.json" }
    },
    "keywords": {
      "type": "array",
      "items": { "type": "string" },
      "uniqueItems": true
    }
  },
  "additionalProperties": false
}
"""

    let personSchema =
        """{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://example.org/schemas/person.schema.json",
  "title": "Person",
  "type": "object",
  "required": ["givenName", "familyName"],
  "properties": {
    "givenName": { "type": "string", "minLength": 1 },
    "familyName": { "type": "string", "minLength": 1 },
    "email": { "type": "string", "format": "email" },
    "orcid": {
      "type": "string",
      "pattern": "^[0-9]{4}-[0-9]{4}-[0-9]{4}-[0-9]{3}[0-9X]$",
      "description": "must be an ORCID iD such as 0000-0002-1825-0097"
    },
    "roles": {
      "type": "array",
      "items": { "enum": ["author", "contact", "reviewer"] }
    }
  },
  "additionalProperties": false
}
"""

    let studyYaml =
        """# A small study description with six mistakes. Edit it, or edit the schemas.
identifier: dilution-series
title: Dilution series
status: runing
submissionDate: 2026-13-01
contacts:
  - givenName: Jane
    familyNmae: Doe
    email: jane.doe.example.org
    orcid: 0000-0002-1825-0097
    roles: [author, editor]
keywords: [proteomics, proteomics]
"""

    let studyJson =
        """{
  "identifier": "dilution-series",
  "title": "Dilution series",
  "status": "runing",
  "submissionDate": "2026-13-01",
  "contacts": [
    {
      "givenName": "Jane",
      "familyNmae": "Doe",
      "email": "jane.doe.example.org",
      "orcid": "0000-0002-1825-0097",
      "roles": ["author", "editor"]
    }
  ],
  "keywords": ["proteomics", "proteomics"]
}
"""

    let newSchema (n: int) =
        $"""{{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://example.org/schemas/schema-{n}.schema.json",
  "type": "object"
}}
"""

module private SchemaPlaygroundHelpers =

    type SchemaTab = { Key: int; Text: string }

    let parse (text: string) : Result<obj, string> =
        try
            Ok(JS.JSON.parse text)
        with e ->
            Error e.Message

    let idOf (schema: obj) : string option =
        (unbox<SchemaIndex.SchemaNode> schema).``$id``

    /// The file name at the end of the $id, or "Schema n" when the text has none.
    let label (index: int) (tab: SchemaTab) =
        match parse tab.Text |> Result.toOption |> Option.bind idOf with
        | Some id -> id.TrimEnd('/').Split('/') |> Array.last
        | None -> $"Schema {index + 1}"

    /// The validator for the tabs, or why there is none.
    let buildValidator (tabs: SchemaTab[]) (rootIndex: int) : Result<Validator.SchemaValidator, string> =
        let parsed = tabs |> Array.map (fun t -> parse t.Text)

        match parsed |> Array.tryFindIndex Result.isError with
        | Some i ->
            match parsed.[i] with
            | Error message -> Error $"{label i tabs.[i]} is not valid JSON: {message}"
            | Ok _ -> Error "unreachable"
        | None ->
            let schemas = parsed |> Array.map (fun r -> Result.defaultValue null r)

            match idOf schemas.[min rootIndex (schemas.Length - 1)] with
            | None -> Error "The root schema has no $id."
            | Some rootId -> Validator.create schemas rootId

open SchemaPlaygroundHelpers

[<Erase; Mangle(false)>]
type internal TextEditorSample =

    [<ReactComponent>]
    static member DirtySave() =
        let status, setStatus = React.useState "Saved"
        let handleRef = React.useRef<TextEditorHandle option> None

        let onSave (_: TextEditorSnapshot) : JS.Promise<bool> = promise {
            setStatus "Saving..."
            do! Promise.sleep 100
            return true
        }

        let onDirtyChange (dirty: bool) =
            setStatus (if dirty then "Unsaved changes" else "Saved")

        Html.div [
            prop.className "swt:flex swt:flex-col swt:gap-2 swt:p-4"
            prop.children [
                Html.div [
                    prop.className "swt:flex swt:items-center swt:gap-2"
                    prop.children [
                        Html.span [ prop.text status ]
                        Html.button [
                            prop.className "swt:btn swt:btn-sm"
                            prop.text "Save"
                            prop.onClick (fun _ -> handleRef.current |> Option.iter (fun h -> h.save () |> ignore))
                        ]
                    ]
                ]
                Html.div [
                    prop.className "swt:h-64"
                    prop.children [
                        TextEditor.TextEditor(
                            "inmemory://samples/notes.md",
                            "# Notes\n\nSome text.\n",
                            fileName = "notes.md",
                            onSave = onSave,
                            onDirtyChange = onDirtyChange,
                            handleRef = handleRef
                        )
                    ]
                ]
            ]
        ]

    [<ReactComponent>]
    static member Languages() =
        let editor (name: string) (content: string) =
            Html.div [
                prop.className "swt:h-48 swt:min-w-0 swt:flex-1"
                prop.children [
                    TextEditor.TextEditor($"inmemory://samples/languages/{name}", content, fileName = name)
                ]
            ]

        Html.div [
            prop.className "swt:flex swt:gap-2 swt:p-4"
            prop.children [
                editor "notes.md" "# Title\n\nA *short* note.\n"
                editor "config.yaml" "name: demo\nitems:\n  - a\n  - b\n"
                editor "run.py" "def main():\n    print('hello')\n"
            ]
        ]

    [<ReactComponent>]
    static member ThemeSwitch() =
        Html.div [
            prop.className "swt:h-64 swt:p-4"
            prop.children [
                TextEditor.TextEditor("inmemory://samples/data.json", "{\n  \"a\": 1\n}\n", fileName = "data.json")
            ]
        ]

    /// Edit JSON Schemas and a document side by side. The schemas reference each other by $id.
    /// schemas, yamlDocument and jsonDocument set the starting content.
    [<ReactComponent>]
    static member SchemaPlayground(?schemas: string[], ?yamlDocument: string, ?jsonDocument: string) =
        let initialTabs () =
            defaultArg schemas [|
                TextEditorSampleData.studySchema
                TextEditorSampleData.personSchema
            |]
            |> Array.mapi (fun i text -> { Key = i; Text = text })

        let tabs, setTabs = React.useStateWithUpdater (initialTabs ())
        let active, setActive = React.useState 0
        let rootIndex, setRootIndex = React.useState 0
        let format, setFormat = React.useState "yaml"

        let yamlText, setYamlText =
            React.useState (defaultArg yamlDocument TextEditorSampleData.studyYaml)

        let jsonText, setJsonText =
            React.useState (defaultArg jsonDocument TextEditorSampleData.studyJson)

        let diagnostics, setDiagnostics = React.useState ([||]: TextEditorDiagnostic[])
        let schemaHandle = React.useRef<TextEditorHandle option> None
        let documentHandle = React.useRef<TextEditorHandle option> None
        let nextKey = React.useRef 1000
        let pendingSchema = React.useRef<int option> None

        let active = min active (tabs.Length - 1)
        let rootIndex = min rootIndex (tabs.Length - 1)

        let validatorResult =
            React.useMemo ((fun () -> buildValidator tabs rootIndex), [| box tabs; box rootIndex |])

        let validator = validatorResult |> Result.toOption
        let documentText = if format = "yaml" then yamlText else jsonText
        let activeIsRoot = active = rootIndex

        // Typing in a schema rebuilds the validator 300 ms after the last change.
        let onSchemaChange () =
            pendingSchema.current |> Option.iter JS.clearTimeout
            let key = tabs.[active].Key

            pendingSchema.current <-
                Some(
                    JS.setTimeout
                        (fun () ->
                            schemaHandle.current
                            |> Option.iter (fun h ->
                                let text = h.getText ()

                                setTabs (fun current ->
                                    current
                                    |> Array.map (fun t -> if t.Key = key then { t with Text = text } else t)
                                )
                            )
                        )
                        300
                )

        let onDocumentChange () =
            documentHandle.current
            |> Option.iter (fun h ->
                if format = "yaml" then
                    setYamlText (h.getText ())
                else
                    setJsonText (h.getText ())
            )

        let addSchema () =
            let key = nextKey.current
            nextKey.current <- key + 1

            setTabs (fun current ->
                Array.append current [|
                    {
                        Key = key
                        Text = TextEditorSampleData.newSchema key
                    }
                |]
            )

            setActive tabs.Length

        let removeSchema () =
            if tabs.Length > 1 then
                setTabs (fun current -> current |> Array.removeAt active)
                setActive (max 0 (active - 1))

                if rootIndex >= active && rootIndex > 0 then
                    setRootIndex (rootIndex - 1)

        let tabButton (i: int) (tab: SchemaTab) =
            Html.button [
                prop.key tab.Key
                prop.role "tab"
                prop.className [
                    "swt:tab"
                    if i = active then
                        "swt:tab-active"
                ]
                prop.text (label i tab + (if i = rootIndex then " (root)" else ""))
                prop.onClick (fun _ -> setActive i)
            ]

        let status =
            match validatorResult with
            | Ok v ->
                Html.div [
                    prop.className "swt:text-sm swt:flex swt:flex-col swt:gap-1"
                    prop.children [
                        Html.div [
                            prop.className "swt:text-success"
                            prop.testId "schema-status"
                            prop.text $"Schemas compiled. Root: {v.RootId} ({v.SchemaIds.Length} schemas)"
                        ]
                        for w in v.Warnings do
                            Html.div [ prop.className "swt:text-warning"; prop.text $"Ajv: {w}" ]
                    ]
                ]
            | Error message ->
                Html.div [
                    prop.className "swt:text-sm swt:text-error"
                    prop.testId "schema-status"
                    prop.text $"No validation: {message}"
                ]

        let rawErrors =
            match validator with
            | None -> Html.none
            | Some v ->
                let content =
                    match Lint.rawErrors v format documentText with
                    | Error message -> [ Html.li [ prop.text message ] ]
                    | Ok [||] -> [ Html.li [ prop.text "Valid." ] ]
                    | Ok errors -> [
                        for e in errors do
                            let path = if e.instancePath = "" then "/" else e.instancePath

                            Html.li [
                                prop.text $"{path}  {e.keyword}: {e.message}  ({e.schemaPath})"
                            ]
                      ]

                Html.details [
                    prop.className "swt:text-xs"
                    prop.children [
                        Html.summary [
                            prop.className "swt:cursor-pointer"
                            prop.text "Raw Ajv errors"
                        ]
                        Html.ul [
                            prop.className "swt:font-mono swt:flex swt:flex-col swt:gap-0.5 swt:mt-1"
                            prop.children content
                        ]
                    ]
                ]

        Html.div [
            prop.className "swt:grid swt:grid-cols-1 swt:lg:grid-cols-2 swt:gap-4 swt:p-4 swt:h-[48rem]"
            prop.children [
                Html.section [
                    prop.className "swt:flex swt:flex-col swt:gap-2 swt:min-h-0"
                    prop.children [
                        Html.div [
                            prop.className "swt:flex swt:flex-wrap swt:items-center swt:gap-2"
                            prop.children [
                                Html.div [
                                    prop.role "tablist"
                                    prop.className "swt:tabs swt:tabs-box swt:tabs-sm"
                                    prop.children (tabs |> Array.mapi tabButton)
                                ]
                                Html.button [
                                    prop.className "swt:btn swt:btn-sm"
                                    prop.text "Add schema"
                                    prop.onClick (fun _ -> addSchema ())
                                ]
                                Html.button [
                                    prop.className "swt:btn swt:btn-sm"
                                    prop.text "Remove schema"
                                    prop.disabled (tabs.Length < 2)
                                    prop.onClick (fun _ -> removeSchema ())
                                ]
                                Html.button [
                                    prop.className "swt:btn swt:btn-sm"
                                    prop.text "Use as root"
                                    prop.disabled activeIsRoot
                                    prop.onClick (fun _ -> setRootIndex active)
                                ]
                            ]
                        ]
                        Html.div [
                            prop.className
                                "swt:flex-1 swt:min-h-0 swt:border swt:border-base-300 swt:rounded-box swt:overflow-hidden"
                            prop.children [
                                TextEditor.TextEditor(
                                    $"inmemory://playground/schemas/{tabs.[active].Key}.schema.json",
                                    tabs.[active].Text,
                                    fileName = "schema.json",
                                    onChange = onSchemaChange,
                                    handleRef = schemaHandle
                                )
                            ]
                        ]
                        status
                    ]
                ]
                Html.section [
                    prop.className "swt:flex swt:flex-col swt:gap-2 swt:min-h-0"
                    prop.children [
                        Html.div [
                            prop.className "swt:join"
                            prop.children [
                                for f in [ "yaml"; "json" ] do
                                    Html.button [
                                        prop.key f
                                        prop.className [
                                            "swt:btn swt:btn-sm swt:join-item"
                                            if f = format then
                                                "swt:btn-primary"
                                        ]
                                        prop.text (f.ToUpperInvariant())
                                        prop.onClick (fun _ ->
                                            if f <> format then
                                                setDiagnostics [||]
                                                setFormat f
                                        )
                                    ]
                            ]
                        ]
                        Html.div [
                            prop.className
                                "swt:flex-1 swt:min-h-0 swt:border swt:border-base-300 swt:rounded-box swt:overflow-hidden"
                            prop.children [
                                TextEditor.TextEditor(
                                    $"inmemory://playground/study.{format}",
                                    documentText,
                                    fileName = $"study.{format}",
                                    ?schemaValidator = validator,
                                    onChange = onDocumentChange,
                                    onDiagnostics = setDiagnostics,
                                    handleRef = documentHandle
                                )
                            ]
                        ]
                        Html.div [
                            prop.className "swt:text-sm swt:font-semibold"
                            prop.testId "diagnostic-count"
                            prop.text $"{diagnostics.Length} diagnostics"
                        ]
                        Html.ul [
                            prop.className "swt:text-sm swt:flex swt:flex-col swt:gap-1 swt:max-h-48 swt:overflow-auto"
                            prop.testId "diagnostic-list"
                            prop.children [
                                for d in diagnostics do
                                    Html.li [
                                        prop.className "swt:flex swt:gap-2"
                                        prop.children [
                                            Html.span [
                                                prop.className "swt:font-mono swt:opacity-70 swt:shrink-0"
                                                prop.text $"{d.line}:{d.column}"
                                            ]
                                            Html.span [
                                                prop.className (if d.source = "syntax" then "swt:text-warning" else "")
                                                prop.text d.message
                                            ]
                                        ]
                                    ]
                            ]
                        ]
                        rawErrors
                    ]
                ]
            ]
        ]
