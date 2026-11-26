open System.IO
open System.Threading.Tasks

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Giraffe
open Saturn
open Arcitisa.Components.Shared

open Fable.Remoting.Server
open Fable.Remoting.Giraffe
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Configuration

open ARCtrl
open ARCtrl.Json

let serviceApi: IServiceAPIv1 = {
    getAppVersion = fun () -> async { return Version.AssemblyVersion }
}

open Microsoft.AspNetCore.Http

open Database

let templateApi credentials =
    let templateUrl =
        @"https://github.com/nfdi4plants/swate-template-registry/releases/download/latest/templates_v2.0.0.json"

    {
        getTemplates =
            fun () -> async {
                let! templates = ARCtrl.Template.Web.getTemplates (None)
                let templatesJson = ARCtrl.Json.Templates.toJsonString 0 (Array.ofSeq templates)
                return templatesJson
            }

        getTemplateById =
            fun id -> async {
                let! templates = ARCtrl.Template.Web.getTemplates (None)
                let template = templates |> Seq.find (fun t -> t.Id = System.Guid(id))
                let templateJson = Template.toCompressedJsonString 0 template
                return templateJson
            }
    }

let testApi (ctx: HttpContext) : ITestAPI = {
    test =
        fun () -> async {
            let c =
                let settings = ctx.GetService<IConfiguration>()

                let credentials: Helper.Neo4JCredentials = {
                    User = settings.[Helper.Neo4JCredentials.UserVarString]
                    Pw = settings.[Helper.Neo4JCredentials.PwVarString]
                    BoltUrl = settings.[Helper.Neo4JCredentials.UriVarString]
                    DatabaseName = settings.[Helper.Neo4JCredentials.DBNameVarString]
                }

                credentials
            //let exmp = OntologyDB.Queries.Term(c).getByAdvancedTermSearch(termName="insturment~ -Shimadzu")
            return "Info", "nothing active here"
        }
    postTest =
        fun (termName) -> async {
            let c =
                let settings = ctx.GetService<IConfiguration>()

                let credentials: Helper.Neo4JCredentials = {
                    User = settings.[Helper.Neo4JCredentials.UserVarString]
                    Pw = settings.[Helper.Neo4JCredentials.PwVarString]
                    BoltUrl = settings.[Helper.Neo4JCredentials.UriVarString]
                    DatabaseName = settings.[Helper.Neo4JCredentials.DBNameVarString]
                }

                credentials

            let exmp =
                Term.Term(c).searchByName (termName, sourceOntologyName = Term.AnyOfOntology.Single "ms")

            return "Info", sprintf "%A" (exmp |> Seq.length)
        }
}

let createITemplateApiv1 credentials =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.fromValue (templateApi credentials)
    |> Remoting.withDiagnosticsLogger (printfn "%A")
    |> Remoting.withErrorHandler API.Helper.errorHandler
    |> Remoting.buildHttpHandler

let createIServiceAPIv1 =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.fromValue serviceApi
    |> Remoting.withDiagnosticsLogger (printfn "%A")
    |> Remoting.withErrorHandler API.Helper.errorHandler
    |> Remoting.OpenAPI.withDocs Fable.Remoting.OpenAPI.OpenApi.options

let createTestApi =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.fromContext testApi
    |> Remoting.withDiagnosticsLogger (printfn "%A")
    |> Remoting.withErrorHandler API.Helper.errorHandler
    |> Remoting.OpenAPI.withDocs Fable.Remoting.OpenAPI.OpenApi.options

let getMessage () = "Hello from SAFE!"

let getNeo4JCredentials (ctx: HttpContext) =
    let settings = ctx.GetService<IConfiguration>()

    try
        let credentials: Helper.Neo4JCredentials = {
            User = settings.[Helper.Neo4JCredentials.UserVarString]
            Pw = settings.[Helper.Neo4JCredentials.PwVarString]
            BoltUrl = settings.[Helper.Neo4JCredentials.UriVarString]
            DatabaseName = settings.[Helper.Neo4JCredentials.DBNameVarString]
        }

        credentials
    with _ ->
        let credentials: Helper.Neo4JCredentials = {
            User = settings.[System.Environment.GetEnvironmentVariable(Helper.Neo4JCredentials.UserVarString)]
            Pw = settings.[System.Environment.GetEnvironmentVariable(Helper.Neo4JCredentials.PwVarString)]
            BoltUrl = settings.[System.Environment.GetEnvironmentVariable(Helper.Neo4JCredentials.UriVarString)]
            DatabaseName = settings.[System.Environment.GetEnvironmentVariable(Helper.Neo4JCredentials.DBNameVarString)]
        }

        credentials

//// https://cors-test.codehappy.dev/?url=https%3A%2F%2Fswate.nfdi4plants.org%2Fapi%2FIOntologyAPIv2%2FgetAllOntologies&method=get
///// Enable CORS. Makes external access of ARCitISA API possible
//let allow_cors =
//    pipeline {
//        set_header "Access-Control-Allow-Origin" "*"
//        set_header "Access-Control-Allow-Methods" "*"
//        set_header "Access-Control-Allow-Headers" "*"
//    }

let topLevelRouter = router {
    //pipe_through allow_cors
    get "/test/test1" (htmlString "<h1>Hi this is test response 1</h1>")
    get "/test/hello" (getMessage () |> json)

    forward
        @""
        (fun next ctx ->
            let credentials = getNeo4JCredentials ctx
            API.IOntologyAPI.V1.createIOntologyApi credentials next ctx
        )

    forward
        @""
        (fun next ctx ->
            let credentials = getNeo4JCredentials ctx
            API.IOntologyAPI.V2.createIOntologyApi credentials next ctx
        )

    forward
        @""
        (fun next ctx ->
            let credentials = getNeo4JCredentials ctx
            API.IOntologyAPI.V3.createIOntologyApi credentials next ctx
        )

    forward
        @""
        (fun next ctx ->
            let credentials = getNeo4JCredentials ctx
            createITemplateApiv1 credentials next ctx
        )

    //
    forward @"" (fun next ctx -> createIServiceAPIv1 next ctx)

    forward @"" (fun next ctx -> createTestApi next ctx)
}

open Microsoft.AspNetCore.StaticFiles
open Microsoft.AspNetCore.Cors.Infrastructure

/// Enable CORS. Makes external access of ARCitISA API possible
let cors_config =
    fun (b: CorsPolicyBuilder) -> b.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin() |> ignore

/// Allows serving .yaml files directly
let config (app: IApplicationBuilder) =
    let provider = new FileExtensionContentTypeProvider()
    provider.Mappings.Add(".yaml", "application/x-yaml")

    app.UseStaticFiles(
        let opt = new StaticFileOptions()
        opt.ContentTypeProvider <- provider
        opt
    )

let app = application {
    url "http://*:5000"
    app_config config
    use_router topLevelRouter
    use_cors "CORS_CONFIG" cors_config
    memory_cache
    use_static "public"
    use_gzip
    logging (fun (builder: ILoggingBuilder) -> builder.SetMinimumLevel(LogLevel.Debug) |> ignore)
}

app.ConfigureAppConfiguration(
    System.Action<Microsoft.Extensions.Hosting.HostBuilderContext, IConfigurationBuilder>(fun ctx config ->
        config.AddUserSecrets("6de80bdf-2a05-4cf7-a1a8-d08581dfa887") |> ignore
        config.AddJsonFile("dev.json", true, true) |> ignore
        config.AddJsonFile("production.json", true, true) |> ignore
    )
)
|> run
