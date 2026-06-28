# SDR# SDK reference assemblies (stubs)

Modern SDR# (the Airspy .NET build) ships as a **single-file, self-contained
`SDRSharp.exe`** — the managed `SDRSharp.Common` / `SDRSharp.Radio` /
`SDRSharp.PanView` assemblies are embedded in the executable and are **not**
available as separate, redistributable DLLs.

To compile a plugin against them, this folder provides minimal **reference
("stub") assemblies** that declare only the public API surface the plugin uses
(the `ISharpPlugin` / `ISharpControl` interfaces, the `IIQProcessor` stream-hook
interfaces, `ProcessorType`, and the `Complex` struct). The plugin is compiled
against these; at run time inside SDR# the references bind to the host's real
assemblies (same simple assembly names, which are not strong-named).

The API surface here was verified against the open SDR# source mirror
(`SDRSharpR/SDRSharp` on GitHub). If a future SDR# release changes the plugin
API, update these stubs to match.

CI builds these three projects into `../libs/` before building the plugin; for a
local build, run `dotnet build` on each (or just build the plugin — see the root
README).
