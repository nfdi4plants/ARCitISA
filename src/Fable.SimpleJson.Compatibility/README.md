# Fable.SimpleJson compatibility source

Temporary, patched copy of Fable.SimpleJson 3.24.0 (MIT, see [LICENSE](LICENSE)). It keeps
Fable.Remoting working with Fable 5.1 and later until a fixed upstream release exists.

## Action required: check whether this workaround can be removed

Check this whenever you update Fable or Fable.Remoting, or every few months.

1. **Check whether [Fable.SimpleJson](https://github.com/Zaid-Ajaj/Fable.SimpleJson) has moved to
   [fable-hub](https://github.com/fable-hub).** If it has not moved and has no new release, stop
   here. The workaround stays.
2. **Check whether the fix was merged and released.** Look at
   [PR #111](https://github.com/Zaid-Ajaj/Fable.SimpleJson/pull/111) (or its successor on fable-hub)
   and [Fable.Remoting #394](https://github.com/Zaid-Ajaj/Fable.Remoting/issues/394).
   PR #111 only fixes `option`. Lists are fixed only if `UnionType` is also matched after
   `ListType` (see [What is patched](#what-is-patched)). The tests in step 3 check both.
3. **Verify the release with the isolated tests before deleting anything.**
   - Set the fixed `Fable.SimpleJson` version in [Directory.Packages.props](../../Directory.Packages.props).
   - In [Fable.SimpleJson.Tests.fsproj](../../tests/Fable.SimpleJson.Compatibility/Fable.SimpleJson.Tests.fsproj),
     replace the `ProjectReference` with `<PackageReference Include="Fable.SimpleJson" />`.
   - In [Fable.Remoting.Client.Tests.fsproj](../../tests/Fable.Remoting.Client.Compatibility/Fable.Remoting.Client.Tests.fsproj),
     replace the `ProjectReference` with `<PackageReference Include="Fable.Remoting.Client" />`
     and `<PackageReference Include="Fable.SimpleJson" />`.
   - Run `dotnet run --project ./build/Build.fsproj -- test run compatibility`.
     If any test fails, the release is not sufficient. Keep the workaround and report the failing
     test upstream.
4. **If all tests pass, delete these projects and switch back to the packages:**
   - Delete `src/Fable.SimpleJson.Compatibility`, `src/Fable.Remoting.Client.Compatibility` and
     `src/Fable.Electron.Remoting.Compatibility`.
   - [Directory.Packages.props](../../Directory.Packages.props): keep the fixed `Fable.SimpleJson` version.
     Bump `Fable.Remoting.Client` and `Fable.Electron.Remoting` if newer releases depend on it.
     Remove `Fable.Parsimmon`, `Fable.Remoting.MsgPack` and `Fable.Browser.XMLHttpRequest`, which
     only the compatibility projects use.
   - [Swate.Components.fsproj](../Components/src/Swate.Components.fsproj): replace the
     `Fable.Remoting.Client.Compatibility` `ProjectReference` with
     `<PackageReference Include="Fable.Remoting.Client" />` and add
     `<PackageReference Include="Fable.SimpleJson" />`, so the published NuGet package also
     requires the fixed version.
   - [Swate.Electron.Shared.fsproj](../Electron/src/Swate.Electron.Shared/Swate.Electron.Shared.fsproj):
     replace the `Fable.Electron.Remoting.Compatibility` `ProjectReference` with
     `<PackageReference Include="Fable.Electron.Remoting" />`.
   - [Client.fsproj](../Client/Client.fsproj): add `<PackageReference Include="Fable.SimpleJson" />` back.
   - Delete `tests/Fable.SimpleJson.Compatibility` and `tests/Fable.Remoting.Client.Compatibility`,
     or port them upstream first (see [Tests](#tests)). Then remove the `compatibility` test target
     from [Test.fs](../../build/Actions/Test.fs) (`Run.compatibility` and its use in `Run.All`),
     [Build.fs](../../build/Build.fs) and [ProjectInfo.fs](../../build/ProjectInfo.fs), and the
     compatibility step from [run-tests.yml](../../.github/workflows/run-tests.yml).
   - Keep [JsonWireFormat.test.fs](../Components/src/Api/JsonWireFormat.test.fs). It tests the
     Swate API DTOs, not the library.
   - Re-enable NuGet publishing by removing the `nuget` matrix exclusion in
     [release-pipeline.yml](../../.github/workflows/release-pipeline.yml) after verifying that a
     consumer of the packed NuGet package compiles with the fixed upstream dependencies.

## What is patched

Fable 5.1+ reflects `option` and `list` as unions, as .NET does: `FSharpType.IsUnion` returns
true for both. The upstream converter checks `UnionType` before `OptionType` and `ListType`, so
both were treated as ordinary unions. Request payloads were sent in the wrong format, and
responses and persisted JSON (for example `Guid list` in local storage) failed to decode.

- `TypeInfo.Converter.fs`: the `UnionType` match arm is moved below every named-type check,
  directly above `AsyncType`. No other arm changes position.
- `SimpleJson.fs`: the unused `open Fable.Import` is removed, because that namespace no longer
  exists in Fable.Core 5.

All other files are unmodified copies from the 3.24.0 NuGet package.

## Why three projects

Fable compiles NuGet package sources before project sources. A packaged Fable.Remoting.Client
would compile before this project and fail to find it. The unmodified Remoting sources are
therefore copied as projects next to this one:

- [Fable.Remoting.Client.Compatibility](../Fable.Remoting.Client.Compatibility/README.md),
  referenced by Swate.Components (and through it by Client).
- [Fable.Electron.Remoting.Compatibility](../Fable.Electron.Remoting.Compatibility/README.md),
  referenced by Swate.Electron.Shared.

These projects keep the original package and assembly names, but are not packable. The npm
package of Swate.Components contains the fix, because it is compiled in. The NuGet package does
not: `dotnet pack` declares a dependency on the stock `Fable.Remoting.Client 7.32.0`
instead. NuGet publishing is therefore disabled through a matrix exclusion in
[release-pipeline.yml](../../.github/workflows/release-pipeline.yml) until the fixed upstream
dependencies can be shipped.

## Tests

The tests reference only the compatibility project they cover. Each fixture type checks either
`option` or `list`, so a failing test points at the type that broke.

- [tests/Fable.SimpleJson.Compatibility](../../tests/Fable.SimpleJson.Compatibility): converter
  tests for the wire format of options, lists and unions.
- [tests/Fable.Remoting.Client.Compatibility](../../tests/Fable.Remoting.Client.Compatibility):
  calls a real proxy end to end. `FakeXmlHttpRequest.fs` replaces the browser `XMLHttpRequest`,
  so no server is needed.

Run both with `dotnet run --project ./build/Build.fsproj -- test run compatibility`. CI runs them
in the `components` job.

Fable.Electron.Remoting has no separate tests. It serializes through the same converter.

To port the tests upstream, copy the test file and its fixture types. Upstream Fable.SimpleJson
runs Mocha with its own `testCase` helper, so only the assertion calls need rewriting.
