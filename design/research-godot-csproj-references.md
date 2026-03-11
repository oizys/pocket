# Research: Godot .csproj External Project References

## 1. Can a Godot .csproj use `<ProjectReference>` to include an external .csproj?

**Yes.** A Godot C# project uses `Godot.NET.Sdk` instead of `Microsoft.NET.Sdk`, but it is still a standard MSBuild project file. `<ProjectReference>` works identically to any .NET project. The referenced project (`Pockets.Core`) must not reference Godot-specific assemblies, which it doesn't. This is the recommended community approach.

## 2. Known build system conflicts?

**Minimal.** Key friction points:
- The Godot editor can **regenerate the .csproj** in certain circumstances (e.g., "Create C# Solution" button), which may strip manual `<ProjectReference>` entries. After initial setup, avoid that button. Normal builds preserve manual edits.
- Building from CLI requires the `Godot.NET.Sdk` NuGet package to be resolvable (run `dotnet restore` or build once from the editor first).
- Godot's source generators only apply to the Godot project itself, not referenced projects — this is correct behavior.
- Assembly output goes to `.godot/mono/temp/bin/`; referenced project DLLs are copied there automatically.

## 3. Does the Godot editor need to know about the external project?

**No.** The editor delegates all C# compilation to `dotnet build`, which follows `<ProjectReference>` entries natively. External `.cs` files won't appear in Godot's Script panel (expected — use your IDE). The Godot editor auto-generates its own `.sln`; you can either add Core to it or use your repo-root `Pockets.sln` for IDE work.

## 4. Alternative approaches?

- **DLL reference**: Works but requires separate build step, no auto-rebuild. Inferior.
- **Local NuGet package**: Overkill for a single repo. Must re-pack after every change.
- **Shared project (.shproj)**: Can conflict with Godot source generators. Not recommended.
- **Recommendation: `<ProjectReference>` is the correct approach.** Alternatives are fallbacks only.

## 5. .NET 8 TFM compatibility?

- Godot 4.0–4.2 used .NET 6. **Godot 4.3+ targets .NET 8**, so both `Pockets.Core` and `Pockets.Godot` can target `net8.0` with no mismatch.
- `System.Collections.Immutable` (used by Core) is in the .NET 8 BCL — no extra NuGet needed.
- Nullable, ImplicitUsings — no conflicts.
- AOT/trimming at export time is unlikely to be an issue with functional/LINQ code, but worth testing.
- **The `Godot.NET.Sdk` version in the .csproj must match the installed Godot version** (e.g., `Godot.NET.Sdk/4.6.1`).

## Practical .csproj template

```xml
<Project Sdk="Godot.NET.Sdk/4.6.1">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Pockets.Core/Pockets.Core.csproj" />
  </ItemGroup>
</Project>
```

## Main Gotchas

1. Verify `<ProjectReference>` survives Godot editor operations (adding scripts, etc.)
2. Data file paths differ between TUI and Godot working directories — make configurable
3. Check exact `Godot.NET.Sdk` version after installing Godot 4.6.1
4. First build from the Godot editor needed to restore NuGet and generate `.godot/mono/`

## Confidence Note

Based on training data through early 2025. Godot 4.6.1 specifics are extrapolated from 4.3/4.4. Recommend verifying the SDK version and .csproj regeneration behavior against 4.6.1 release notes.
