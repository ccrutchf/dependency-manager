# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

Targets .NET 10 (`net10.0`). Solution file: `DependencyManager.sln`. Assembly name is `depend` (not `DependencyManager`).

- Build: `dotnet build` (Debug) or `dotnet build -c Release` (Release enables single-file self-contained publish settings)
- Run CLI against the example config: `dotnet run --project src/DependencyManager -- plan --config examples/packages.yaml`
- Run all tests: `dotnet test`
- Run one test class: `dotnet test --filter "FullyQualifiedName~PlannerTests"`
- Run one test: `dotnet test --filter "FullyQualifiedName~PlannerTests.Topo_sort_places_dependencies_first"`
- Publish a release binary (mirrors `.github/workflows/release.yml`): `dotnet publish src/DependencyManager -c Release -r linux-x64 -o out/linux-x64` (supported RIDs: `linux-x64`, `linux-arm64`)

`TreatWarningsAsErrors=true` — warnings will break the build.

## Architecture

The pipeline has three stages: **load → plan → run**. Each stage has a single entry point and is unit-testable without the others.

**Config (`src/DependencyManager/Config/`)** — YAML is a top-level map of named *blocks*. Each block has filter keys (`platform`, `architecture`, `version`), optional `ppas`, and per-manager package maps (`apt`, `snap`, `flatpak`, `deb`, `pip`, `pipx`, `cargo`, `nvm`, `script`, `vscode`). `BlockYamlConverter` is a hand-rolled `IYamlTypeConverter` rather than plain POCO deserialization because it needs to distinguish filter keys from provider keys in the same flat map and emit a warning for unknown keys. If you add a new top-level block key, update both `FilterKeys`/`ProviderKeys` sets there and the `Block` record in `ConfigSchema.cs`.

**Planner (`Runner/Planner.cs`)** — Filters blocks via `BlockFilter.Matches` (with `"all"` as the wildcard and version matching via `StartsWith`), flattens all matching package sections into a `(ManagerKind, Id)` dictionary (so later blocks overwrite earlier ones on the same key), dedupes PPAs, then topologically sorts by `Spec.Dependencies`. Dependency lookup is keyed by `Spec.Name ?? Id`, so a package can override its lookup name; unknown dependency names are silently skipped. Cycles throw.

**Runner (`Runner/Runner.cs` + `Managers/`)** — `Runner` iterates the sorted plan, finds a matching available `IPackageManager`, bootstraps it on first use (apt: adds PPAs then `apt-get update`; flatpak: adds flathub remote; snap: no-op), then `IsInstalledAsync` → `InstallAsync`. Managers shell out through `Util/ProcessRunner.cs` and are selected by `Kind` + `IsAvailable()` (binary presence checks like `/usr/bin/apt-get`). Add a new provider by implementing `IPackageManager`, adding a `ManagerKind` enum value, wiring it into `InstallCommand.BuildManagers`, and extending `BlockYamlConverter`'s provider keys + the `Block` record.

**Root requirement** — `Util/RootCheck.PlanRequiresSudo` returns true if the plan has any PPAs, any apt/snap/deb package, or any package with an explicit `user_scope: false`. `InstallCommand` re-invokes under `sudo` via `Util/Sudo` in that case; euid is checked via `libc.geteuid` P/Invoke. Flatpak/pip/pipx default to `--user` scope and only escalate to sudo when `user_scope: false` is set. `plan` and `test` never need root.

## CLI entry

`Program.cs` uses `System.CommandLine` 2.0 beta 4. Subcommands: `plan`, `install` (supports `--fail-fast`), `test`, `list`, `update`. `plan`/`install`/`test` accept `--config`/`-c` (default `packages.yaml`); `list` and `update` take no config. Commands live in `Commands/` as static classes with `Run`/`RunAsync`.

## Tests

xUnit + Shouldly. Tests construct `ConfigFile`/`Block` records directly rather than parsing YAML, except `ConfigLoaderTests` which exercises the YAML pipeline end-to-end. There are no integration tests that hit real package managers — the `IPackageManager` implementations are tested manually.
