module Arcitisa.Components.Page.MockData.DataHub

open Arcitisa.Components.Page.DataHub.DataHubTypes
open Arcitisa.Components.Api.GitLabApi


let private ns id name path = {
    id = id
    name = name
    kind = "group"
    full_path = path
}

let groups: GroupDto array = [|
    {
        id = 100
        name = "NFDI4Plants"
        full_path = "nfdi4plants"
        web_url = "https://git.nfdi4plants.org/nfdi4plants"
        avatar_url = Some "https://picsum.photos/48/48?g=1"
    }
    {
        id = 101
        name = "Plant Data"
        full_path = "plant-data"
        web_url = "https://git.nfdi4plants.org/plant-data"
        avatar_url = Some "https://picsum.photos/48/48?g=2"
    }
|]

let yourRepos: ExploreProjectDto array = [|
    {
        id = 1
        name = "metabolomics-arc"
        path_with_namespace = "kevin/metabolomics-arc"
        name_with_namespace = "kevin / metabolomics-arc"
        description = Some "LC-MS metabolomics workflow"
        web_url = "https://git.nfdi4plants.org/kevin/metabolomics-arc"
        http_url_to_repo = "https://git.nfdi4plants.org/kevin/metabolomics-arc.git"
        ssh_url_to_repo = Some "ssh://git@git.nfdi4plants.org/kevin/metabolomics-arc.git"
        avatar_url = Some "https://picsum.photos/40/40?r=1"
        visibility = Some "private"
        star_count = 7
        created_at = System.DateTime(2026, 1, 10, 9, 0, 0) |> Some
        last_activity_at = System.DateTime(2026, 3, 12, 11, 0, 0) |> Some
        tag_list = [| "metabolomics"; "arc" |]
        ``namespace`` = ns 1 "kevin" "kevin"
    }
    {
        id = 2
        name = "rnaseq-arc"
        path_with_namespace = "kevin/rnaseq-arc"
        name_with_namespace = "kevin / rnaseq-arc"
        description = Some "RNA-seq analysis templates"
        web_url = "https://git.nfdi4plants.org/kevin/rnaseq-arc"
        http_url_to_repo = "https://git.nfdi4plants.org/kevin/rnaseq-arc.git"
        ssh_url_to_repo = Some "ssh://git@git.nfdi4plants.org/kevin/rnaseq-arc.git"
        avatar_url = Some "https://picsum.photos/40/40?r=2"
        visibility = Some "private"
        star_count = 11
        created_at = System.DateTime(2025, 11, 5, 10, 0, 0) |> Some
        last_activity_at = System.DateTime(2026, 3, 1, 9, 0, 0) |> Some
        tag_list = [| "rnaseq"; "arc" |]
        ``namespace`` = ns 1 "kevin" "kevin"
    }
    {
        id = 3
        name = "proteomics-arc"
        path_with_namespace = "kevin/proteomics-arc"
        name_with_namespace = "kevin / proteomics-arc"
        description = Some "Proteomics starter ARC"
        web_url = "https://git.nfdi4plants.org/kevin/proteomics-arc"
        http_url_to_repo = "https://git.nfdi4plants.org/kevin/proteomics-arc.git"
        ssh_url_to_repo = Some "ssh://git@git.nfdi4plants.org/kevin/proteomics-arc.git"
        avatar_url = None
        visibility = Some "private"
        star_count = 3
        created_at = System.DateTime(2025, 10, 9, 10, 0, 0) |> Some
        last_activity_at = System.DateTime(2026, 2, 27, 9, 0, 0) |> Some
        tag_list = [| "proteomics"; "arc" |]
        ``namespace`` = ns 1 "kevin" "kevin"
    }
|]

let mostStarred: ExploreProjectDto array = [|
    {
        id = 10
        name = "plant-ontology"
        path_with_namespace = "nfdi4plants/plant-ontology"
        name_with_namespace = "NFDI4Plants / Plant Ontology"
        description = Some "Controlled vocabulary and mappings"
        web_url = "https://git.nfdi4plants.org/nfdi4plants/plant-ontology"
        http_url_to_repo = "https://git.nfdi4plants.org/nfdi4plants/plant-ontology.git"
        ssh_url_to_repo = Some "ssh://git@git.nfdi4plants.org/nfdi4plants/plant-ontology.git"
        avatar_url = Some "https://picsum.photos/40/40?r=10"
        visibility = Some "public"
        star_count = 233
        created_at = System.DateTime(2024, 1, 15, 9, 0, 0) |> Some
        last_activity_at = System.DateTime(2026, 3, 11, 14, 0, 0) |> Some
        tag_list = [| "ontology"; "plants" |]
        ``namespace`` = ns 100 "NFDI4Plants" "nfdi4plants"
    }
    {
        id = 11
        name = "arc-spec"
        path_with_namespace = "nfdi4plants/arc-spec"
        name_with_namespace = "NFDI4Plants / ARC Specification"
        description = Some "ARC specification"
        web_url = "https://git.nfdi4plants.org/nfdi4plants/arc-spec"
        http_url_to_repo = "https://git.nfdi4plants.org/nfdi4plants/arc-spec.git"
        ssh_url_to_repo = Some "ssh://git@git.nfdi4plants.org/nfdi4plants/arc-spec.git"
        avatar_url = Some "https://picsum.photos/40/40?r=11"
        visibility = Some "public"
        star_count = 190
        created_at = System.DateTime(2024, 5, 20, 9, 0, 0) |> Some
        last_activity_at = System.DateTime(2026, 3, 9, 12, 0, 0) |> Some
        tag_list = [| "arc"; "specification" |]
        ``namespace`` = ns 100 "NFDI4Plants" "nfdi4plants"
    }
    {
        id = 12
        name = "arc-templates"
        path_with_namespace = "plant-data/arc-templates"
        name_with_namespace = "Plant Data / ARC Templates"
        description = Some "Reusable ARC templates"
        web_url = "https://git.nfdi4plants.org/plant-data/arc-templates"
        http_url_to_repo = "https://git.nfdi4plants.org/plant-data/arc-templates.git"
        ssh_url_to_repo = Some "ssh://git@git.nfdi4plants.org/plant-data/arc-templates.git"
        avatar_url = Some "https://picsum.photos/40/40?r=12"
        visibility = Some "public"
        star_count = 99
        created_at = System.DateTime(2024, 8, 1, 9, 0, 0) |> Some
        last_activity_at = System.DateTime(2026, 3, 5, 10, 0, 0) |> Some
        tag_list = [| "templates"; "arc" |]
        ``namespace`` = ns 101 "Plant Data" "plant-data"
    }
|]

let orgRepos: Map<int, ExploreProjectDto array> =
    Map.ofArray [|
        100, [| mostStarred[0]; mostStarred[1] |]
        101,
        [|
            mostStarred[2]
            {
                id = 13
                name = "growth-model"
                path_with_namespace = "plant-data/growth-model"
                name_with_namespace = "Plant Data / Growth Model"
                description = Some "Plant growth model experiments"
                web_url = "https://git.nfdi4plants.org/plant-data/growth-model"
                http_url_to_repo = "https://git.nfdi4plants.org/plant-data/growth-model.git"
                ssh_url_to_repo = Some "ssh://git@git.nfdi4plants.org/plant-data/growth-model.git"
                avatar_url = None
                visibility = Some "internal"
                star_count = 27
                created_at = System.DateTime(2025, 9, 15, 9, 0, 0) |> Some
                last_activity_at = System.DateTime(2026, 3, 1, 10, 0, 0) |> Some
                tag_list = [| "modeling"; "growth" |]
                ``namespace`` = ns 101 "Plant Data" "plant-data"
            }
        |]
    |]
