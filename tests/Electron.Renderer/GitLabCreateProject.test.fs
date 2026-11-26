module ElectronRenderer.GitLabCreateProjectTests

open Fable.Core
open Fable.Core.JsInterop
open Arcitisa.Components.Api.GitLabApi
open Vitest

[<Emit("""
globalThis.__arcitisaApiOriginalFetch = globalThis.fetch;
globalThis.__arcitisaApiFetches = [];
globalThis.fetch = async (url, options) => {
  const body = options && options.body ? JSON.parse(options.body) : null;
  globalThis.__arcitisaApiFetches.push({ url, options, body });
  return $0({ url, body });
};
""")>]
let private installFetchSpy (respond: obj -> obj) : unit = jsNative

[<Emit("globalThis.__arcitisaApiFetches")>]
let private recordedFetches () : obj[] = jsNative

[<Emit("""
if (globalThis.__arcitisaApiOriginalFetch === undefined) {
  delete globalThis.fetch;
} else {
  globalThis.fetch = globalThis.__arcitisaApiOriginalFetch;
}
delete globalThis.__arcitisaApiOriginalFetch;
delete globalThis.__arcitisaApiFetches;
""")>]
let private removeFetchSpy () : unit = jsNative

[<Emit("$0[$1]")>]
let private getProperty<'T> (target: obj) (propertyName: string) : 'T = jsNative

[<Emit("Object.prototype.hasOwnProperty.call($0, $1)")>]
let private hasOwnProperty (target: obj) (propertyName: string) : bool = jsNative

[<Emit("new URL($0).searchParams.get($1)")>]
let private queryParam (url: string) (name: string) : string = jsNative

let private jsonResponse (headers: (string * string) list) (payload: obj) : obj =
    createObj [
        "ok" ==> true
        "status" ==> 200
        "headers"
        ==> createObj [
            "get"
            ==> (fun (name: string) ->
                headers
                |> List.tryFind (fun (key, _) -> key = name)
                |> Option.map snd
                |> Option.toObj
            )
        ]
        "json" ==> (fun () -> promise { return payload })
    ]

let private failedResponse (status: int) : obj =
    createObj [
        "ok" ==> false
        "status" ==> status
        "headers" ==> createObj [ "get" ==> (fun (_: string) -> null) ]
        "text" ==> (fun () -> promise { return "" })
    ]

let private withFetchSpy (respond: obj -> obj) (run: unit -> JS.Promise<unit>) = promise {
    installFetchSpy respond

    try
        do! run ()
    finally
        removeFetchSpy ()
}

let private expectNameRefused (expected: string option) (error: CreateProjectError) =
    match error with
    | CreateProjectError.NameRefused details -> Vitest.expect(details).toEqual (expected)
    | _ -> failwith "Expected NameRefused."

let private expectNotAllowed (expected: string option) (error: CreateProjectError) =
    match error with
    | CreateProjectError.NotAllowed details -> Vitest.expect(details).toEqual (expected)
    | _ -> failwith "Expected NotAllowed."

let private memberGroup id (name: string) (fullPath: string) (level: string option) : PublishMemberGroup = {
    id = id
    name = name
    full_path = fullPath
    project_creation_level = level
}

let private acceptingGroup id (name: string) (fullPath: string) : PublishAcceptingGroup = {
    id = id
    name = name
    full_path = fullPath
}

Vitest.describe (
    "classifyCreateProjectFailure",
    fun () ->
        Vitest.test (
            "reports a namespace error as not allowed whatever the status is",
            fun () ->
                classifyCreateProjectFailure 400 """{"message":{"namespace":["is not valid"]}}"""
                |> expectNotAllowed None

                classifyCreateProjectFailure 404 """{"message":{"namespace":["is not valid","is missing"]}}"""
                |> expectNotAllowed None
        )

        Vitest.test (
            "reports a forbidden response as not allowed with the text of the error when there is one",
            fun () ->
                classifyCreateProjectFailure
                    403
                    """{"error":["Your project limit is 5 projects! Please contact your administrator to increase it"]}"""
                |> expectNotAllowed (
                    Some "Your project limit is 5 projects! Please contact your administrator to increase it"
                )

                classifyCreateProjectFailure 403 """{"message":{"base":["not permitted"]}}"""
                |> expectNotAllowed None
        )

        Vitest.test (
            "reads a message that is an array of texts",
            fun () ->
                classifyCreateProjectFailure 403 """{"message":["first text","second text"]}"""
                |> expectNotAllowed (Some "first text; second text")
        )

        Vitest.test (
            "reports a refused name with the flattened field messages",
            fun () ->
                classifyCreateProjectFailure
                    400
                    """{"message":{"name":["has already been taken"],"path":["has already been taken"]}}"""
                |> expectNameRefused (Some "name has already been taken; path has already been taken")

                classifyCreateProjectFailure 409 """{"message":"Conflict"}"""
                |> expectNameRefused (Some "Conflict")

                classifyCreateProjectFailure 400 """{"message":{}}""" |> expectNameRefused None
        )

        Vitest.test (
            "maps every other status to a GitLab error",
            fun () ->
                match classifyCreateProjectFailure 401 """{"message":"401 Unauthorized"}""" with
                | CreateProjectError.Failed GitLabError.Unauthorized -> ()
                | _ -> failwith "Expected Failed Unauthorized."

                match classifyCreateProjectFailure 500 """{"message":"boom"}""" with
                | CreateProjectError.Failed(GitLabError.InvalidRequest text) ->
                    Vitest.expect(text).toContain ("HTTP 500")
                | _ -> failwith "Expected Failed InvalidRequest."
        )

        Vitest.test (
            "falls back to the status when the body is not JSON",
            fun () ->
                classifyCreateProjectFailure 400 "<html>Bad Request</html>"
                |> expectNameRefused None

                classifyCreateProjectFailure 403 "" |> expectNotAllowed None

                match classifyCreateProjectFailure 502 "<html>Bad Gateway</html>" with
                | CreateProjectError.Failed(GitLabError.InvalidRequest text) ->
                    Vitest.expect(text).toContain ("HTTP 502")
                | _ -> failwith "Expected Failed InvalidRequest."
        )
)

let private projectBodies () =
    recordedFetches () |> Array.map (fun fetched -> getProperty<obj> fetched "body")

Vitest.describe (
    "GitLabApi.CreateProject",
    fun () ->
        Vitest.test (
            "sends the namespace id when an owner is chosen and omits it otherwise",
            fun () ->
                withFetchSpy
                    (fun _ -> failedResponse 500)
                    (fun () -> promise {
                        let! _ =
                            GitLabApi.CreateProject(
                                "https://gitlab.example/",
                                "token-123",
                                {
                                    Name = "My ARC"
                                    NamespaceId = Some 7
                                }
                            )

                        let! _ =
                            GitLabApi.CreateProject(
                                "https://gitlab.example/",
                                "token-123",
                                { Name = "My ARC"; NamespaceId = None }
                            )

                        let bodies = projectBodies ()
                        Vitest.expect(bodies.Length).toBe (2)
                        Vitest.expect(getProperty<int> bodies[0] "namespace_id").toBe (7)
                        Vitest.expect(hasOwnProperty bodies[1] "namespace_id").toBe (false)
                    })
        )
)

let private user =
    createObj [
        "id" ==> 1
        "username" ==> "carol"
        "name" ==> "Carol"
        "email" ==> "carol@example.org"
    ]

let private restGroup (id: int) (path: string) (level: string) =
    createObj [
        "id" ==> id
        "name" ==> path
        "full_path" ==> path
        "web_url" ==> $"https://gitlab.example/{path}"
        "project_creation_level" ==> level
    ]

let private graphQlPage (nodes: obj[]) (endCursor: string option) =
    createObj [
        "data"
        ==> createObj [
            "currentUser"
            ==> createObj [
                "groups"
                ==> createObj [
                    "nodes" ==> nodes
                    "pageInfo"
                    ==> createObj [
                        "hasNextPage" ==> endCursor.IsSome
                        "endCursor" ==> Option.toObj endCursor
                    ]
                ]
            ]
        ]
    ]

let private graphQlNode (id: int) (path: string) =
    createObj [
        "id" ==> $"gid://gitlab/Group/{id}"
        "name" ==> path
        "fullPath" ==> path
    ]

/// Answers the requests of ListPublishOwners. Two pages of REST groups and two pages of GraphQL groups.
let private publishOwnersResponse (request: obj) : obj =
    let url = getProperty<string> request "url"
    let body = getProperty<obj> request "body"

    if url.EndsWith "/api/v4/user" then
        jsonResponse [] user
    elif url.Contains "/api/v4/groups" then
        if queryParam url "page" = "1" then
            jsonResponse [ "x-next-page", "2" ] [|
                restGroup 1 "alpha" "maintainer"
                restGroup 2 "beta" "owner"
            |]
        else
            jsonResponse [] [| restGroup 3 "gamma" "noone" |]
    elif url.EndsWith "/api/graphql" then
        if isNull (getProperty<obj> (getProperty<obj> body "variables") "after") then
            jsonResponse [] (graphQlPage [| graphQlNode 2 "beta" |] (Some "cursor-1"))
        else
            jsonResponse [] (graphQlPage [| graphQlNode 9 "shared" |] None)
    else
        failedResponse 404

Vitest.describe (
    "GitLabApi.ListPublishOwners",
    fun () ->
        Vitest.test (
            "merges the paged groups of the user with the paged groups that accept projects",
            fun () ->
                withFetchSpy
                    publishOwnersResponse
                    (fun () -> promise {
                        let! result = GitLabApi.ListPublishOwners("https://gitlab.example/", "token-123")

                        match result with
                        | Error _ -> failwith "Expected the owners to load."
                        | Ok owners ->
                            Vitest.expect(owners.username).toBe ("carol")

                            Vitest
                                .expect(
                                    owners.groups
                                    |> Array.map (fun group -> group.full_path, group.can_create_projects)
                                )
                                .toEqual (
                                    [|
                                        ("alpha", false)
                                        ("beta", true)
                                        ("gamma", false)
                                        ("shared", true)
                                    |]
                                )

                        let fetched = recordedFetches ()
                        let urlOf (request: obj) = getProperty<string> request "url"

                        let graphQlRequests =
                            fetched |> Array.filter (fun request -> (urlOf request).EndsWith "/api/graphql")

                        Vitest
                            .expect(
                                fetched
                                |> Array.filter (fun request -> (urlOf request).Contains "/api/v4/groups")
                            )
                            .toHaveLength (2)

                        Vitest.expect(graphQlRequests.Length).toBe (2)

                        let headersOf (request: obj) =
                            getProperty<obj> (getProperty<obj> request "options") "headers"

                        Vitest
                            .expect(getProperty<string> (headersOf graphQlRequests[0]) "Authorization")
                            .toContain ("Bearer token-123")

                        let firstBody = getProperty<obj> graphQlRequests[0] "body"
                        let secondBody = getProperty<obj> graphQlRequests[1] "body"
                        Vitest.expect(getProperty<string> firstBody "query").toContain ("CREATE_PROJECTS")

                        Vitest
                            .expect(getProperty<string> (getProperty<obj> secondBody "variables") "after")
                            .toBe ("cursor-1")
                    })
        )

        Vitest.test (
            "fails when GraphQL answers with errors",
            fun () ->
                let respond (request: obj) =
                    if (getProperty<string> request "url").EndsWith "/api/graphql" then
                        jsonResponse
                            []
                            (createObj [
                                "errors" ==> [| createObj [ "message" ==> "field is not allowed" ] |]
                            ])
                    else
                        publishOwnersResponse request

                withFetchSpy
                    respond
                    (fun () -> promise {
                        let! result = GitLabApi.ListPublishOwners("https://gitlab.example/", "token-123")

                        match result with
                        | Error(GitLabError.InvalidRequest text) ->
                            Vitest.expect(text).toContain ("field is not allowed")
                        | _ -> failwith "Expected the lookup to fail."
                    })
        )
)

Vitest.describe (
    "mergePublishGroups",
    fun () ->
        Vitest.test (
            "flags the member groups that accept projects",
            fun () ->
                let merged =
                    mergePublishGroups [|
                        memberGroup 1 "Alpha" "alpha" (Some "maintainer")
                        memberGroup 2 "Beta" "beta" (Some "owner")
                    |] [| acceptingGroup 2 "Beta" "beta" |]

                Vitest
                    .expect(merged |> Array.map (fun group -> group.id, group.can_create_projects))
                    .toEqual ([| (1, false); (2, true) |])

                Vitest.expect(merged[0].project_creation_level).toEqual (Some "maintainer")
        )

        Vitest.test (
            "adds an accepting group that is missing from the member groups",
            fun () ->
                let merged =
                    mergePublishGroups [| memberGroup 1 "Alpha" "alpha" (Some "owner") |] [|
                        acceptingGroup 1 "Alpha" "alpha"
                        acceptingGroup 7 "Shared" "shared"
                    |]

                Vitest.expect(merged.Length).toBe (2)
                let extra = merged |> Array.find (fun group -> group.id = 7)
                Vitest.expect(extra.can_create_projects).toBe (true)
                Vitest.expect(extra.project_creation_level).toEqual (None)
                Vitest.expect(extra.full_path).toBe ("shared")
        )

        Vitest.test (
            "sorts by full path without regard to case",
            fun () ->
                let merged =
                    mergePublishGroups [|
                        memberGroup 1 "zeta" "Zeta" None
                        memberGroup 2 "alpha" "alpha" None
                        memberGroup 3 "Beta" "Beta" None
                    |] [||]

                Vitest.expect(merged |> Array.map _.full_path).toEqual ([| "alpha"; "Beta"; "Zeta" |])
        )

        Vitest.test (
            "lists a group once even when both lists repeat it",
            fun () ->
                let merged =
                    mergePublishGroups [|
                        memberGroup 1 "Alpha" "alpha" None
                        memberGroup 1 "Alpha" "alpha" None
                    |] [|
                        acceptingGroup 1 "Alpha" "alpha"
                        acceptingGroup 1 "Alpha" "alpha"
                    |]

                Vitest.expect(merged |> Array.map _.id).toEqual ([| 1 |])
        )
)
