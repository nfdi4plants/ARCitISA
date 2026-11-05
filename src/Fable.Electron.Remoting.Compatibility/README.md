# Fable.Electron.Remoting compatibility source

Unmodified F# source from Fable.Electron.Remoting 7.2.0, upstream commit
`0108f1306724b08b2433dfef470fca7e6db5d8c9`, under the accompanying MIT license.
This project references the adjacent patched SimpleJson project. Fable compiles NuGet sources
before project sources, so the IPC remoting library must also be a project reference to see
the patched dependency in the correct order.

It has no separate tests. It serializes through the same SimpleJson converter, which
[tests/Fable.SimpleJson.Compatibility](../../tests/Fable.SimpleJson.Compatibility) covers.

See [the SimpleJson compatibility notes](../Fable.SimpleJson.Compatibility/README.md) for the
upstream status, the removal checklist and the NuGet publishing limitation.
