module internal Swate.Components.Page.Metadata.FormComponents.PersonsInputSample

open ARCtrl

let emptyPerson () = Person()

let importedPersons () = [|
    Person(firstName = "Ada", lastName = "Lovelace", affiliation = "Research Institute")
    Person(firstName = "Grace", lastName = "Hopper")
|]

let importedPersonDetails () = [|
    Person(
        firstName = "Ada",
        lastName = "Lovelace",
        midInitials = "A",
        orcid = "0000-0002-1825-0097",
        affiliation = "Research Institute",
        email = "ada@example.org",
        phone = "+44 123",
        fax = "+44 456",
        address = "12 Example Street\nLondon",
        roles =
            ResizeArray [
                OntologyAnnotation(
                    "Investigator",
                    tsr = "CREDIT",
                    tan = "CREDIT:0000001",
                    comments = ResizeArray [ Comment("Role note", "Primary role") ]
                )
            ],
        comments = ResizeArray [ Comment("Contribution", "Study design") ]
    )
|]
