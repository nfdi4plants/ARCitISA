namespace Arcitisa.Components.Shared

module URLs =

    [<Literal>]
    let PRODUCTION_URL_ALPHA = @"https://swate-alpha.nfdi4plants.org"

    [<Literal>]
    let PRODUCTION_URL = @"https://swate.nfdi4plants.org"

    module Data =

        module SelectorFormat =

            [<Literal>]
            let csv = @"https://datatracker.ietf.org/doc/html/rfc7111"

    [<RequireQualifiedAccessAttribute>]
    module Docs =

        [<Literal>]
        let IOntologyAPIv3 = "/api/IOntologyAPIv3/docs"


    type Helpdesk =
        static member Url = @"https://support.nfdi4plants.org"

        static member UrlArcitisaTopic = Helpdesk.Url + "/?topic=Tools_Swate"

        static member UrlOntologyTopic = Helpdesk.Url + "/?topic=Metadata_OntologyUpdate"

        static member UrlTemplateTopic = Helpdesk.Url + "/?topic=Metadata_SwateTemplate"

    let CONTACT = Helpdesk.Url

    [<LiteralAttribute>]
    let AnnotationPrinciplesUrl = @"https://nfdi4plants.github.io/AnnotationPrinciples/"

    [<LiteralAttribute>]
    let ARCITISA_WIKI =
        @"https://nfdi4plants.github.io/nfdi4plants.knowledgebase/arcitisa/"

    [<LiteralAttribute>]
    let ArcitisaRepo = @"https://github.com/nfdi4plants/ARCitISA"

    [<LiteralAttribute>]
    let CSBTwitterUrl = @"https://twitter.com/cs_biology"

    [<LiteralAttribute>]
    let NFDITwitterUrl = @"https://twitter.com/nfdi4plants"

    [<Literal>]
    let NFDIGitHubUrl = @"https://github.com/nfdi4plants"

    [<LiteralAttribute>]
    let CSBWebsiteUrl = @"https://csb.bio.uni-kl.de/"

    [<LiteralAttribute>]
    let NfdiWebsite = @"https://nfdi4plants.org"

    [<LiteralAttribute>]
    let OntobeeOntologyPrefix = @"https://ontobee.org/ontology/"
