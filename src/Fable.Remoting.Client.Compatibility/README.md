# Fable.Remoting.Client compatibility source

Unmodified F# source from the Fable.Remoting.Client 7.32.0 NuGet package, under the
accompanying MIT license. This project references the adjacent patched SimpleJson project.
Fable compiles NuGet sources before project sources, so Remoting must also be a project
reference to see the patched dependency in the correct compile order.

Tests: [tests/Fable.Remoting.Client.Compatibility](../../tests/Fable.Remoting.Client.Compatibility)
calls a proxy end to end against a fake `XMLHttpRequest`.

See [the SimpleJson compatibility notes](../Fable.SimpleJson.Compatibility/README.md) for the
upstream status, the removal checklist and the NuGet publishing limitation.
