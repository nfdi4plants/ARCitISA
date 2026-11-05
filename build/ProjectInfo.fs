module ProjectInfo

open System.IO

let DEFINE_SWATE_ENVIRONMENT_FABLE = [ "--define"; "SWATE_ENVIRONMENT" ]

module ProjectPaths =

    let sharedPath = Path.GetFullPath "src/Shared"
    let serverPath = Path.GetFullPath "src/Server"
    let clientPath = Path.GetFullPath "src/Client"
    let componentsPath = Path.GetFullPath "src/Components"
    let electronPath = Path.GetFullPath "src/Electron"
    let deployPath = Path.GetFullPath "deploy"
    let nugetDeployPath = Path.GetFullPath "nupkgs"
    let nugetSln = Path.GetFullPath "Nuget.sln"

    let sharedTestsPath = Path.GetFullPath "tests/Shared"
    let serverTestsPath = Path.GetFullPath "tests/Server"
    let clientTestsPath = Path.GetFullPath "tests/Client"
    let electronCoreTestsPath = Path.GetFullPath "tests/Electron.Core"
    let electronRendererTestsPath = Path.GetFullPath "tests/Electron.Renderer"
    let componentTestsPath = Path.GetFullPath "src/Components"

    let simpleJsonCompatibilityTestsPath =
        Path.GetFullPath "tests/Fable.SimpleJson.Compatibility"

    let remotingCompatibilityTestsPath =
        Path.GetFullPath "tests/Fable.Remoting.Client.Compatibility"

    let dockerComposePath = Path.GetFullPath ".db/docker-compose.yml"
    let dockerFilePath = Path.GetFullPath "build/Dockerfile.publish"

    let devManifestPath = Path.GetFullPath "build/manifest.xml"

let developmentUrl = "https://localhost:3000"

let gitOwner = "nfdi4plants"
let project = "ARCitISA"
let projectRepo = $"https://github.com/{gitOwner}/{project}"

let componentsPackageName = "@nfdi4plants/swate-components"
